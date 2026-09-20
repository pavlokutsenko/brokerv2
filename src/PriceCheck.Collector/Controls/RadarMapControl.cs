using System.Windows;
using System.Windows.Media;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Controls;

public sealed class RadarMapControl : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(RadarSnapshot), typeof(RadarMapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public RadarSnapshot? Snapshot
    {
        get => (RadarSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(13, 18, 25)),
            new Pen(new SolidColorBrush(Color.FromRgb(39, 48, 61)), 1), rect, 10, 10);
        if (Snapshot is null || Snapshot.Traders.Count == 0)
        {
            DrawCentered(context, "Ожидание снимка радара", Brushes.Gray);
            return;
        }

        var maxDistance = Math.Max(500, Snapshot.Traders.Select(point => point.Distance).Order().ElementAt((int)(Snapshot.Traders.Count * 0.97)));
        var scale = Math.Min(ActualWidth, ActualHeight) * 0.45 / maxDistance;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 50, 62, 78)), 1);
        for (var ring = 1; ring <= 4; ring++)
            context.DrawEllipse(null, gridPen, center, ring * Math.Min(ActualWidth, ActualHeight) * 0.105, ring * Math.Min(ActualWidth, ActualHeight) * 0.105);
        context.DrawLine(gridPen, new Point(center.X, 18), new Point(center.X, ActualHeight - 18));
        context.DrawLine(gridPen, new Point(18, center.Y), new Point(ActualWidth - 18, center.Y));

        if (Snapshot.CenterZoneConfigured)
        {
            var zoneCenter = new Point(
                center.X + (Snapshot.CenterZoneX - Snapshot.PlayerX) * scale,
                center.Y + (Snapshot.CenterZoneY - Snapshot.PlayerY) * scale);
            var zoneRadius = Snapshot.CenterZoneRadius * scale;
            var zoneColor = Snapshot.IsInsideCenterZone
                ? Color.FromRgb(87, 215, 160)
                : Color.FromRgb(242, 189, 102);
            var zonePen = new Pen(new SolidColorBrush(Color.FromArgb(210, zoneColor.R, zoneColor.G, zoneColor.B)), 1.5)
            {
                DashStyle = DashStyles.Dash
            };
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(16, zoneColor.R, zoneColor.G, zoneColor.B)),
                zonePen, zoneCenter, zoneRadius, zoneRadius);
        }

        var brushes = new Dictionary<int, Brush>
        {
            [1] = new SolidColorBrush(Color.FromRgb(87, 215, 160)),
            [3] = new SolidColorBrush(Color.FromRgb(94, 168, 255)),
            [8] = new SolidColorBrush(Color.FromRgb(242, 189, 102))
        };
        foreach (var trader in Snapshot.Traders)
        {
            var x = center.X + (trader.X - Snapshot.PlayerX) * scale;
            var y = center.Y + (trader.Y - Snapshot.PlayerY) * scale;
            if (x < 8 || y < 8 || x > ActualWidth - 8 || y > ActualHeight - 8) continue;
            var brush = brushes.GetValueOrDefault(trader.KioskType, Brushes.Gray);
            context.PushOpacity(trader.IsVisible ? 1 : 0.28);
            context.DrawEllipse(brush, null, new Point(x, y), trader.IsVisible ? 1.7 : 1.25, trader.IsVisible ? 1.7 : 1.25);
            context.Pop();
        }
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(87, 215, 160)), 2), center, 4.5, 4.5);
    }

    private void DrawCentered(DrawingContext context, string value, Brush brush)
    {
        var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }
}
