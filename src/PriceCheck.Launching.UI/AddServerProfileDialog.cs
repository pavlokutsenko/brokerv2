using System.Windows;
using System.Windows.Controls;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector;

public sealed class AddServerProfileDialog : Window
{
    private readonly ComboBox _server = new();

    public string ServerName { get; private set; } = "";
    public int ServerId { get; private set; }

    public AddServerProfileDialog(Window owner, IEnumerable<string> existing, bool allowExisting = false)
    {
        var existingNames = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        Owner = owner;
        Title = "Добавить профиль сервера";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Игровой сервер / рынок" });
        _server.ItemsSource = GameServerCatalog.VerifiedNames
            .Where(name => allowExisting || !existingNames.Contains(name)).ToArray();
        _server.SelectedIndex = _server.Items.Count > 0 ? 0 : -1;
        _server.Margin = new Thickness(0, 5, 0, 12);
        content.Children.Add(_server);
        content.Children.Add(new TextBlock
        {
            Text = _server.Items.Count == 0
                ? "Профили для всех четырёх серверов уже созданы."
                : "Gamma, Black, White и Carmine. Проверенный ID сервера подставляется автоматически.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", Width = 82, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var add = new Button { Content = "Добавить", Width = 100, IsDefault = true,
            IsEnabled = _server.Items.Count > 0 };
        add.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        content.Children.Add(buttons);
        Content = content;
        Loaded += (_, _) => _server.Focus();
    }

    private void Accept()
    {
        if (_server.SelectedItem is not string name ||
            !GameServerCatalog.TryGetVerifiedId(name, out var id))
        {
            MessageBox.Show(this, "Выберите один из четырёх серверов.",
                "PriceCheck Collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ServerName = name;
        ServerId = id;
        DialogResult = true;
    }
}
