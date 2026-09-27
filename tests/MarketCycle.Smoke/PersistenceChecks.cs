using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class PersistenceChecks
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "PriceCheck-cycle-tests", Guid.NewGuid().ToString("N"));
        var profile = new CollectorProfile { Name = "Gamma", City = "Giran" };
        var folder = Path.Combine(root, $"{profile.Id:N}-GAMMA-GIRAN");
        var path = Path.Combine(folder, "queue.json");
        var now = DateTimeOffset.UtcNow;
        var queue = new CycleQueue(profile, () => now, root);
        queue.ApplyBroker(new() { CapturedAtUtc = now, Rows = [new() {
            TraderName = "SHOP", TraderObjectId = 7, ItemId = 1, StoreType = 1, Amount = 3 }] }, true, now);
        queue.Observe(new() { Traders = [new(7, "SHOP", 1, 100, 200, 0, true, now)] });
        queue.Complete(queue.Ready(0, 0).Single(), now);
        queue.ApplyBroker(new() { CapturedAtUtc = now, Rows = [new() {
            TraderName = "SHOP", TraderObjectId = 7, ItemId = 1, StoreType = 1, Amount = 2 }] }, true, now);
        File.WriteAllBytes(path, new byte[1024]);
        queue = new(profile, () => now, root);
        Check(queue.RecoveryNotice is not null && queue.Status(new()).Checked == 1, "backup preserves completed price deadline");
        Check(Directory.GetFiles(folder, "queue.json.corrupt-*").Length == 1, "damaged bytes are preserved");
        Check(new CycleQueue(profile, () => now, root).RecoveryNotice is null, "recovered primary is valid");
        File.WriteAllBytes(path, new byte[500]);
        File.WriteAllText(path + ".bak", "{truncated");
        File.WriteAllText(path + ".tmp", "null");
        queue = new(profile, () => now, root);
        Check(queue.RecoveryNotice?.Contains("rebuilding") == true && queue.Status(new()).Active == 0,
            "unrecoverable local queue restarts without inventing records");
        Check(Directory.GetFiles(folder, "*.corrupt-*").Length == 4, "all damaged candidates retained");
        File.Move(path, path + ".tmp");
        queue = new(profile, () => now, root);
        Check(queue.RecoveryNotice is not null && File.Exists(path), "flushed pending snapshot can recover a missing primary");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { _ = new CycleQueue(profile, () => now, root); throw new Exception("lock must not be treated as corruption"); }
            catch (IOException) { }
        }
        return 7;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
