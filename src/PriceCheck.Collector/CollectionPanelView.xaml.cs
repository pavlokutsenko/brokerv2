using System.Windows;
using System.Windows.Controls;

namespace PriceCheck.Collector;

public partial class CollectionPanelView : UserControl
{
    public CollectionPanelView() => InitializeComponent();
    public event RoutedEventHandler? ClearCenterZoneClickRequested;
    private void ClearCenterZone_Click(object sender, RoutedEventArgs e) => ClearCenterZoneClickRequested?.Invoke(sender, e);
    public event RoutedEventHandler? MarkCenterZoneClickRequested;
    private void MarkCenterZone_Click(object sender, RoutedEventArgs e) => MarkCenterZoneClickRequested?.Invoke(sender, e);
    public event TextChangedEventHandler? ProfileFieldChangedRequested;
    private void ProfileField_Changed(object sender, TextChangedEventArgs e) => ProfileFieldChangedRequested?.Invoke(sender, e);
    public event SelectionChangedEventHandler? ProfileSelectionChangedRequested;
    private void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e) => ProfileSelectionChangedRequested?.Invoke(sender, e);
    public event EventHandler? RoleDropDownClosedRequested;
    private void Role_DropDownClosed(object sender, EventArgs e) => RoleDropDownClosedRequested?.Invoke(sender, e);
    public event RoutedEventHandler? ToggleCollectionClickRequested;
    private void ToggleCollection_Click(object sender, RoutedEventArgs e) => ToggleCollectionClickRequested?.Invoke(sender, e);
}
