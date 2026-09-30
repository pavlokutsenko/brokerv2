using System.Windows;
using System.Windows.Controls;

namespace PriceCheck.Collector;

public sealed record AccountLaunchItem(object Runtime,string TemplateName);

public partial class AccountLaunchPanelView : UserControl
{
    public AccountLaunchPanelView() => InitializeComponent();
    public event RoutedEventHandler? AddAccountRequested,EditAccountRequested,DeleteAccountRequested;
    public event RoutedEventHandler? LaunchAccountRequested,StopAccountRequested;
    public event RoutedEventHandler? BrowseFolderRequested,ManageTemplatesRequested;
    public event TextChangedEventHandler? ProfileFieldChangedRequested;

    private void AddAccount_Click(object sender,RoutedEventArgs e)=>AddAccountRequested?.Invoke(sender,e);
    private void EditAccount_Click(object sender,RoutedEventArgs e)=>EditAccountRequested?.Invoke(sender,e);
    private void DeleteAccount_Click(object sender,RoutedEventArgs e)=>DeleteAccountRequested?.Invoke(sender,e);
    private void LaunchAccount_Click(object sender,RoutedEventArgs e)=>LaunchAccountRequested?.Invoke(sender,e);
    private void StopAccount_Click(object sender,RoutedEventArgs e)=>StopAccountRequested?.Invoke(sender,e);
    private void BrowseFolder_Click(object sender,RoutedEventArgs e)=>BrowseFolderRequested?.Invoke(sender,e);
    private void ManageTemplates_Click(object sender,RoutedEventArgs e)=>ManageTemplatesRequested?.Invoke(sender,e);
    private void ProfileField_Changed(object sender,TextChangedEventArgs e)=>ProfileFieldChangedRequested?.Invoke(sender,e);
}
