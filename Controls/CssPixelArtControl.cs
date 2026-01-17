using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BluetoothWidget.Controls
{
    public class CssPixelArtControl : Canvas
    {
        private const double AnimationSlowdownFactor = 4.0;

        private static readonly Regex BoxShadowBlockRegex = new Regex(
            "box-shadow\\s*:\\s*([^;]+);",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex ShadowEntryRegex = new Regex(
            "(\\d+)px\\s+(\\d+)px\\s+0\\s+0\\s+(#[0-9a-fA-F]{6})",
            RegexOptions.Singleline);

        private static readonly Regex CellSizeRegex = new Regex(
            "(?:height|width)\\s*:\\s*(\\d+)px",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex AnimationDurationRegex = new Regex(
            "(?:^|[;{}\\r\\n])\\s*(?:-webkit-)?animation\\s*:\\s*[^;]*?\\b([0-9]*\\.?[0-9]+)\\s*(ms|s)\\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private readonly List<string> _frames = new List<string>();
        private int _frameIndex = 0;
        private DispatcherTimer? _animationTimer;

        public static readonly DependencyProperty CssProperty = DependencyProperty.Register(
            nameof(Css),
            typeof(string),
            typeof(CssPixelArtControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnCssChanged));

        public string Css
        {
            get => (string)GetValue(CssProperty);
            set => SetValue(CssProperty, value);
        }

        public CssPixelArtControl()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            SizeChanged += (_, __) => Render();
            Loaded += (_, __) => Render();
            Unloaded += (_, __) => StopAnimation();
        }

        private static void OnCssChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CssPixelArtControl control)
            {
                control.RebuildFramesAndAnimation();
                control.Render();
            }
        }

        private void RebuildFramesAndAnimation()
        {
            StopAnimation();
            _frames.Clear();
            _frameIndex = 0;

            var css = Css;
            if (string.IsNullOrWhiteSpace(css))
                return;

            // Collect ALL box-shadow blocks (useful for @keyframes animation).
            var matches = BoxShadowBlockRegex.Matches(css);
            foreach (Match m in matches)
            {
                var frame = m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty;
                if (!string.IsNullOrWhiteSpace(frame))
                    _frames.Add(frame);
            }

            // If there are multiple frames, animate through them.
            if (_frames.Count <= 1)
                return;

            var duration = TryParseAnimationDuration(css) ?? TimeSpan.FromSeconds(1);
            duration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * AnimationSlowdownFactor);
            var interval = TimeSpan.FromMilliseconds(Math.Max(16, duration.TotalMilliseconds / _frames.Count));

            _animationTimer = new DispatcherTimer { Interval = interval };
            _animationTimer.Tick += (_, __) =>
            {
                if (_frames.Count == 0)
                    return;
                _frameIndex = (_frameIndex + 1) % _frames.Count;
                Render();
            };
            _animationTimer.Start();
        }

        private void StopAnimation()
        {
            if (_animationTimer == null)
                return;
            _animationTimer.Stop();
            _animationTimer = null;
        }

        private static TimeSpan? TryParseAnimationDuration(string css)
        {
            var m = AnimationDurationRegex.Match(css);
            if (!m.Success)
                return null;

            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return null;

            var unit = m.Groups[2].Value.ToLowerInvariant();
            if (unit == "ms")
                return TimeSpan.FromMilliseconds(value);
            if (unit == "s")
                return TimeSpan.FromSeconds(value);
            return null;
        }

        private void Render()
        {
            var css = Css;
            if (string.IsNullOrWhiteSpace(css))
            {
                Children.Clear();
                return;
            }

            // If given a full CSS block (e.g. @keyframes), extract the first box-shadow list.
            // This keeps rendering fast and avoids duplicating pixels across keyframes.
            string parseText;
            if (_frames.Count > 0 && _frameIndex >= 0 && _frameIndex < _frames.Count)
            {
                parseText = _frames[_frameIndex];
            }
            else
            {
                var boxShadowMatch = BoxShadowBlockRegex.Match(css);
                parseText = boxShadowMatch.Success ? boxShadowMatch.Groups[1].Value : css;
            }

            // Parse occurrences like: "10px 10px 0 0 #303f46"
            var matches = ShadowEntryRegex.Matches(parseText);
            if (matches.Count == 0)
            {
                Children.Clear();
                return;
            }

            int cell = 10;
            var cellMatch = CellSizeRegex.Match(css);
            if (cellMatch.Success && int.TryParse(cellMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCell) && parsedCell > 0)
                cell = parsedCell;

            var pixels = new Dictionary<(int x, int y), string>();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) continue;
                if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) continue;
                var c = m.Groups[3].Value;

                pixels[(x, y)] = c;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;

                // Infer cell size if the CSS didn't include width/height.
                if (!cellMatch.Success)
                {
                    cell = Gcd(cell, x);
                    cell = Gcd(cell, y);
                }
            }

            if (pixels.Count == 0)
            {
                Children.Clear();
                return;
            }

            if (cell <= 0) cell = 10;

            int cols = (maxX - minX) / cell + 1;
            int rows = (maxY - minY) / cell + 1;

            var availableWidth = double.IsNaN(Width) ? ActualWidth : Width;
            var availableHeight = double.IsNaN(Height) ? ActualHeight : Height;

            if (availableWidth <= 0 || availableHeight <= 0)
            {
                // We'll render on the next layout pass.
                return;
            }

            double scaleX = availableWidth / cols;
            double scaleY = availableHeight / rows;
            double cellSize = Math.Max(1.0, Math.Min(scaleX, scaleY));

            Children.Clear();

            // Keep the canvas sized to the rendered art so alignment works cleanly.
            var renderedWidth = cols * cellSize;
            var renderedHeight = rows * cellSize;
            MinWidth = renderedWidth;
            MinHeight = renderedHeight;

            foreach (var kvp in pixels)
            {
                var (x, y) = kvp.Key;
                var color = kvp.Value;

                double cx = (x - minX) / (double)cell * cellSize;
                double cy = (y - minY) / (double)cell * cellSize;

                var rect = new Rectangle
                {
                    Width = cellSize,
                    Height = cellSize,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
                };

                SetLeft(rect, cx);
                SetTop(rect, cy);
                Children.Add(rect);
            }
        }

        private static int Gcd(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            if (a == 0) return b;
            if (b == 0) return a;

            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }

            return a;
        }
    }
}
