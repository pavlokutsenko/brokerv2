using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class JournalPanelView : UserControl
{
    private ICollectionView? _view;
    public JournalPanelView() { InitializeComponent(); Loaded += InitializeJournal; }
    private void InitializeJournal(object sender, RoutedEventArgs e)
    {
        if (_view is not null || DataContext is not MainWindow main) return;
        ProfileFilter.ItemsSource = new[] { "Все профили", "System" }.Concat(main.MarketOptions);
        ProfileFilter.SelectedIndex = 0;
        _view = CollectionViewSource.GetDefaultView(main.JournalEntries);
        _view.Filter = Matches; Entries.ItemsSource = _view;
        main.JournalEntries.CollectionChanged += OnEntriesChanged;
    }
    private bool Matches(object item) => item is JournalEntry entry &&
        (ProfileFilter.SelectedIndex == 0 || entry.Profile == ProfileFilter.SelectedItem?.ToString()) &&
        (LevelFilter.SelectedIndex == 0 || entry.Level == (LevelFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()) &&
        (string.IsNullOrWhiteSpace(Search.Text) || entry.Display.Contains(Search.Text, StringComparison.OrdinalIgnoreCase) ||
         entry.Trader.Contains(Search.Text, StringComparison.OrdinalIgnoreCase));
    private void FilterChanged(object sender, RoutedEventArgs e) => _view?.Refresh();
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AutoScroll.IsChecked == true && e.NewItems?.Count > 0 && Matches(e.NewItems[0]!))
            Entries.ScrollIntoView(e.NewItems[0]);
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var entries = Entries.SelectedItems.Count > 0 ? Entries.SelectedItems.Cast<JournalEntry>() :
            _view?.Cast<JournalEntry>() ?? [];
        Clipboard.SetText(string.Join(Environment.NewLine, entries.Select(entry => entry.Display)));
    }
}
