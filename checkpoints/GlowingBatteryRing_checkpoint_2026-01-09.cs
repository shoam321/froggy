// Checkpoint snapshot created: 2026-01-09
// Source: WinUI/Controls/GlowingBatteryRing.xaml.cs

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;
using Windows.UI;

namespace BluetoothWidget.Controls
{
    public sealed partial class GlowingBatteryRing : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(GlowingBatteryRing),
                new PropertyMetadata(100.0, OnValueChanged));

        public static readonly DependencyProperty TimeRemainingProperty =
            DependencyProperty.Register(nameof(TimeRemaining), typeof(string), typeof(GlowingBatteryRing),
                new PropertyMetadata("", OnTimeRemainingChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string TimeRemaining
        {
            get => (string)GetValue(TimeRemainingProperty);
            set => SetValue(TimeRemainingProperty, value);
        }

        public GlowingBatteryRing()
        {
            this.InitializeComponent();
            this.Loaded += GlowingBatteryRing_Loaded;
        }

        private void GlowingBatteryRing_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateRing();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlowingBatteryRing ring)
            {
                ring.UpdateRing();
            }
        }

        private static void OnTimeRemainingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlowingBatteryRing ring)
            {
                ring.TimeRemainingText.Text = e.NewValue?.ToString() ?? "";
            }
        }

        private void UpdateRing()
        {
            var value = Math.Clamp(Value, 0, 100);
            
            // Update percentage text
            PercentageText.Text = $"{(int)value}%";

            // Calculate colors based on battery level
            var (startColor, endColor) = GetGradientColors(value);
            
            // Update gradient colors
            GradientStart.Color = startColor;
            GradientEnd.Color = endColor;
            GlowColor.Color = startColor;

            // Create arc path
            UpdateArcPath(value);
        }

        private (Color start, Color end) GetGradientColors(double percentage)
        {
            if (percentage > 60)
            {
                // Green gradient
                return (Color.FromArgb(255, 76, 175, 80), Color.FromArgb(255, 139, 195, 74));
            }
            else if (percentage > 30)
            {
                // Yellow/Orange gradient
                return (Color.FromArgb(255, 255, 193, 7), Color.FromArgb(255, 255, 152, 0));
            }
            else if (percentage > 15)
            {
                // Orange gradient
                return (Color.FromArgb(255, 255, 152, 0), Color.FromArgb(255, 255, 87, 34));
            }
            else
            {
                // Red gradient
                return (Color.FromArgb(255, 244, 67, 54), Color.FromArgb(255, 229, 57, 53));
            }
        }

        private void UpdateArcPath(double percentage)
        {
            if (percentage <= 0)
            {
                ProgressArc.Data = null;
                return;
            }

            double radius = 32;
            double centerX = 40;
            double centerY = 40;
            
            // Start from top (270 degrees in standard coordinates, but -90 in our system)
            double startAngle = -90;
            double sweepAngle = (percentage / 100.0) * 360;
            double endAngle = startAngle + sweepAngle;

            // Convert to radians
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            // Calculate points
            double startX = centerX + radius * Math.Cos(startRad);
            double startY = centerY + radius * Math.Sin(startRad);
            double endX = centerX + radius * Math.Cos(endRad);
            double endY = centerY + radius * Math.Sin(endRad);

            // Determine if arc is greater than 180 degrees
            bool isLargeArc = sweepAngle > 180;

            // Build path data
            var pathFigure = new PathFigure
            {
                StartPoint = new Point(startX, startY),
                IsClosed = false
            };

            var arcSegment = new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            };

            pathFigure.Segments.Add(arcSegment);

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            ProgressArc.Data = pathGeometry;
        }
    }
}
