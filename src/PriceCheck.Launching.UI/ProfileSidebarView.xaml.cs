using System.Windows;
using System.Windows.Controls;

namespace PriceCheck.Collector;

public partial class ProfileSidebarView : UserControl
{
    public static readonly DependencyProperty ProductLabelProperty = DependencyProperty.Register(
        nameof(ProductLabel), typeof(string), typeof(ProfileSidebarView),
        new PropertyMetadata("СБОРЩИК"));

    public static readonly DependencyProperty IsTemplatesSelectedProperty = DependencyProperty.Register(
        nameof(IsTemplatesSelected), typeof(bool), typeof(ProfileSidebarView), new PropertyMetadata(false));

    public bool IsTemplatesSelected
    {
        get => (bool)GetValue(IsTemplatesSelectedProperty);
        set => SetValue(IsTemplatesSelectedProperty, value);
    }

    public string ProductLabel
    {
        get => (string)GetValue(ProductLabelProperty);
        set => SetValue(ProductLabelProperty, value);
    }

    public event RoutedEventHandler? AddProfileRequested;
    public event RoutedEventHandler? DeleteProfileRequested;
    public event RoutedEventHandler? TemplatesRequested;
    public event RoutedEventHandler? ProfileSelected;

    public ProfileSidebarView() => InitializeComponent();

    private void Add_Click(object sender, RoutedEventArgs e) => AddProfileRequested?.Invoke(sender, e);
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteProfileRequested?.Invoke(sender, e);
    private void Templates_Click(object sender, RoutedEventArgs e) => TemplatesRequested?.Invoke(sender, e);
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0) ProfileSelected?.Invoke(sender, e);
    }
}
