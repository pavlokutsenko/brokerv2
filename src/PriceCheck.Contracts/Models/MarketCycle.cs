namespace PriceCheck.Collector.Models;

public sealed record CycleTarget(string TraderKey, string Name, long ObjectId, int KioskType,
    double X, double Y, long Revision, string Reason, DateTimeOffset DueAt)
{
    public ServerPriceJob? ServerJob { get; init; }
    public bool RebindOnRead { get; init; }
    public string? VerificationRevision { get; init; }
}

public sealed record CycleQueueRow(string Name, string State, string Reason, string PriceAge, int Attempts);

public sealed record MarketCycleStatus
{
    public string Phase { get; init; } = "Stopped";
    public string Detail { get; init; } = "Set the center and start collection.";
    public int Active { get; init; }
    public int Pending { get; init; }
    public int Deferred { get; init; }
    public int Overdue { get; init; }
    public int Checked { get; init; }
    public int CurrentPriceTraders { get; init; }
    public int IgnoredOutsideZone { get; init; }
    public int Cycles { get; init; }
    public int PassNewFound { get; init; }
    public int PassRadarPending { get; init; }
    public int PassRead { get; init; }
    public IReadOnlyList<CycleQueueRow> RadarQueue { get; init; } = [];
    public DateTimeOffset? LastBrokerAt { get; init; }
    public DateTimeOffset? NextBrokerAt { get; init; }
    public IReadOnlyList<CycleQueueRow> Queue { get; init; } = [];
}

public sealed class CycleTraderState
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public long ObjectId { get; set; }
    public int KioskType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double CheckedX { get; set; }
    public double CheckedY { get; set; }
    public bool Active { get; set; } = true;
    public bool HasPosition { get; set; }
    public bool Dirty { get; set; } = true;
    public string Reason { get; set; } = "New shop";
    public long Revision { get; set; } = 1;
    public DateTimeOffset ChangedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public int Attempts { get; set; }
    public Dictionary<string, int> Composition { get; set; } = [];
    public Dictionary<string, int> CheckedComposition { get; set; } = [];
}
