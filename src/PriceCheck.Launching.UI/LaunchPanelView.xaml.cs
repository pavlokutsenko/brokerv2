using System.Windows;
using System.Windows.Controls;

namespace PriceCheck.Collector;

public partial class LaunchPanelView : UserControl
{
    public LaunchPanelView() => InitializeComponent();
    public event RoutedEventHandler? AutoLoginChangedRequested;
    private void AutoLogin_Changed(object sender, RoutedEventArgs e) => AutoLoginChangedRequested?.Invoke(sender, e);
    public event RoutedEventHandler? BrowseFolderClickRequested;
    private void BrowseFolder_Click(object sender, RoutedEventArgs e) => BrowseFolderClickRequested?.Invoke(sender, e);
    public event RoutedEventHandler? LaunchClickRequested;
    private void Launch_Click(object sender, RoutedEventArgs e) => LaunchClickRequested?.Invoke(sender, e);
    public event SelectionChangedEventHandler? LaunchTemplateSelectionChangedRequested;
    private void LaunchTemplateSelection_Changed(object sender, SelectionChangedEventArgs e) => LaunchTemplateSelectionChangedRequested?.Invoke(sender, e);
    public event RoutedEventHandler? LoginPasswordChangedRequested;
    private void LoginPassword_Changed(object sender, RoutedEventArgs e) => LoginPasswordChangedRequested?.Invoke(sender, e);
    public event RoutedEventHandler? ManageTemplatesClickRequested;
    private void ManageTemplates_Click(object sender, RoutedEventArgs e) => ManageTemplatesClickRequested?.Invoke(sender, e);
    public event TextChangedEventHandler? ProfileFieldChangedRequested;
    private void ProfileField_Changed(object sender, TextChangedEventArgs e) => ProfileFieldChangedRequested?.Invoke(sender, e);
    public event SelectionChangedEventHandler? ProfileSelectionChangedRequested;
    private void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e) => ProfileSelectionChangedRequested?.Invoke(sender, e);
    public event RoutedEventHandler? StopClickRequested;
    private void Stop_Click(object sender, RoutedEventArgs e) => StopClickRequested?.Invoke(sender, e);
}
