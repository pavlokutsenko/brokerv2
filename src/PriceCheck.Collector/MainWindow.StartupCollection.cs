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
                var accounts=MarketAccounts(runtime);
                var active=accounts.FirstOrDefault(account=>account.Session is { } session && ClientProcessIdentity.IsCurrent(session))
                    ?? throw new InvalidOperationException("Запустите хотя бы один аккаунт этого сервера перед сбором.");
                SelectedRuntime = runtime;
                MainTabs.SelectedItem = ProfilesTab;
                ProfileTabs.SelectedIndex = 1;
                runtime.IsBusy = true;
                try
                {
                    foreach(var account in accounts)
                        account.OneTraderPerTurn=accounts.Count(r=>r.Session is { } s && ClientProcessIdentity.IsCurrent(s))>1;
                    await _launcher.ValidateProtectionAsync(active.Profile.Id, true, CancellationToken.None);
                    await EnsureReaderAttachedAsync(active);
                    await _collection.SetCollectionAsync(active, true);
                    _marketActive[runtime.Profile.Id]=active;
                    _lastBrokerOwner[runtime.Profile.Id]=accounts.ToList().IndexOf(active);
                    runtime.MarketCollectionEnabled=true;
                    BeginMarketReaderWarmups(runtime);
                    foreach(var waiting in accounts.Where(r=>r!=active))
                        _characterRotation.Schedule.Pause(waiting.Profile.Id,DateTimeOffset.UtcNow);
                    RefreshMarketDisplay(runtime);
                    await SaveProfilesAsync();
                }
                finally { runtime.IsBusy = false; }
            }
            catch (Exception e) { Log($"Requested collection {requested} did not start: {e.GetBaseException().Message}"); }
        }
    }
}
