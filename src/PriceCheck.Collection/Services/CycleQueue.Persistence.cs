using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collector.Services;

public sealed partial class CycleQueue
{
    public string? RecoveryNotice { get; private set; }

    private Dictionary<string, CycleTraderState> Load()
    {
        var damaged = false;
        foreach (var candidate in new[] { _path, _path + ".bak", _path + ".tmp" })
        {
            if (!File.Exists(candidate)) continue;
            Dictionary<string, CycleTraderState> entries;
            try
            {
                using var stream = File.OpenRead(candidate);
                entries = JsonSerializer.Deserialize<Dictionary<string, CycleTraderState>>(stream, Json)
                    ?? throw new InvalidDataException("Queue has no records object.");
                foreach (var (key, e) in entries)
                    if (e is null || string.IsNullOrWhiteSpace(e.Name) || key != Key(e.Name) || e.Key != key ||
                        e.Composition is null || e.CheckedComposition is null ||
                        !double.IsFinite(e.X) || !double.IsFinite(e.Y) ||
                        !double.IsFinite(e.CheckedX) || !double.IsFinite(e.CheckedY))
                        throw new InvalidDataException("Queue contains an invalid trader record.");
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                // Keep the exact original bytes for recovery/diagnostics. Access
                // and sharing errors are not corruption and must still surface.
                File.Move(candidate, candidate + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                damaged = true;
                continue;
            }
            if (candidate != _path)
            {
                RecoveryNotice = "Saved queue restored from recovery copy.";
                WriteSnapshot(entries);
            }
            return entries;
        }
        if (damaged)
        {
            RecoveryNotice = "Saved queue was damaged; rebuilding from broker. Server history is preserved.";
            WriteSnapshot([]);
        }
        return [];
    }

    private void Save() => WriteSnapshot(_entries);

    private void WriteSnapshot(Dictionary<string, CycleTraderState> entries)
        => DurableJsonFile.Write(_path, entries, Json);
}
