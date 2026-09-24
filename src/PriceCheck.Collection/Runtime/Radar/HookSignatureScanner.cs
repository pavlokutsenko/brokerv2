using System.Buffers.Binary;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Runtime.Radar;

public static class HookSignatureScanner
{
    private static readonly byte?[] Pattern =
    [
        0x44, 0x0F, 0xB6, 0xC2, 0x41, 0x88, 0x4A, 0xFF,
        0x44, 0x3B, 0xCE, 0x7C, null, 0x48, 0x01, 0x75, 0x50
    ];

    public static ulong FindUnique(Lu4Device device, int pid, ulong imageBase)
    {
        var headers = device.Read(pid, imageBase, 0x2000);
        var pe = BinaryPrimitives.ReadInt32LittleEndian(headers.AsSpan(0x3C));
        if (pe < 0 || pe + 24 >= headers.Length || BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe)) != 0x4550)
            throw new InvalidDataException("Invalid client PE header.");
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(headers.AsSpan(pe + 6));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(headers.AsSpan(pe + 20));
        var sectionTable = pe + 24 + optionalSize;
        var matches = new List<ulong>();

        for (var section = 0; section < sectionCount; section++)
        {
            var entry = sectionTable + section * 40;
            var virtualSize = BinaryPrimitives.ReadInt32LittleEndian(headers.AsSpan(entry + 8));
            var rva = BinaryPrimitives.ReadInt32LittleEndian(headers.AsSpan(entry + 12));
            var characteristics = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(entry + 36));
            if (virtualSize <= 0 || (characteristics & 0x20000000) == 0) continue;
            ScanSection(device, pid, imageBase + (uint)rva, virtualSize, matches);
        }

        var unique = matches.Distinct().ToArray();
        if (unique.Length != 1)
            throw new InvalidOperationException($"Expected one receive hook signature; found {unique.Length}.");
        return unique[0];
    }

    private static void ScanSection(Lu4Device device, int pid, ulong start, int size, List<ulong> matches)
    {
        byte[] overlap = [];
        for (var offset = 0; offset < size; offset += 1024 * 1024)
        {
            var length = Math.Min(1024 * 1024, size - offset);
            var chunk = device.Read(pid, start + (uint)offset, length);
            var data = new byte[overlap.Length + chunk.Length];
            overlap.CopyTo(data, 0);
            chunk.CopyTo(data, overlap.Length);
            var origin = start + (uint)offset - (uint)overlap.Length;
            for (var index = 0; index <= data.Length - Pattern.Length; index++)
            {
                var match = true;
                for (var p = 0; p < Pattern.Length; p++)
                    if (Pattern[p] is byte expected && data[index + p] != expected) { match = false; break; }
                if (match) matches.Add(origin + (uint)index);
            }
            overlap = data[^Math.Min(32, data.Length)..];
        }
    }
}
