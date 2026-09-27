using PriceCheck.Contracts;
using PriceCheck.Launching;
using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class GuardPhaseChecks
{
    public static async Task RunAsync()
    {
        foreach (var failure in new[] { "before", "after", "healthy" })
        {
            var fake = new Processes(failure);
            var profile = new CollectorProfile { Name = "Gamma", AutoLoginEnabled = true, LoginName = "synthetic",
                LoginPassword = "synthetic", LoginServerName = "Gamma" };
            var template = new LaunchTemplate();
            var logins = 0; var reader = 0;
            var launcher = new LaunchModule(fake, (_,_,_) => { logins++; return Task.CompletedTask; }, fake.Identity, _ => Task.CompletedTask);
            // Validate() checks that the packaged login DLL exists, even with an injected login.
            var runtime = Path.Combine(AppContext.BaseDirectory, "ClientLaunchRuntime");
            Directory.CreateDirectory(runtime);
            var source = Path.GetFullPath("native/ClientLaunch/build/x64/PriceCheck.ClientLogin.dll");
            File.Copy(source, Path.Combine(runtime, "PriceCheck.ClientLogin.dll"), true);
            try
            {
                await launcher.LaunchAsync(profile, template, _ => {}, CancellationToken.None,
                    (_,_) => { reader++; return Task.CompletedTask; });
                Require(failure == "healthy", "Failed protection launched");
                Require(logins == 1 && reader == 1, "Healthy protected client did not enter");
                Require(fake.Phases.SequenceEqual(new[] { false, true }), "Missing pre-login or post-character gate");
                await launcher.ValidateProtectionAsync(profile.Id, true, CancellationToken.None);
                launcher.Stop(profile.Id);
            }
            catch (LaunchProtectionException)
            {
                Require(failure != "healthy", "Healthy client rejected");
                Require(logins == (failure == "before" ? 0 : 1), "Login ran before protection");
                Require(reader == (failure == "before" ? 0 : 1), "Reader ran before protection");
                Require(fake.Killed == 1 && launcher.Protection(profile.Id).Failed, "Failure did not terminate and retain error");
            }
        }
        var noTemplate = new ClientProcessService();
        try { await noTemplate.LaunchAndBindAsync(new(), null, CancellationToken.None); throw new Exception("Missing template accepted"); }
        catch (LaunchProtectionException e) when (e.Message.Contains("шаблон")) { }
        Console.WriteLine("PHASE_GATES_OK before login/reader, after character, failed client terminated, no unprotected launch");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Processes(string failure) : IClientProcessService
    {
        private ClientSession? _session;
        public int Killed { get; private set; }
        public List<bool> Phases { get; } = [];
        public ClientSession? Identity(int pid) => _session;
        public bool IsAlive(int pid) => _session is not null;
        public void Release(int? pid) { }
        public void Terminate(int pid) { Killed++; _session = null; }
        public Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken token)
        { _session = new(100, DateTimeOffset.UtcNow); return Task.FromResult(100); }
        public Task WaitForGameWindowAsync(int pid, CancellationToken token) => Task.CompletedTask;
        public Task ActivateLateAgentAsync(int pid, CancellationToken token) => Task.CompletedTask;
        public ClientProtectionStatus Protection(int pid) => new(true, true, true, 2, 32, 32, "SYNTHETIC", null);
        public Task ValidateProtectionAsync(int pid, bool world, CancellationToken token)
        {
            Phases.Add(world);
            if (failure == "before" && !world || failure == "after" && world)
                throw new LaunchProtectionException("synthetic protection failure");
            return Task.CompletedTask;
        }
    }
}
