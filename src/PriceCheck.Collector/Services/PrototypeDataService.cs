using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;

namespace PriceCheck.Collector.Services;

public sealed class PrototypeDataService : ICollectorDataSource
{
    private readonly string _diagnostics;

    public PrototypeDataService()
    {
        _diagnostics = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ChatGPT",
            "driver",
            "diagnostics");
    }

    public async Task<RadarSnapshot?> ReadRadarAsync(int? expectedPid)
    {
        var path = Path.Combine(_diagnostics, "latest_actor_snapshot.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        var pid = root.GetProperty("pid").GetInt32();
        if (expectedPid is int value && value != pid) return null;
        var player = root.GetProperty("player");
        var traders = new List<RadarPoint>();
        foreach (var item in root.GetProperty("traders").EnumerateArray())
        {
            traders.Add(new RadarPoint(
                item.GetProperty("object_id").GetInt32(),
                item.GetProperty("name").GetString() ?? "",
                item.GetProperty("kiosk_type").GetInt32(),
                item.GetProperty("x").GetDouble(),
                item.GetProperty("y").GetDouble(),
                item.GetProperty("distance").GetDouble()));
        }
        return new RadarSnapshot
        {
            ProcessId = pid,
            PlayerX = player.GetProperty("x").GetDouble(),
            PlayerY = player.GetProperty("y").GetDouble(),
            PositionedActors = root.GetProperty("positioned_actor_count").GetInt32(),
            Traders = traders,
            CapturedAtUtc = File.GetLastWriteTimeUtc(path)
        };
    }

    public async Task<BrokerSnapshot?> ReadLatestBrokerAsync()
    {
        if (!Directory.Exists(_diagnostics)) return null;
        var file = new DirectoryInfo(_diagnostics)
            .GetFiles("full_broker_inventory*.json")
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file is null) return null;
        await using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        var summary = root.GetProperty("summary");
        var byType = root.GetProperty("by_store_type");
        int TypeCount(string type) => byType.TryGetProperty(type, out var value)
            ? value.GetProperty("unique_traders").GetInt32()
            : 0;
        return new BrokerSnapshot
        {
            UniqueTraders = summary.GetProperty("unique_traders").GetInt32(),
            ListingRows = summary.GetProperty("listing_rows").GetInt32(),
            SellTraders = TypeCount("1"),
            BuyTraders = TypeCount("3"),
            PackageTraders = TypeCount("8"),
            ElapsedSeconds = root.GetProperty("elapsed_seconds").GetDouble(),
            CapturedAtUtc = file.LastWriteTimeUtc
        };
    }
}
