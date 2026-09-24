using PriceCheck.Collector.Services;

internal static class WorldIdentityChecks
{
    public static void Run()
    {
        var first = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var second = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
        const string seed = "0123456789ABCDEF0123456789ABCDEF";
        var original = WorldIdentityConfiguration.Resolve(first, seed);
        if (original.Length != 32 || !original.All(Uri.IsHexDigit) ||
            original != WorldIdentityConfiguration.Resolve(first, seed.ToLowerInvariant()))
            throw new Exception("Saved world identity is not stable.");
        if (original == WorldIdentityConfiguration.Resolve(second, seed))
            throw new Exception("A shared template merged two profile identities.");
        if (original == WorldIdentityConfiguration.Resolve(first, "FEDCBA9876543210FEDCBA9876543210"))
            throw new Exception("Regeneration did not rotate the world identity.");
        if (WorldIdentityConfiguration.Resolve(first, "") != first.ToString("N"))
            throw new Exception("An existing fixed template changed without regeneration.");
        try { WorldIdentityConfiguration.Resolve(first, "invalid"); }
        catch (ArgumentException) { Console.WriteLine("WORLD_IDENTITY_CHECKS_OK"); return; }
        throw new Exception("Invalid world seed was accepted.");
    }
}
