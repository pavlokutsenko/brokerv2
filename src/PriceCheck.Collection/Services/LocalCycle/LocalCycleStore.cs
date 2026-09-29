using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly NativeSqlite _db;
    private readonly Dictionary<string, LocalTrader> _traders = [];
    private readonly CollectorProfile _profile;
    private string _serverUrl;
    private Guid _sourceProfile;
    private readonly Func<DateTimeOffset> _clock;
    private string _session = "";
    private double _recheckHours = 24;
    private CycleRadarPool _collectionBoundary=CycleRadarPool.ForCity("Giran");
    public string DatabasePath { get; }
    public event Action<string>? Message;
    public LocalCycleStore(CollectorProfile profile, string? root = null, Func<DateTimeOffset>? clock = null)
    {
        _profile = new(){Id=profile.Id,Name=profile.Name,City="Giran",ServerUrl=profile.ServerUrl};
        _serverUrl=profile.ServerUrl;_sourceProfile=profile.Id;_clock = clock ?? (() => DateTimeOffset.UtcNow);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CycleQueue.Key(profile.Name))))[..24];
        DatabasePath = Path.Combine(root ?? Path.Combine(CycleQueue.Root, "markets"), hash, "market.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _db = new(DatabasePath);
        _db.Execute("""
            CREATE TABLE IF NOT EXISTS traders (trader_key TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checks (trader_key TEXT PRIMARY KEY REFERENCES traders(trader_key), revision INTEGER NOT NULL, dirty INTEGER NOT NULL, reason TEXT NOT NULL, last_error TEXT);
            CREATE TABLE IF NOT EXISTS route (pass_id TEXT NOT NULL, ordinal INTEGER NOT NULL, trader_key TEXT NOT NULL REFERENCES traders(trader_key), revision INTEGER NOT NULL, done INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(pass_id,trader_key));
            CREATE TABLE IF NOT EXISTS snapshots (snapshot_id TEXT PRIMARY KEY, trader_key TEXT NOT NULL, captured_at INTEGER NOT NULL, revision INTEGER NOT NULL, payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS snapshot_trader_time ON snapshots(trader_key,captured_at);
            CREATE TABLE IF NOT EXISTS outbox (operation_id TEXT PRIMARY KEY, kind TEXT NOT NULL, url TEXT NOT NULL, payload TEXT NOT NULL, next_attempt INTEGER NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, last_error TEXT, trader_key TEXT);
            CREATE INDEX IF NOT EXISTS outbox_due ON outbox(next_attempt);
            CREATE TABLE IF NOT EXISTS latest_broker_delivery (id INTEGER PRIMARY KEY CHECK(id=1), epoch_id TEXT NOT NULL, captured_at INTEGER NOT NULL, traders INTEGER NOT NULL, accepted INTEGER NOT NULL DEFAULT 0);
            PRAGMA user_version=1;
            """);
        if(!_db.Query("PRAGMA table_info(outbox)").Any(row=>row[1]=="trader_key"))_db.Execute("ALTER TABLE outbox ADD COLUMN trader_key TEXT");
        _db.Execute("CREATE INDEX IF NOT EXISTS outbox_trader_kind ON outbox(trader_key,kind)");
        _db.Execute("CREATE INDEX IF NOT EXISTS outbox_kind_due ON outbox(kind,next_attempt)");
        var health = _db.Query("PRAGMA quick_check");
        if (health.Count != 1 || health[0][0] != "ok") throw new InvalidDataException($"Local market database failed integrity check: {DatabasePath}");
        foreach (var row in _db.Query("SELECT data FROM traders"))
        {
            var trader = JsonSerializer.Deserialize<LocalTrader>(row[0]!, Json) ?? throw new InvalidDataException("Invalid local trader");
            trader.Composition=trader.Composition.GroupBy(pair=>pair.Key.ToLowerInvariant()).ToDictionary(g=>g.Key,g=>g.Max(p=>p.Value));
            _traders.Add(trader.Key, trader);
        }
        _db.Execute("PRAGMA journal_size_limit=8388608");
        MaintainStorage();
    }
    public void BeginSession(string session, bool preservePass=false)
    {
        lock (_sync)
        {
            _session = session;
            if(!preservePass)
            { _pass="";_remaining.Clear();_admitted.Clear();_admittedGenerations.Clear();_readPass.Clear();_initialCount=0;_admissionCount=0;_admissionLimit=0; }
            Transaction(() => {
                if(!preservePass)_db.Command("DELETE FROM route");
                foreach (var trader in _traders.Values)
                {
                    trader.ObjectId = 0; trader.Session = session;
                    // Explicit closure remains history, but a new local ObjectID never proves reopening.
                    trader.SeenOpenThisSession = false; trader.ClosedThisSession = false;
                    Save(trader);
                }
            });
        }
    }
    public void SetRecheckHours(double value)
    {
        lock (_sync) _recheckHours = double.IsFinite(value) ? Math.Clamp(value, 0.1, 8760) : 24;
    }
    public void Configure(CollectorProfile profile)
    {
        if(CycleQueue.Key(profile.Name)!=CycleQueue.Key(_profile.Name))throw new InvalidOperationException("Market scope is immutable.");
        lock(_sync){_serverUrl=profile.ServerUrl;_sourceProfile=profile.Id;SetRecheckHours(profile.RecheckHours);}
    }
    public IReadOnlyList<string> Keys { get { lock (_sync) return _traders.Keys.ToArray(); } }
    private void Save(LocalTrader t)
    {
        _db.Command("INSERT INTO traders(trader_key,data) VALUES(?,?) ON CONFLICT(trader_key) DO UPDATE SET data=excluded.data", t.Key, JsonSerializer.Serialize(t, Json));
        _db.Command("INSERT INTO checks VALUES(?,?,?,?,?) ON CONFLICT(trader_key) DO UPDATE SET revision=excluded.revision,dirty=excluded.dirty,reason=excluded.reason,last_error=excluded.last_error", t.Key, t.Revision, t.Dirty, t.Reason, t.Error);
    }
    private void Invalidate(LocalTrader t, string reason, DateTimeOffset at)
    { t.Dirty = true; t.Revision++; t.VerificationToken=Guid.NewGuid().ToString("N"); t.Reason = reason; t.ChangedAt = at; t.Error = null; t.NeedsServerHistory=false; }
    private void Transaction(Action action)
    {
        try{_db.Transaction(action);}
        catch
        {
            _traders.Clear();
            foreach(var row in _db.Query("SELECT data FROM traders"))
            {var t=JsonSerializer.Deserialize<LocalTrader>(row[0]!,Json)!;_traders.Add(t.Key,t);}
            _remaining.Clear();_remaining.AddRange(_db.Query("SELECT trader_key FROM route WHERE pass_id=? AND done=0 ORDER BY ordinal",_pass).Select(row=>row[0]!));
            throw;
        }
    }
    private bool NeedsRead(LocalTrader t) => t.Dirty || t.LastRead is null || t.LastRead.Value.AddHours(_recheckHours) <= _clock();
    private static double Distance(double x, double y, double a, double b) => Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));
    public void Dispose()
    {
        _senderStop.Cancel();
        try{_sender?.GetAwaiter().GetResult();}catch(OperationCanceledException){}
        lock(_sync)_db.Dispose();_senderStop.Dispose();
    }
}

public sealed class LocalTrader
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public bool HasPosition { get; set; }
    public int KioskType { get; set; }
    public long ObjectId { get; set; }
    public string Session { get; set; } = "";
    public bool ClosedThisSession { get; set; }
    public bool SeenOpenThisSession { get; set; }
    public bool ConfirmedClosed { get; set; }
    public DateTimeOffset StateAt { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset? LastRead { get; set; }
    public string? LastSnapshotId { get; set; }
    public bool NeedsServerHistory { get; set; }
    public double CheckedX { get; set; }
    public double CheckedY { get; set; }
    public long Revision { get; set; } = 1;
    public string VerificationToken { get; set; } = "1";
    public bool Dirty { get; set; } = true;
    public string Reason { get; set; } = "New shop";
    public DateTimeOffset ChangedAt { get; set; }
    public string? Error { get; set; }
    public Dictionary<string,int> Composition { get; set; } = [];
    public DateTimeOffset? ServerRequiredAt { get; set; }
    public DateTimeOffset? ServerClosedHandledAt { get; set; }
    public DateTimeOffset? ServerReopenObservedAt { get; set; }
}
