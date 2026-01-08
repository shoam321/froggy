using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BluetoothWidget.Controls;

/// <summary>
/// A circular progress ring that displays battery level with gradient colors.
/// Teal (high) -> Amber (medium) -> Red (low)
/// </summary>
public sealed class BatteryRing : Control
{
    private Path? _arcPath;
    private Ellipse? _backgroundEllipse;

    public static readonly DependencyProperty BatteryLevelProperty =
        DependencyProperty.Register(nameof(BatteryLevel), typeof(int?), typeof(BatteryRing),
            new PropertyMetadata(null, OnBatteryLevelChanged));

    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(BatteryRing),
            new PropertyMetadata(false, OnIsConnectedChanged));

    public int? BatteryLevel
    {
        get => (int?)GetValue(BatteryLevelProperty);
        set => SetValue(BatteryLevelProperty, value);
    }

    public bool IsConnected
    {
        get => (bool)GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public BatteryRing()
    {
        DefaultStyleKey = typeof(BatteryRing);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _arcPath = GetTemplateChild("PART_Arc") as Path;
        _backgroundEllipse = GetTemplateChild("PART_Background") as Ellipse;
        UpdateVisual();
    }

    private static void OnBatteryLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BatteryRing)d).UpdateVisual();
    }

    private static void OnIsConnectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BatteryRing)d).UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_arcPath == null || _backgroundEllipse == null)
            return;

        var level = BatteryLevel ?? 0;
        var hasLevel = BatteryLevel.HasValue;

        // Update background opacity based on connection
        _backgroundEllipse.Opacity = IsConnected ? 0.15 : 0.08;

        if (!hasLevel)
        {
            _arcPath.Visibility = Visibility.Collapsed;
            return;
        }

        _arcPath.Visibility = Visibility.Visible;

        // Get the gradient brush based on level
        _arcPath.Stroke = GetBatteryBrush(level);

        // Calculate arc geometry
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) size = 80;

        var strokeWidth = 6.0;
        var radius = (size / 2) - strokeWidth;
        var center = size / 2;

        // Create arc path
        var angle = (level / 100.0) * 360;
        var endAngle = angle - 90; // Start from top
        var startAngle = -90.0;

        var pathGeometry = CreateArcGeometry(center, center, radius, startAngle, endAngle, level >= 100);
        _arcPath.Data = pathGeometry;
        _arcPath.StrokeThickness = strokeWidth;
    }

    private static Brush GetBatteryBrush(int level)
    {
        if (level <= 20)
        {
            // Red gradient
            return new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 229, 57, 53), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 82, 82), Offset = 1 }
                }
            };
        }
        else if (level <= 50)
        {
            // Amber gradient
            return new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 179, 0), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 193, 7), Offset = 1 }
                }
            };
        }
        else
        {
            // Teal gradient
            return new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 191, 165), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 29, 233, 182), Offset = 1 }
                }
            };
        }
    }

    private static Geometry CreateArcGeometry(double centerX, double centerY, double radius, double startAngle, double endAngle, bool isFullCircle)
    {
        if (isFullCircle)
        {
            return new EllipseGeometry
            {
                Center = new Windows.Foundation.Point(centerX, centerY),
                RadiusX = radius,
                RadiusY = radius
            };
        }

        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        var startX = centerX + radius * Math.Cos(startRad);
        var startY = centerY + radius * Math.Sin(startRad);
        var endX = centerX + radius * Math.Cos(endRad);
        var endY = centerY + radius * Math.Sin(endRad);

        var isLargeArc = (endAngle - startAngle) > 180;

        var pathFigure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(startX, startY),
            IsClosed = false
        };

        pathFigure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(endX, endY),
            Size = new Windows.Foundation.Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);

        return pathGeometry;
    }
}
