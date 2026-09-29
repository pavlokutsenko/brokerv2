namespace PriceCheck.Collector.Services;

internal static class DeviceIdentityPreview
{
    public static string Map(string seed, string original)
    {
        if (string.IsNullOrEmpty(seed) ||
            !(original.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
              original.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))) return original;
        var separator = original.IndexOf('\\', 4);
        if (separator < 0) return original;
        ulong hash = 14695981039346656037UL;
        foreach (var ch in seed + "|" + original)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash = unchecked(hash * 1099511628211UL);
        }
        var mapped = original.ToCharArray();
        const string hex = "0123456789ABCDEF";
        for (var index = separator + 1; index < mapped.Length; index++)
        {
            var ch = mapped[index];
            if (!(ch is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z')) continue;
            hash ^= hash >> 12;
            hash ^= hash << 25;
            hash ^= hash >> 27;
            mapped[index] = hex[(int)(unchecked(hash * 2685821657736338717UL) >> 60)];
        }
        return new string(mapped);
    }
}
