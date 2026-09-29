using System.IO;
using System.Text.Json;
using System.Windows;

namespace PriceCheck.Collector.Controls;

internal static class CollectionZoneOutline
{
    private static readonly Dictionary<string, IReadOnlyList<Point>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Point> Load(string city)
    {
        if (Cache.TryGetValue(city, out var cached)) return cached;
        if (string.IsNullOrWhiteSpace(city) || city.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || city is "." or "..") return [];
        var path = Path.Combine(AppContext.BaseDirectory, "Maps", city, "city.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!string.Equals(root.GetProperty("city").GetString(), city, StringComparison.OrdinalIgnoreCase)) return [];
            if (!root.TryGetProperty("collectionZone", out var zone)) return Cache[city] = [];
            var polygon = zone.GetProperty("polygon");
            if (polygon.GetArrayLength() is < 3 or > 128) return [];
            var points = new List<Point>();
            foreach (var pair in polygon.EnumerateArray())
            {
                if (pair.GetArrayLength() != 2) return [];
                var x = pair[0].GetDouble(); var y = pair[1].GetDouble();
                if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7) return [];
                points.Add(new(x, y));
            }
            return Cache[city] = points;
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return []; }
    }
}
