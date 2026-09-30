using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Contracts;
using PriceCheck.Launching;

internal static class MemoryBudgetTests
{
    public static async Task Run()
    {
        var legacy = JsonSerializer.Deserialize<LaunchTemplate>("{\"Name\":\"Legacy\"}")!;
        Check(!legacy.MemoryBudgetEnabled && legacy.MemoryBudgetMiB == 3072 && !legacy.CpuBudgetEnabled &&
            legacy.CpuBudgetPercent == 20, "Legacy settings must not silently enable a budget.");
        var template = new LaunchTemplate { MemoryBudgetEnabled = true, MemoryBudgetMiB = 3072,
            CpuBudgetEnabled = true, CpuBudgetPercent = 20 };
        var stored = JsonSerializer.Deserialize<LaunchTemplate>(JsonSerializer.Serialize(template))!;
        Check(stored.MemoryBudgetEnabled && stored.MemoryBudgetMiB == 3072 && stored.CpuBudgetEnabled &&
            stored.CpuBudgetPercent == 20, "Budgets must survive configuration serialization.");
        foreach (var limit in new[] { 0, 255, 65537 })
        {
            try { ClientResourceBudget.Validate(new() { MemoryBudgetEnabled = true, MemoryBudgetMiB = limit }); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid enabled budget accepted.");
        }
        foreach (var limit in new[] { 0, 101 })
        {
            try { ClientResourceBudget.Validate(new() { CpuBudgetEnabled = true, CpuBudgetPercent = limit }); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid CPU budget accepted.");
        }
        var processes = new FakeProcesses();
        var launcher = new LaunchModule(processes, identity: processes.Identity);
        var first = new CollectorProfile { Name = "First" };
        var second = new CollectorProfile { Name = "Second" };
        var a = await launcher.LaunchAsync(first, template, _ => { }, CancellationToken.None,
            (session, _) => { Check(processes.MemoryBudgets.Any(b => b.Session == session), "Budget must precede login/collection attachment."); return Task.CompletedTask; });
        var b = await launcher.LaunchAsync(second, new() { MemoryBudgetEnabled = true, MemoryBudgetMiB = 4096 }, _ => { }, CancellationToken.None);
        Check(a != b && processes.MemoryBudgets.SequenceEqual(new[] { (a, 3072), (b, 4096) }), "Budgets must target separate owned sessions.");
        await launcher.LaunchAsync(first, template, _ => { }, CancellationToken.None);
        Check(processes.MemoryBudgets.Count == 2, "Returning an existing session must not overwrite original limits.");
        await launcher.LaunchAsync(new() { Name = "Default" }, legacy, _ => { }, CancellationToken.None);
        Check(processes.MemoryBudgets.Count == 2, "Disabled budgets must not invoke the driver.");
        processes.MemoryBudgetError = new IOException("Synthetic driver rejection.");
        var rejected = new CollectorProfile { Name = "Rejected" };
        try { await launcher.LaunchAsync(rejected, template, _ => { }, CancellationToken.None); throw new Exception("Driver rejection was hidden."); }
        catch (IOException) { }
        Check(processes.Terminations == 1 && launcher.Owns(first.Id, a) && launcher.Owns(second.Id, b),
            "Failure must release only the new target, leaving other profiles owned.");
        var count = processes.MemoryBudgets.Count;
        try { await launcher.LaunchAsync(new(), new() { MemoryBudgetEnabled = true, MemoryBudgetMiB = 1 }, _ => { }, CancellationToken.None); throw new Exception("Invalid launch budget accepted."); }
        catch (ArgumentException) { }
        Check(processes.MemoryBudgets.Count == count, "Invalid configuration must be rejected before quota calls.");
        Console.WriteLine("MEMORY_BUDGET_LAUNCH_OK two independent sessions, defaults, persistence, before-login application, failure isolation");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
