using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Runtime.Driver;

if (args.Length is < 3 or > 4 || !int.TryParse(args[0], out var pid) || !long.TryParse(args[1], out var birthTicks))
    throw new ArgumentException("PID, UTC birth ticks, report path and optional expected server required");
var expectedName = args.Length == 4 ? args[3] : "";
using var target = Process.GetProcessById(pid);
if (target.StartTime.ToUniversalTime().Ticks != birthTicks) throw new InvalidOperationException("PID birth mismatch");
using var device = new Lu4Device();
var baseAddress = device.GetProcessBase(pid);
var report = Path.GetFullPath(args[2]);
ulong U64(ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(device.Read(pid, address, 8));
uint U32(ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(device.Read(pid, address, 4));
const ulong gobjectsRva = 0x080768E0;
var chunkTable = U64(baseAddress + gobjectsRva);
ulong ObjectAt(int index) => U64(U64(chunkTable + (ulong)(index / 65536) * 8) + (ulong)(index % 65536) * 0x18);
var itemClass = ObjectAt(52742);
if (U32(itemClass + 0x0C) != 52742) throw new InvalidOperationException("Server widget class index changed");
var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
while (DateTimeOffset.UtcNow < deadline && !target.HasExited)
{
    var items = new List<object>();
    var sawExpected = expectedName.Length == 0;
    for (var chunkIndex = 0; chunkIndex < 8; chunkIndex++)
    {
        var chunk = U64(chunkTable + (ulong)chunkIndex * 8);
        if (chunk == 0) break;
        var first = chunkIndex < 3 ? 65536 : chunkIndex == 3 ? 45000 : 0;
        for (var index = first; index < 65536; index++)
        {
            ulong obj;
            try { obj = U64(chunk + (ulong)index * 0x18); }
            catch { break; }
            if (obj < 0x1000000000 || obj > 0x00007FFFFFFFFFFF) continue;
            try
            {
                if (U64(obj + 0x10) != itemClass) continue;
                var serverId = U32(obj + 0x318);
                var widgetId = U32(obj + 0x368);
                var namePointer = U64(obj + 0x318 + 0x28);
                var nameLength = U32(obj + 0x318 + 0x30);
                var name = namePointer != 0 && nameLength is > 0 and < 128
                    ? Encoding.Unicode.GetString(device.Read(pid, namePointer, checked((int)nameLength * 2))).TrimEnd('\0')
                    : "";
                if (name.Length > 0)
                {
                    items.Add(new { name, serverId, widgetId, objectIndex = chunkIndex * 65536 + index });
                    if (name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) sawExpected = true;
                }
            }
            catch { }
        }
    }
    if (items.Count > 1 && sawExpected)
    {
        File.WriteAllText(report, JsonSerializer.Serialize(new { pid, birthTicks, capturedAt = DateTimeOffset.UtcNow, items }));
        return;
    }
    await Task.Delay(1000);
}
File.WriteAllText(report, JsonSerializer.Serialize(new { pid, birthTicks, capturedAt = DateTimeOffset.UtcNow, status = "no-live-list" }));
