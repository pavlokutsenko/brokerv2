using System.Buffers.Binary;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Runtime.Radar;

internal sealed class LocalPlayerPositionReader
{
    private const uint CurrentTimestamp = 0x956E0D97;
    private const uint CurrentImageSize = 0x0DCEB000;
    private const ulong CurrentGWorldRva = 0x081F6A80;
    private readonly Lu4Device _device;
    private readonly int _pid;
    private readonly ulong _imageBase;
    private ulong _capsule;

    public LocalPlayerPositionReader(Lu4Device device, int pid)
    {
        _device = device;
        _pid = pid;
        _imageBase = device.GetProcessBase(pid);
        ValidateClientProfile();
    }

    public bool TryRead(out PlayerPosition position)
    {
        position = default;
        try
        {
            if (!IsPointer(_capsule) && !TryResolveCapsule()) return false;
            var coordinates = _device.Read(_pid, _capsule + 0x1F0, 24);
            var x = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates));
            var y = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(8)));
            var z = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(coordinates.AsSpan(16)));
            if (!IsCoordinate(x) || !IsCoordinate(y) || !IsCoordinate(z))
            {
                _capsule = 0;
                return false;
            }
            position = new PlayerPosition(x, y, z);
            return true;
        }
        catch
        {
            _capsule = 0;
            return false;
        }
    }

    private bool TryResolveCapsule()
    {
        var world = ReadPointer(_imageBase + CurrentGWorldRva);
        var gameInstance = ReadPointer(world + 0x1D8);
        var localPlayers = ReadPointer(gameInstance + 0x38);
        var localPlayer = ReadPointer(localPlayers);
        var controller = ReadPointer(localPlayer + 0x30);
        var playerActor = ReadPointer(controller + 0x2D0);
        var capsule = ReadPointer(playerActor + 0x1A0);
        if (!IsPointer(capsule)) return false;
        _capsule = capsule;
        return true;
    }

    private ulong ReadPointer(ulong address)
    {
        if (!IsPointer(address)) return 0;
        return BinaryPrimitives.ReadUInt64LittleEndian(_device.Read(_pid, address, 8));
    }

    private void ValidateClientProfile()
    {
        var headers = _device.Read(_pid, _imageBase, 0x1000);
        var pe = BinaryPrimitives.ReadInt32LittleEndian(headers.AsSpan(0x3C));
        if (pe < 0 || pe + 24 + 60 >= headers.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe)) != 0x4550)
            throw new InvalidDataException("Некорректный PE заголовок клиента");
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe + 8));
        var imageSize = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(pe + 24 + 56));
        if (timestamp != CurrentTimestamp || imageSize != CurrentImageSize)
            throw new InvalidOperationException(
                $"Нет профиля координат для клиента timestamp=0x{timestamp:X8}, image=0x{imageSize:X}");
    }

    private static bool IsPointer(ulong value) => value is >= 0x10000 and < 0x0000800000000000;
    private static bool IsCoordinate(double value) => double.IsFinite(value) && Math.Abs(value) < 1_000_000_000;
}

internal readonly record struct PlayerPosition(double X, double Y, double Z);
