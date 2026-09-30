using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PriceCheck.Collector;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class ProfileSelectionChecks
{
    public static void Verify(Application app, string directory)
    {
        foreach (var (name, id) in new[] { ("Gamma", 1), ("Black", 10), ("White", 20), ("Carmine", 30) })
            if (!GameServerCatalog.TryGetVerifiedId(name, out var actual) || actual != id)
                throw new Exception($"Verified game server ID changed for {name}.");
        VerifyConfiguration(app, directory, ["Gamma", "Black", "White", "Carmine"]);
        VerifyConfiguration(app, directory, ["Gamma", "Black", "White", "Carmine", "Fifth", "Sixth"]);
        // Existing settings can contain two profiles on one market; switching
        // their editors must not run the user's duplicate-market validation.
        VerifyConfiguration(app, directory, ["Gamma", "Black", "Gamma", "White"]);
    }

    private static void VerifyConfiguration(Application app, string directory, string[] names)
    {
        var settings = Path.Combine(directory, "profile-selection-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(false, settings) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        var fixtures = names.Select((name, index) =>
            new ProfileRuntime { Profile = new CollectorProfile
            {
                Name = name, LaunchFile = $@"C:\Fixture{index}\LU4.exe", LoginName = "account-" + index,
                LoginPassword = "password-" + index, AutoLoginEnabled = index % 2 == 0,
                RotationIntervalMinutes = 60 + index, RotationJitterMinutes = 10 + index,
                RecheckHours = 24 + index, TraderPauseSeconds = 30 + index,
                ServerUrl = $"https://fixture{index}.invalid/api",
                RotationCharacterSlots = [0, 1, 2], CharacterSlot = index % 3
            } }).ToArray();
        foreach (var runtime in fixtures) window.Runtimes.Add(runtime);
        window.SelectedRuntime = fixtures[0];
        window.Show(); Pump(app);
        typeof(MainWindow).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        var tabs = (TabControl)window.FindName("MainTabs");
        var profileTabs = (TabControl)window.FindName("ProfileTabs");
        if (tabs.SelectedIndex != 0 || tabs.SelectedContent is null || profileTabs.SelectedIndex != 0)
            throw new Exception("Collector startup did not display the profile launch panel.");
        // Materialize both profile-bound editors before switching the left profile list.
        tabs.SelectedIndex = 0; profileTabs.SelectedIndex = 1; Pump(app);
        var collection = (CollectionPanelView)window.FindName("CollectionPanel");
        var settingsTab = Find<TabControl>(collection).Single();
        settingsTab.SelectedIndex = 3; Pump(app);
        profileTabs.SelectedIndex = 2; Pump(app);
        var journal = (JournalPanelView)window.FindName("JournalPanel");
        var entries = (DataGrid)journal.FindName("Entries");
        window.JournalEntries.Add(new(DateTimeOffset.UtcNow, "INFO", fixtures[0].Profile.Name,
            fixtures[0].Profile.Name, "", "first profile entry"));
        window.JournalEntries.Add(new(DateTimeOffset.UtcNow, "INFO", fixtures[1].Profile.Name,
            fixtures[1].Profile.Name, "", "second profile entry"));
        Pump(app);
        if (entries.Items.Cast<JournalEntry>().Select(value => value.Message).Single() != "first profile entry")
            throw new Exception("Journal displayed another profile's entry.");
        window.SelectedRuntime = fixtures[1]; Pump(app);
        if (entries.Items.Cast<JournalEntry>().Select(value => value.Message).Single() != "second profile entry")
            throw new Exception("Journal did not follow the selected profile.");
        window.SelectedRuntime = fixtures[0]; profileTabs.SelectedIndex = 1; Pump(app);
        var list = Find<ListBox>(window).Single(box => ReferenceEquals(box.ItemsSource, window.Runtimes));
        for (var pass = 0; pass < 4; pass++)
            foreach (var runtime in fixtures.Reverse())
            {
                list.SelectedItem = runtime; Pump(app);
                if (!ReferenceEquals(window.SelectedRuntime, runtime))
                    throw new Exception("Left profile selection did not update collection settings.");
                profileTabs.SelectedIndex = 0; Pump(app);
                profileTabs.SelectedIndex = 1; Pump(app);
            }
        for (var index = 0; index < fixtures.Length; index++)
        {
            var profile = fixtures[index].Profile;
            if (profile.Name != names[index] ||
                profile.LoginName != "account-" + index || profile.LoginPassword != "password-" + index ||
                profile.LaunchFile != $@"C:\Fixture{index}\LU4.exe" || profile.RecheckHours != 24 + index ||
                profile.TraderPauseSeconds != 30 + index ||
                profile.CharacterSlot != index % 3 || profile.RotationIntervalMinutes != 60 + index)
                throw new Exception("Switching profiles changed stored fields.");
        }
        // A real field edit must still save to the selected profile.
        var recheck = Find<TextBox>(collection).Single(box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Profile.RecheckHours");
        recheck.Text = "36"; recheck.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Pump(app);
        var stored = new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult();
        if (stored.Single(value => value.Id == window.SelectedRuntime!.Profile.Id).RecheckHours != 36)
            throw new Exception("Profile editing stopped persisting settings.");
        var pause = Find<TextBox>(collection).Single(box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Profile.TraderPauseSeconds");
        pause.Text = "45"; pause.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Pump(app);
        stored = new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult();
        if (stored.Single(value => value.Id == window.SelectedRuntime!.Profile.Id).TraderPauseSeconds != 45)
            throw new Exception("Trader pause stopped persisting settings.");
        tabs.SelectedIndex=0; profileTabs.SelectedIndex=0; Pump(app);
        list.SelectedItem=fixtures[1]; Pump(app);
        if(window.SelectedRuntime!=fixtures[1] || window.SelectedRuntime.Profile.Name!=names[1])
            throw new Exception("Selecting a server did not switch its owned profile and market.");
        Console.WriteLine("PROFILE_SELECTION_OK repeated_switches=16 fields_preserved edit_saved isolated_settings");
        VerifyDeletion(app, window, settings, list, fixtures);
        window.Close(); Pump(app);
    }

    private static void VerifyDeletion(Application app, MainWindow window, string settings, ListBox list, ProfileRuntime[] fixtures)
    {
        var sidebar = (ProfileSidebarView)window.FindName("SidebarView");
        var button = (Button)sidebar.FindName("DeleteProfileButton");
        list.SelectedItem = fixtures[^1]; Pump(app);
        if (!button.IsEnabled) throw new Exception("Sidebar deletion is unavailable for an idle profile.");
        var remove = typeof(MainWindow).GetMethod("RemoveProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Remove(ProfileRuntime runtime)
        {
            var task = (Task<bool>)remove.Invoke(window, [runtime])!;
            while (!task.IsCompleted) Pump(app);
            return task.GetAwaiter().GetResult();
        }
        fixtures[^1].IsBusy = true; Pump(app);
        if (button.IsEnabled || Remove(fixtures[^1])) throw new Exception("A busy profile can be deleted.");
        fixtures[^1].IsBusy = false; Pump(app);
        if (!Remove(fixtures[^1]) || window.SelectedRuntime != fixtures[^2])
            throw new Exception("Deletion did not select the adjacent profile.");
        var store = new ProfileStore(settings);
        if (store.LoadAsync().GetAwaiter().GetResult().Any(value => value.Id == fixtures[^1].Profile.Id))
            throw new Exception("Deleted profile remained in persisted configuration.");
        for(var index=fixtures.Length-2;index>0;index--)
            if(!Remove(fixtures[index])) throw new Exception("Idle profile deletion failed.");
        Pump(app);
        if (button.IsEnabled || Remove(fixtures[0]) || window.Runtimes.Count != 1)
            throw new Exception("The last profile can be deleted.");
        if (store.LoadAsync().GetAwaiter().GetResult().Single().Id != fixtures[0].Profile.Id)
            throw new Exception("Deletion changed the remaining profile's identity.");
        Console.WriteLine("PROFILE_DELETION_OK sidebar_button busy_guard saved_removal adjacent_selection last_profile_guard");
    }

    private static void Pump(Application app) => app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
}
