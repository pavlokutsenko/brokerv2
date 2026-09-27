namespace PriceCheck.Collector.Models;

public sealed record GiranCenterAssignment(int DistinctSavedCenters, int MissingProfiles, int ReusedProfiles);

public static class SavedGiranCenter
{
    public static CenterZoneSettings? Stored(CollectorProfile profile) =>
        profile.CenterZonesByCity is not null &&
        profile.CenterZonesByCity.TryGetValue("Giran", out var center) && Valid(center) ? center : null;

    public static CenterZoneSettings? Resolve(CollectorProfile profile) =>
        Stored(profile) ?? (Valid(profile.SharedGiranCenter) ? profile.SharedGiranCenter : null);

    public static GiranCenterAssignment AssignSharedFallback(IEnumerable<CollectorProfile> profiles)
    {
        var all = profiles.ToArray();
        var saved = all.Select(Stored).Where(center => center is not null)
            .Select(center => (center!.X, center.Y)).Distinct().ToArray();
        var missing = 0;
        foreach (var profile in all)
        {
            profile.SharedGiranCenter = null;
            if (Stored(profile) is not null) continue;
            missing++;
            if (saved.Length == 1)
                profile.SharedGiranCenter = new() { X = saved[0].X, Y = saved[0].Y };
        }
        return new(saved.Length, missing, saved.Length == 1 ? missing : 0);
    }

    private static bool Valid(CenterZoneSettings? center) =>
        center is not null && double.IsFinite(center.X) && double.IsFinite(center.Y);
}
