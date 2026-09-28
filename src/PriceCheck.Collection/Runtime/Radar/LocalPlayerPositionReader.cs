using System.Buffers.Binary;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Runtime.Radar;

internal sealed class LocalPlayerPositionReader
{
    private readonly Lu4Device _device;
    private readonly int _pid;
    private readonly ulong _imageBase;
    private readonly uint _imageSize;
    private readonly (uint Rva, uint Size)[] _writableSections;
    private ulong _worldSlot;
    private long _nextWorldScan;
    private ulong _controller;
    private ulong _capsule;

    public LocalPlayerPositionReader(Lu4Device device, int pid)
    {
        _device = device;
        _pid = pid;
        _imageBase = device.GetProcessBase(pid);
        (_imageSize, _writableSections) = ReadImageLayout();
    }

    public bool TryRead(out PlayerPosition position)
    {
        position = default;
        try
        {
            if (!TryRefreshCapsule() && !TryResolveCapsule()) return false;
            var coordinates = _device.Read(_pid, _capsule + 0x1F0, 24);
            var x = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates));
            var y = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(8)));
            var z = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(16)));
            if (!IsCoordinate(x) || !IsCoordinate(y) || !IsCoordinate(z))
            {
                _controller = 0;
                _capsule = 0;
                _worldSlot = 0;
                return false;
            }
            position = new PlayerPosition(x, y, z);
            return true;
        }
        catch
        {
            _controller = 0;
            _capsule = 0;
            _worldSlot = 0;
            return false;
        }
    }

    private bool TryResolveCapsule()
    {
        if (_worldSlot == 0 && !FindWorldSlot()) return false;
        var world = ReadPointer(_worldSlot);
        var gameInstance = ReadPointer(world + 0x1D8);
        var localPlayers = ReadPointer(gameInstance + 0x38);
        var localPlayer = ReadPointer(localPlayers);
        var controller = ReadPointer(localPlayer + 0x30);
        var playerActor = ReadPointer(controller + 0x2D0);
        var capsule = ReadPointer(playerActor + 0x1A0);
        if (!IsPointer(controller) || !IsPointer(capsule)) { _worldSlot = 0; return false; }
        _controller = controller;
        _capsule = capsule;
        return true;
    }

    private bool TryRefreshCapsule()
    {
        if (!IsPointer(_controller)) return false;
        var playerActor = ReadPointer(_controller + 0x2D0);
        var currentCapsule = ReadPointer(playerActor + 0x1A0);
        if (!IsPointer(currentCapsule)) return false;
        _capsule = currentCapsule;
        return true;
    }

    private ulong ReadPointer(ulong address)
    {
        if (!IsPointer(address)) return 0;
        return BinaryPrimitives.ReadUInt64LittleEndian(_device.Read(_pid, address, 8));
    }

    private bool FindWorldSlot()
    {
        if (Environment.TickCount64 < _nextWorldScan) return false;
        _nextWorldScan = Environment.TickCount64 + 5000;
        foreach (var (rva, size) in _writableSections)
        {
            for (uint offset = 0; offset < size; offset += 0x80000)
            {
                var count = (int)Math.Min(0x80000u, size - offset);
                count -= count % 8;
                if (count < 8) break;
                byte[] page;
                try { page = _device.Read(_pid, _imageBase + rva + offset, count); }
                catch { continue; }
                for (var at = 0; at + 8 <= page.Length; at += 8)
                {
                    var world = BinaryPrimitives.ReadUInt64LittleEndian(page.AsSpan(at));
                    if (!IsPointer(world)) continue;
                    try
                    {
                        var level = ReadPointer(world + 0x30);
                        var game = ReadPointer(world + 0x1D8);
                        if (!IsPointer(level) || !IsPointer(game)) continue;
                        var actors = ReadPointer(level + 0xA0);
                        var actorCount = BinaryPrimitives.ReadInt32LittleEndian(_device.Read(_pid, level + 0xA8, 4));
                        if (!IsPointer(actors) || actorCount is < 1 or > 200000) continue;
                        var players = ReadPointer(game + 0x38);
                        var local = ReadPointer(players);
                        var controller = ReadPointer(local + 0x30);
                        var actor = ReadPointer(controller + 0x2D0);
                        var capsule = ReadPointer(actor + 0x1A0);
                        if (!IsPointer(controller) || !IsPointer(capsule)) continue;
                        var vtable = ReadPointer(controller);
                        var processEvent = ReadPointer(vtable + 77 * 8);
                        if (processEvent < _imageBase || processEvent >= _imageBase + _imageSize) continue;
                        var coordinates = _device.Read(_pid, capsule + 0x1F0, 24);
                        if (!IsCoordinate(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates))) ||
                            !IsCoordinate(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(8)))) ||
                            !IsCoordinate(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(16))))) continue;
                        _worldSlot = _imageBase + rva + offset + (uint)at;
                        _controller = controller;
                        _capsule = capsule;
                        return true;
                    }
                    catch { }
                }
            }
        }
        return false;
    }

    private (uint ImageSize, (uint Rva, uint Size)[] Sections) ReadImageLayout()
    {
        var headers = _device.Read(_pid, _imageBase, 0x1000);
        var pe = BinaryPrimitives.ReadInt32LittleEndian(headers.AsSpan(0x3C));
        if (pe < 64 || pe + 24 + 60 >= headers.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe)) != 0x4550)
            throw new InvalidDataException("Invalid client PE header.");
        var imageSize = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe + 24 + 56));
        var number = BinaryPrimitives.ReadUInt16LittleEndian(headers.AsSpan(pe + 6));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(headers.AsSpan(pe + 20));
        var first = pe + 24 + optionalSize;
        if (imageSize is < 0x10000 or > 0x40000000 || number is < 1 or > 64 ||
            first + number * 40 > headers.Length)
            throw new InvalidDataException("Invalid client PE section layout.");
        var sections = new List<(uint Rva, uint Size)>();
        for (var index = 0; index < number; index++)
        {
            var at = first + index * 40;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(at + 8));
            var rva = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(at + 12));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(at + 36));
            if ((flags & 0xC0000000u) == 0xC0000000u && size > 0 &&
                rva < imageSize && size <= imageSize - rva) sections.Add((rva, size));
        }
        if (sections.Count == 0) throw new InvalidDataException("No writable client section for world discovery.");
        return (imageSize, sections.ToArray());
    }

    private static bool IsPointer(ulong value) => value is >= 0x10000 and < 0x0000800000000000;
    private static bool IsCoordinate(double value) => double.IsFinite(value) && Math.Abs(value) < 1_000_000_000;
}

internal readonly record struct PlayerPosition(double X, double Y, double Z);
