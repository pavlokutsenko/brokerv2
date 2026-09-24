using System.Security.Cryptography;

namespace PriceCheck.Collector.Services;

internal static class WorldIdentityConfiguration
{
    public static string Resolve(Guid profileId, string? seed)
    {
        // Existing fixed templates keep their tested identity until regenerated.
        if (string.IsNullOrEmpty(seed)) return profileId.ToString("N");
        if (seed.Length != 32 || !seed.All(Uri.IsHexDigit))
            throw new ArgumentException("The template contains an invalid world identity. Click Regenerate.");
        Span<byte> input = stackalloc byte[32];
        profileId.TryWriteBytes(input[..16]);
        Convert.FromHexString(seed).CopyTo(input[16..]);
        return Convert.ToHexString(SHA256.HashData(input).AsSpan(0, 16));
    }
}
