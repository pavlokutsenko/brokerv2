namespace PriceCheck.Collector.Services;

// Keep the normalization and FNV calculation in sync with monitor_identity.cpp.
public static class MonitorIdentityPreview
{
    public static string SerialText(string uuid, string instanceId)
    {
        var serial = Hash("edid|" + uuid + "|" + instanceId);
        if (serial == 0) serial = 1;
        return serial.ToString("X8") + (serial ^ 0x9E3779B9u).ToString("X8")[..4];
    }

    public static bool IsValid(ReadOnlySpan<byte> edid)
    {
        ReadOnlySpan<byte> header = [0, 255, 255, 255, 255, 255, 255, 0];
        if (edid.Length < 128 || edid.Length % 128 != 0 || !edid[..8].SequenceEqual(header) ||
            (edid[126] + 1) * 128 > edid.Length) return false;
        for (var block = 0; block < edid.Length; block += 128)
        {
            var sum = 0;
            foreach (var value in edid.Slice(block, 128)) sum += value;
            if ((sum & 255) != 0) return false;
        }
        return true;
    }

    public static byte[] Map(string uuid, string instanceId, byte[] original)
    {
        var result = (byte[])original.Clone();
        if (!Guid.TryParse(uuid, out _) || string.IsNullOrEmpty(instanceId) || !IsValid(original)) return result;
        var serial = Hash("edid|" + uuid + "|" + instanceId);
        if (serial == 0) serial = 1;
        for (var i = 0; i < 4; i++) result[12 + i] = (byte)(serial >> (8 * i));
        var text = serial.ToString("X8") + (serial ^ 0x9E3779B9u).ToString("X8")[..4];
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (result[offset] != 0 || result[offset + 1] != 0 || result[offset + 2] != 0 ||
                result[offset + 3] != 0xFF || result[offset + 4] != 0) continue;
            for (var i = 0; i < 12; i++) result[offset + 5 + i] = (byte)text[i];
            result[offset + 17] = 10;
        }
        var sum = 0;
        for (var i = 0; i < 127; i++) sum += result[i];
        result[127] = unchecked((byte)-sum);
        return result;
    }

    internal static uint Hash(string input)
    {
        var hash = 2166136261u;
        foreach (var ch in input)
        {
            var normalized = ch is >= 'A' and <= 'Z' ? ch + 32 : ch;
            hash = unchecked((hash ^ (uint)normalized) * 16777619u);
        }
        return hash;
    }
}
