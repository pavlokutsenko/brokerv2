using System.Buffers.Binary;
using System.Text;

namespace PriceCheck.Collector.Runtime.Radar;

public static class WorldPacketDecoder
{
    public static WorldPacket? Decode(ReadOnlySpan<byte> packet) => packet.Length == 0 ? null : packet[0] switch
    {
        0x31 => DecodeCharacter(packet),
        0x2F when packet.Length >= 29 => new MovePacket(
            BinaryPrimitives.ReadInt32LittleEndian(packet[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[17..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[21..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[25..])),
        0x47 when packet.Length >= 17 => new MovePacket(
            BinaryPrimitives.ReadInt32LittleEndian(packet[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[5..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[9..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[13..])),
        0x08 when packet.Length >= 5 => new DeletePacket(BinaryPrimitives.ReadInt32LittleEndian(packet[1..])),
        _ => null
    };

    private static CharacterPacket? DecodeCharacter(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 58) return null;
        var objectId = BinaryPrimitives.ReadInt32LittleEndian(packet[2..]);
        if (!TryReadUtf16(packet, 6, out var name, out var offset) ||
            !TryReadUtf16(packet, offset, out var title, out offset) || offset + 52 > packet.Length)
            return null;
        return new CharacterPacket(
            objectId, name, title, packet[offset + 31],
            BinaryPrimitives.ReadInt32LittleEndian(packet[(offset + 40)..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[(offset + 44)..]),
            BinaryPrimitives.ReadInt32LittleEndian(packet[(offset + 48)..]));
    }

    private static bool TryReadUtf16(ReadOnlySpan<byte> packet, int start, out string value, out int next)
    {
        for (var end = start; end + 1 < packet.Length; end += 2)
        {
            if (packet[end] != 0 || packet[end + 1] != 0) continue;
            value = Encoding.Unicode.GetString(packet[start..end]);
            next = end + 2;
            return true;
        }
        value = string.Empty;
        next = start;
        return false;
    }
}
