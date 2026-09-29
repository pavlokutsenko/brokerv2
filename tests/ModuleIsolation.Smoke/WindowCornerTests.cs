using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Launching;

internal static class WindowCornerTests
{
    public static async Task Run()
    {
        var processes = new FakeProcesses();
        var launcher = new LaunchModule(processes, identity: processes.Identity,
            restartPause: _ => Task.CompletedTask);
        var profiles = Enumerable.Range(0, 3).Select(index => new CollectorProfile
        {
            Name = $"Corner {index}"
        }).ToArray();
        var sessions = new PriceCheck.Contracts.ClientSession[profiles.Length];
        for (var index = 0; index < profiles.Length; index++)
            sessions[index] = await launcher.LaunchAsync(profiles[index], null, _ => { }, CancellationToken.None);

        Require(processes.WindowPlacements.Select(value => value.Corner).SequenceEqual(
            [GameWindowCorner.TopLeft, GameWindowCorner.TopRight, GameWindowCorner.BottomLeft]),
            "Three profiles must occupy distinct corners.");

        await launcher.StopAndWaitAsync(profiles[0].Id, sessions[0], CancellationToken.None);
        var replacement = await launcher.LaunchAsync(profiles[0], null, _ => { }, CancellationToken.None);
        Require(processes.WindowPlacements[^1] == (replacement.ProcessId, GameWindowCorner.TopLeft),
            "Character rotation must preserve the profile corner.");

        var fourthProfile = new CollectorProfile { Name = "Fourth" };
        var fourth = await launcher.LaunchAsync(fourthProfile, null,
            _ => { }, CancellationToken.None);
        Require(processes.WindowPlacements[^1] == (fourth.ProcessId, GameWindowCorner.BottomRight),
            "Four active profiles must occupy all four corners.");

        launcher.Stop(profiles[1].Id);
        var fifthProfile = new CollectorProfile { Name = "Replacement" };
        var fifth = await launcher.LaunchAsync(fifthProfile, null, _ => { }, CancellationToken.None);
        Require(processes.WindowPlacements[^1] == (fifth.ProcessId, GameWindowCorner.TopRight),
            "A new profile must reuse a free corner without moving the other clients.");
        foreach (var profile in profiles) launcher.Stop(profile.Id);
        launcher.Stop(fourthProfile.Id);
        launcher.Stop(fifthProfile.Id);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
