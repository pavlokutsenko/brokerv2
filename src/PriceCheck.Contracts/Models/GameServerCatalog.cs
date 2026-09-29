namespace PriceCheck.Collector.Models;

// IDs observed in the game's live server list for the validated LU4 build.
// Profiles remain open-ended: other server names can be added with a verified ID.
public static class GameServerCatalog
{
    private static readonly Dictionary<string, int> VerifiedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gamma"] = 1,
        ["Black"] = 10,
        ["White"] = 20,
        ["Carmine"] = 30
    };

    public static IReadOnlyCollection<string> VerifiedNames => VerifiedIds.Keys;

    public static bool TryGetVerifiedId(string name, out int id) =>
        VerifiedIds.TryGetValue(name.Trim(), out id);
}
