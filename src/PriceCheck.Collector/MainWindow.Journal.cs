using System.Collections.ObjectModel;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private readonly CollectorJournal _journal = new();
    public ObservableCollection<JournalEntry> JournalEntries { get; } = [];
    private void Log(string value)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(value))); return; }
        var message = CollectorJournal.Redact(value);
        var context = System.Text.RegularExpressions.Regex.Replace(value, @"^\s*(INFO|WARNING|ERROR)\s+", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var profile = Runtimes.FirstOrDefault(runtime => context.StartsWith(runtime.Profile.Name + ":", StringComparison.OrdinalIgnoreCase) ||
            context.StartsWith(runtime.Profile.Name + "/Giran:", StringComparison.OrdinalIgnoreCase));
        var explicitLevel = System.Text.RegularExpressions.Regex.Match(value, @"^\s*(INFO|WARNING|ERROR)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var level = explicitLevel.Success ? explicitLevel.Groups[1].Value.ToUpperInvariant() :
            value.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("exception", StringComparison.OrdinalIgnoreCase) ? "ERROR" :
            value.Contains("WARNING", StringComparison.OrdinalIgnoreCase) || value.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ? "WARNING" : "INFO";
        var traderMatch = System.Text.RegularExpressions.Regex.Match(message, @"(?:traderKey|trader)=([^\s,;]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var entry = new JournalEntry(DateTimeOffset.UtcNow, level, profile?.Profile.Name ?? "System",
            profile?.Profile.Name ?? "", traderMatch.Success ? traderMatch.Groups[1].Value : "", message);
        _journal.Append(entry);
        JournalEntries.Add(entry);
        while (JournalEntries.Count > 2000) JournalEntries.RemoveAt(0);
        Events.Insert(0, entry.Display);
        while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
    }
}
