namespace PriceCheck.Collector.Services;

public static class AdapterIdentityPreview
{
    public static string Map(string seed, string adapterId)
    {
        if (seed.Length != 12 || !seed.All(Uri.IsHexDigit) || string.IsNullOrEmpty(adapterId)) return seed;
        var hash = MonitorIdentityPreview.Hash("mac|" + seed + "|" + adapterId.Trim('{', '}'));
        var bytes = Convert.FromHexString(seed);
        bytes[0] = (byte)((bytes[0] | 2) & 254);
        for (var i = 0; i < 3; i++) bytes[3 + i] = (byte)(hash >> (8 * i));
        return Convert.ToHexString(bytes);
    }
}
