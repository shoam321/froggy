using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BluetoothWidget.Controls
{
    public sealed partial class PixelArtControl : UserControl
    {
    }
";

                /// <summary>
                /// Set CSS pixel-art string at runtime and re-render.
                /// </summary>
                public void SetCssPixelArt(string css)
                {
                        if (string.IsNullOrWhiteSpace(css)) return;
                        CssPixelArt = css;
                        RenderFromCss(CssPixelArt, maxWidth: 320, maxHeight: 220);
                }

        public PixelArtControl()
        {
            this.InitializeComponent();
            this.Loaded += PixelArtControl_Loaded;
        }

        private void PixelArtControl_Loaded(object sender, RoutedEventArgs e)
        {
            RenderFromCss(CssPixelArt, maxWidth: 320, maxHeight: 220);
        }

        private void RenderFromCss(string css, double maxWidth = 320, double maxHeight = 220)
        {
            // Parse occurrences like: "10px 10px 0 0 #303f46"
            var matches = Regex.Matches(css, "(\\d+)px\\s+(\\d+)px\\s+0\\s+0\\s+(#[0-9a-fA-F]{6})");
            if (matches.Count == 0)
                return;

            var points = new List<(int x, int y, string color)>();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;

            foreach (Match m in matches)
            {
                int x = int.Parse(m.Groups[1].Value);
                int y = int.Parse(m.Groups[2].Value);
                string c = m.Groups[3].Value;
                points.Add((x, y, c));
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            // Determine cell size (CSS used 10px steps)
            int cell = 10;
            int cols = (maxX - minX) / cell + 1;
            int rows = (maxY - minY) / cell + 1;

            double scaleX = maxWidth / (cols * 1.0);
            double scaleY = maxHeight / (rows * 1.0);
            double scale = Math.Min(scaleX, scaleY);
            double cellSize = Math.Max(1.0, scale);

            PixelCanvas.Children.Clear();
            PixelCanvas.Width = cols * cellSize;
            PixelCanvas.Height = rows * cellSize;

            foreach (var p in points)
            {
                double cx = (p.x - minX) / (double)cell * cellSize;
                double cy = (p.y - minY) / (double)cell * cellSize;

                var rect = new Rectangle
                {
                    Width = cellSize,
                    Height = cellSize,
                    Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                        0xFF,
                        Convert.ToByte(p.color.Substring(1,2),16),
                        Convert.ToByte(p.color.Substring(3,2),16),
                        Convert.ToByte(p.color.Substring(5,2),16)))
                };

                Canvas.SetLeft(rect, cx);
                Canvas.SetTop(rect, cy);
                PixelCanvas.Children.Add(rect);
            }
        }
    }
}
