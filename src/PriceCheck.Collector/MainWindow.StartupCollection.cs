using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    // Explicit invocation only. Normal application startup always stays stopped.
    private async Task StartRequestedCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(StartupCollectProfileId)) return;
        foreach (var requested in StartupCollectProfileId.Split(',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!Guid.TryParse(requested, out var id))
                    throw new InvalidOperationException("--collect-profile requires exact profile GUIDs.");
                var runtime = Runtimes.SingleOrDefault(r => r.Profile.Id == id)
                    ?? throw new InvalidOperationException("Requested collection profile was not found.");
                if (runtime.Session is not { } session || !ClientProcessIdentity.IsCurrent(session))
                    throw new InvalidOperationException("Enter the game with this profile before starting collection.");
                SelectedRuntime = runtime;
                MainTabs.SelectedIndex = 1;
                runtime.IsBusy = true;
                try
                {
                    await _launcher.ValidateProtectionAsync(runtime.Profile.Id, true, CancellationToken.None);
                    await _collection.AttachAsync(runtime, CancellationToken.None);
                    await _collection.SetCollectionAsync(runtime, true);
                    await SaveProfilesAsync();
                }
                finally { runtime.IsBusy = false; }
            }
            catch (Exception e) { Log($"Requested collection {requested} did not start: {e.GetBaseException().Message}"); }
        }
    }
}
