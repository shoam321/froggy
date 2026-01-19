using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using WinForms = System.Windows.Forms; // Alias for laptop battery

namespace BluetoothWidget
{
    /// <summary>
    /// Main window for the Bluetooth widget application.
    /// Displays connected Bluetooth devices with battery levels, drain statistics,
    /// and real-time network speed monitoring.
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer? refreshTimer;

        // Restored private fields removed by previous faulty edit
        private bool _isLoading = false;
        private DispatcherTimer? progressAnimationTimer;
        private Dictionary<string, WindowsBluetoothDevice> _currentDevices = new Dictionary<string, WindowsBluetoothDevice>();
        private Dictionary<string, Border> _deviceCards = new Dictionary<string, Border>();
        private NetworkSpeedHelper? _networkSpeedHelper;
        private DispatcherTimer? networkSpeedTimer;
        private double _marqueePosition = 0;
        private List<string> _championQuotes = new List<string>();
        private int _currentQuoteIndex = 0;
        private DispatcherTimer? marqueeTimer;
        private TextSizeLevel _currentTextSize = TextSizeLevel.Medium;
        private const int SnapDistance = 12;
        private const int CornerSnapDistance = 28;
        private bool _hasFoundDevices = false;
        private Border? _laptopBatteryCard;
        private WinForms.NotifyIcon? _trayIcon;

        public MainWindow()
        {
            // Initialize XAML components and hook lifecycle events
            this.InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            // Try to play the intro animation if present
            try
            {
                var videoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "animation.mp4");
                if (System.IO.File.Exists(videoPath))
                {
                    VideoSplashOverlay.Visibility = Visibility.Visible;
                    SplashVideo.Source = new Uri(videoPath);
                    SplashVideo.LoadedBehavior = MediaState.Manual;
                    SplashVideo.Play();
                }
                else
                {
                    VideoSplashOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to play splash video: {ex.Message}");
                VideoSplashOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Hides the video overlay with a fade animation.
        /// </summary>
        private void HideSplashVideo()
        {
            try
            {
                SplashVideo?.Stop();
                
                // Fade out animation
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                
                fadeOut.Completed += (s, e) =>
                {
                    VideoSplashOverlay.Visibility = Visibility.Collapsed;
                };
                
                VideoSplashOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch
            {
                // Fallback: just hide it
                VideoSplashOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Called when the splash video finishes playing.
        /// </summary>
        private void SplashVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            HideSplashVideo();
        }

        /// <summary>
        /// Called if the splash video fails to load/play.
        /// </summary>
        private void SplashVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            Console.WriteLine($"Splash video failed: {e.ErrorException?.Message}");
            HideSplashVideo();
        }

        /// <summary>
        /// Skip button click - immediately hides splash video.
        /// </summary>
        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            HideSplashVideo();
        }

        

        private void OnThemeChanged()
        {
            // Re-apply theme-specific styles when theme changes
            ApplyThemeSpecificStyles();
            
            // Refresh device cards to update styling
            RefreshAllCards();
        }

        #region Loading Animation

        private double _spinnerAngle = 0;
        
        /// <summary>
        /// Starts the smooth spinning loading animation.
        /// </summary>
        private void StartLoadingAnimation()
        {
            if (_isLoading) return;
            
            _isLoading = true;
            _spinnerAngle = 0;
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingText.Text = "Finding Bluetooth devices…";
            
            // Start spinner animation
            progressAnimationTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(16) // Smooth 60fps
            };
            progressAnimationTimer.Tick += SpinnerAnimation_Tick;
            progressAnimationTimer.Start();
        }

        /// <summary>
        /// Animates the spinner rotation smoothly.
        /// </summary>
        private void SpinnerAnimation_Tick(object? sender, EventArgs e)
        {
            _spinnerAngle += 6; // Rotate 6 degrees per tick (smoother)
            if (_spinnerAngle >= 360) _spinnerAngle = 0;
            
            // Apply rotation to the spinner ring
            SpinnerRotate.Angle = _spinnerAngle;
        }

        /// <summary>
        /// Stops the loading animation and hides the panel.
        /// </summary>
        private void StopLoadingAnimation()
        {
            _isLoading = false;
            
            if (progressAnimationTimer != null)
            {
                progressAnimationTimer.Stop();
                progressAnimationTimer.Tick -= SpinnerAnimation_Tick;
                progressAnimationTimer = null;
            }
            
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        #endregion

        private void ApplyThemeSpecificStyles()
        {
            var theme = ThemeManager.CurrentTheme;
            bool isTerminalStyle = theme == AppTheme.Pixel; // Sharp corners for terminal
            
            // Get theme-specific title color
            Brush titleBrush = theme switch
            {
                AppTheme.Pixel => new SolidColorBrush(Color.FromRgb(0, 255, 0)),       // Bright green
                AppTheme.NeonDrift => new SolidColorBrush(Color.FromRgb(255, 121, 198)), // Hot pink
                AppTheme.Moss => new SolidColorBrush(Color.FromRgb(126, 198, 153)),     // Leaf green
                _ => (Brush)FindResource("TextBrush")                                    // Retro uses theme
            };
            
            if (TitleText != null)
            {
                TitleText.FontFamily = new FontFamily("Consolas");
                TitleText.Foreground = titleBrush;
            }
            
            // Apply font to marquee text
            if (MarqueeText != null)
                MarqueeText.FontFamily = new FontFamily("Consolas");
            
            // Update border styles based on theme
            double cornerRadius = isTerminalStyle ? 4 : 12;
            double borderWidth = isTerminalStyle ? 3 : 2;
            
            if (OuterBorder != null)
            {
                OuterBorder.CornerRadius = new CornerRadius(cornerRadius);
                OuterBorder.BorderThickness = new Thickness(borderWidth);
                OuterBorder.BorderBrush = (Brush)FindResource("BorderBrush");
                OuterBorder.Background = (Brush)FindResource("BackgroundBrush");
            }
            
            if (MainBorder != null)
            {
                MainBorder.CornerRadius = new CornerRadius(isTerminalStyle ? 2 : 10);
                MainBorder.BorderThickness = new Thickness(isTerminalStyle ? 1 : 2);
                MainBorder.Margin = new Thickness(isTerminalStyle ? 3 : 1);
                MainBorder.BorderBrush = (Brush)FindResource("SurfaceBrush");
                MainBorder.Background = (Brush)FindResource("SurfaceBrush");
            }
            
            if (TitleBar != null)
            {
                TitleBar.CornerRadius = new CornerRadius(isTerminalStyle ? 0 : 8, isTerminalStyle ? 0 : 8, 0, 0);
            }
            
            if (FooterBorder != null)
            {
                FooterBorder.CornerRadius = new CornerRadius(0, 0, isTerminalStyle ? 0 : 8, isTerminalStyle ? 0 : 8);
            }
        }

        private void RefreshAllCards()
        {
            // Rebuild all device cards so theme-specific layouts and styles apply
            try
            {
                DeviceListPanel.Children.Clear();
                _deviceCards.Clear();

                foreach (var kvp in _currentDevices.ToList())
                {
                    var newCard = CreateDeviceCard(kvp.Value);
                    _deviceCards[kvp.Key] = newCard;
                    DeviceListPanel.Children.Add(newCard);
                }
            }
            catch (Exception ex)
            {
                App.LogToFile("RefreshAllCards", ex);
            }
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string tag)
            {
                if (Enum.TryParse<AppTheme>(tag, out var theme))
                {
                    ThemeManager.SetTheme(theme);
                    OnThemeChanged();
                }
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinButton.Content = Topmost ? "📍" : "📌";
            PinButton.ToolTip = Topmost ? "Unpin (currently on top)" : "Pin on top";
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("MainWindow loaded, starting scan...");
                App.LogToFile("MainWindow_Loaded", null);
                
                // Start marquee animation immediately
                InitializeMarquee();

                // Apply saved theme now that the window is initialized (safer than static init)
                try { ThemeManager.ApplyTheme(); } catch (Exception ex) { App.LogToFile("ApplyThemeFromLoaded", ex); }

                // Subscribe to theme changes so UI updates when theme changes
                ThemeManager.ThemeChanged += OnThemeChanged;

                try
                {
                    await ScanForDevices();
                }
                catch (Exception ex)
                {
                    App.LogToFile("ScanForDevices_Exception", ex);
                    throw;
                }
                
                // Set up auto-refresh every 60 seconds for Bluetooth (reduced from 30)
                refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(60) // Reduced from 30 to 60 seconds
                };
                refreshTimer.Tick += async (s, args) => await ScanForDevices();
                refreshTimer.Start();
                Console.WriteLine("Auto-refresh timer started");

                // Initialize network speed monitoring
                InitializeNetworkSpeedMonitor();
            }
            catch (Exception ex)
            {
                App.LogToFile("MainWindow_Loaded_Exception", ex);
                Console.WriteLine($"ERROR in MainWindow_Loaded: {ex}");
                MessageBox.Show($"Error starting widget: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Initializes the network speed monitor with a 2-second update interval.
        /// Tracks download/upload speeds and ping latency.
        /// </summary>
        private void InitializeNetworkSpeedMonitor()
        {
            try
            {
                _networkSpeedHelper = new NetworkSpeedHelper();
                
                networkSpeedTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5) // Reduced from 2 to 5 seconds
                };
                networkSpeedTimer.Tick += async (s, args) => await UpdateNetworkSpeedAsync();
                networkSpeedTimer.Start();
                
                // Initial update
                _ = UpdateNetworkSpeedAsync();
                Console.WriteLine("Network speed monitor started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing network monitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the rolling marquee with Leona and Kayle quotes.
        /// </summary>
        private void InitializeMarquee()
        {
            // Position text to start from the right edge
            _marqueePosition = MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 400;
            Canvas.SetLeft(MarqueeText, _marqueePosition);
            
            // Ensure we have at least one quote to avoid index errors
            if (_championQuotes == null || _championQuotes.Count == 0)
            {
                _championQuotes = new List<string> { MarqueeText.Text ?? "" };
                _currentQuoteIndex = 0;
            }

            // Ensure current index is valid and set initial quote
            if (_currentQuoteIndex < 0 || _championQuotes.Count == 0 || _currentQuoteIndex >= _championQuotes.Count)
            {
                App.LogToFile("Marquee_Index_Adjust", new Exception($"Adjusting marquee index from {_currentQuoteIndex} to 0. QuoteCount={_championQuotes.Count}"));
                _currentQuoteIndex = 0;
            }
            MarqueeText.Text = _championQuotes[_currentQuoteIndex];
            
            // Create timer for smooth animation (~20fps instead of ~33fps)
            marqueeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // Reduced from 30 to 50ms
            };
            marqueeTimer.Tick += MarqueeTimer_Tick;
            marqueeTimer.Start();
            Console.WriteLine("Marquee animation started");
        }

        /// <summary>
        /// Animates the marquee text scrolling from right to left.
        /// </summary>
        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            // Move text to the left
            _marqueePosition -= 2; // Speed of scrolling (pixels per tick)
            
            // Measure text width
            MarqueeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = MarqueeText.DesiredSize.Width;
            
            // When text has scrolled completely off the left side, reset to right
            if (_marqueePosition < -textWidth)
            {
                // Move to next quote (guard against empty list just in case)
                if (_championQuotes != null && _championQuotes.Count > 0)
                {
                    _currentQuoteIndex = (_currentQuoteIndex + 1) % _championQuotes.Count;
                    MarqueeText.Text = _championQuotes[_currentQuoteIndex];
                }

                // Reset position to start from right edge
                _marqueePosition = MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 400;
            }
            
            Canvas.SetLeft(MarqueeText, _marqueePosition);
        }

        /// <summary>
        /// Updates the network speed display with current measurements.
        /// </summary>
        private async Task UpdateNetworkSpeedAsync()
        {
            if (_networkSpeedHelper == null) return;

            try
            {
                await _networkSpeedHelper.UpdateSpeedAsync();

                // Update UI on dispatcher thread
                Dispatcher.Invoke(() =>
                {
                    if (_networkSpeedHelper.IsConnected)
                    {
                        var downloadSpeed = NetworkSpeedHelper.FormatSpeed(_networkSpeedHelper.DownloadSpeedBps);
                        var uploadSpeed = NetworkSpeedHelper.FormatSpeed(_networkSpeedHelper.UploadSpeedBps);
                        
                        NetworkSpeedText.Text = $"↓ {downloadSpeed}  ↑ {uploadSpeed}";
                        
                        if (_networkSpeedHelper.PingMs >= 0)
                        {
                            PingText.Text = $"Ping: {_networkSpeedHelper.PingMs}ms";
                            
                            // Color code ping based on latency
                            if (_networkSpeedHelper.PingMs < 50)
                                PingText.Foreground = (Brush)FindResource("SuccessBrush");
                            else if (_networkSpeedHelper.PingMs < 100)
                                PingText.Foreground = (Brush)FindResource("WarningBrush");
                            else
                                PingText.Foreground = (Brush)FindResource("DangerBrush");
                        }
                        else
                        {
                            PingText.Text = "Ping: --";
                            PingText.Foreground = (Brush)FindResource("SubTextBrush");
                        }
                        
                        // Truncate adapter name if too long
                        var adapterName = _networkSpeedHelper.AdapterName;
                        if (adapterName.Length > 15)
                            adapterName = adapterName.Substring(0, 12) + "...";
                        NetworkNameText.Text = adapterName;
                    }
                    else
                    {
                        NetworkSpeedText.Text = "No Connection";
                        PingText.Text = "Ping: --";
                        NetworkNameText.Text = "Disconnected";
                        PingText.Foreground = (Brush)FindResource("SubTextBrush");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating network speed: {ex.Message}");
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        /// <summary>
        /// Handles the resize grip drag to resize the window.
        /// The grip stays fixed in the bottom-right corner.
        /// </summary>
        private void ResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            // Calculate new dimensions
            double newWidth = Width + e.HorizontalChange;
            double newHeight = Height + e.VerticalChange;
            
            // Enforce minimum size
            if (newWidth >= MinWidth)
                Width = newWidth;
            if (newHeight >= MinHeight)
                Height = newHeight;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Minimizes the window to taskbar.
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Shows information about what Froggy is.
        /// </summary>
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutText = @"FROGGY - Bluetooth Battery Widget

       ,     ,
      /(     )\
     (  \___/  )
     (  o   o  )
      \   V   /
      /`-----'\
     /  _   _  \
    |  | | | |  |
    |  |_| |_|  |
    |   _   _   |
   (O) | | | | (O)
       | | | |
       |_| |_|

What is this?
Froggy monitors your Bluetooth devices (headphones, mice, keyboards, etc.) and shows their battery levels in real-time.

Features:
• Real-time battery % for all connected Bluetooth devices
• Battery drain rate tracking (shows how fast battery depletes)
• Estimated time remaining until empty
• Network speed monitor (download/upload + ping)
• 4 beautiful themes: Retro, Pixel, NeonDrift, Moss
• Screen edge snapping (drag to corners!)
• Rolling quotes from Leona & Kayle

Tips:
• Right-click a device to rename it
• Click 'A' to change text size
• Click the paint icon to switch themes
• Hover over title for quick help

Made with love for gamers who need to know when their headset is about to die mid-match!";

            MessageBox.Show(aboutText, "About Froggy", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #region Text Size Methods

        /// <summary>
        /// Loads the saved text size from settings and applies it.
        /// </summary>
        private void LoadSavedTextSize()
        {
            _currentTextSize = DeviceSettings.GetTextSize();
            ApplyTextSize(_currentTextSize);
        }

        /// <summary>
        /// Applies the specified text size level to UI elements.
        /// </summary>
        private void ApplyTextSize(TextSizeLevel size)
        {
            _currentTextSize = size;
            
            // Get font size multipliers based on level - increased for better readability
            var (titleSize, normalSize, smallSize) = size switch
            {
                TextSizeLevel.Small => (18.0, 14.0, 12.0),
                TextSizeLevel.Large => (26.0, 20.0, 16.0),
                _ => (22.0, 16.0, 14.0) // Medium - default, comfortable reading size
            };
            
            // Update title
            if (TitleText != null)
                TitleText.FontSize = titleSize;
            
            // Update marquee text
            if (MarqueeText != null)
                MarqueeText.FontSize = smallSize;
            
            // Update loading text
            if (LoadingText != null)
                LoadingText.FontSize = normalSize;
            
            // Update text size button tooltip to show current size
            if (TextSizeButton != null)
            {
                TextSizeButton.ToolTip = $"Text Size: {size} (click to change)";
                // Update button content to indicate size
                TextSizeButton.Content = size switch
                {
                    TextSizeLevel.Small => "A?",
                    TextSizeLevel.Large => "A?",
                    _ => "A"
                };
            }
            
            // Refresh device cards to apply new sizes
            RefreshAllCardsTextSize(normalSize, smallSize);
        }

        /// <summary>
        /// Updates font sizes in all device cards.
        /// </summary>
        private void RefreshAllCardsTextSize(double normalSize, double smallSize)
        {
            foreach (var card in _deviceCards.Values)
            {
                // Find text elements in the card and update their sizes
                if (card.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb)
                        {
                            // Device name gets normal size, status/details get small
                            if (tb.FontWeight == FontWeights.Bold || tb.FontWeight == FontWeights.SemiBold)
                                tb.FontSize = normalSize;
                            else
                                tb.FontSize = smallSize;
                        }
                        else if (child is StackPanel sp)
                        {
                            foreach (var spChild in sp.Children)
                            {
                                if (spChild is TextBlock stb)
                                    stb.FontSize = smallSize;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cycles through text size levels: Small -> Medium -> Large -> Small
        /// </summary>
        private void TextSizeButton_Click(object sender, RoutedEventArgs e)
        {
            var newSize = DeviceSettings.CycleTextSize();
            ApplyTextSize(newSize);
        }

        #endregion

        #region Screen Edge Snapping (Aero Snap Style)

        // Windows message constants for detecting window movement
        private const int WM_EXITSIZEMOVE = 0x0232;  // Fired when drag/resize ends
        private const int WM_MOVING = 0x0216;        // Fired during window move
        private const int WM_ENTERSIZEMOVE = 0x0231; // Fired when drag/resize starts
        
        private HwndSource? _hwndSource;
        private bool _isDragging = false;

        /// <summary>
        /// Hook into Windows messages for better snap detection.
        /// </summary>
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);
        }

        /// <summary>
        /// Windows message handler for detecting when window drag ends.
        /// This provides much better "smash into corner" behavior.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_ENTERSIZEMOVE:
                    _isDragging = true;
                    break;
                    
                case WM_EXITSIZEMOVE:
                    // Window drag/resize ended - perfect time to snap!
                    _isDragging = false;
                    SnapToNearestEdgeOrCorner();
                    break;
            }
            
            return IntPtr.Zero;
        }

        /// <summary>
        /// Snaps the window to the nearest screen edge or corner.
        /// Prioritizes corners when window is near two edges simultaneously.
        /// </summary>
        private void SnapToNearestEdgeOrCorner()
        {
            try
            {
                // Get the screen the window center is on
                var windowCenter = new System.Drawing.Point(
                    (int)(Left + ActualWidth / 2),
                    (int)(Top + ActualHeight / 2));
                var screen = System.Windows.Forms.Screen.FromPoint(windowCenter);
                var workArea = screen.WorkingArea;
                
                // Calculate distances to each edge
                double distToLeft = Left - workArea.Left;
                double distToRight = workArea.Right - (Left + ActualWidth);
                double distToTop = Top - workArea.Top;
                double distToBottom = workArea.Bottom - (Top + ActualHeight);
                
                // Check if we're near edges (within snap distance)
                bool nearLeft = distToLeft <= SnapDistance && distToLeft >= -CornerSnapDistance;
                bool nearRight = distToRight <= SnapDistance && distToRight >= -CornerSnapDistance;
                bool nearTop = distToTop <= SnapDistance && distToTop >= -CornerSnapDistance;
                bool nearBottom = distToBottom <= SnapDistance && distToBottom >= -CornerSnapDistance;
                
                // Also snap if window is pushed beyond screen edge (negative distance)
                if (distToLeft < 0) nearLeft = true;
                if (distToRight < 0) nearRight = true;
                if (distToTop < 0) nearTop = true;
                if (distToBottom < 0) nearBottom = true;
                
                double newLeft = Left;
                double newTop = Top;
                bool shouldSnap = false;
                
                // Corner snapping (prioritized - check corners first)
                if (nearLeft && nearTop)
                {
                    // Top-left corner
                    newLeft = workArea.Left;
                    newTop = workArea.Top;
                    shouldSnap = true;
                }
                else if (nearRight && nearTop)
                {
                    // Top-right corner
                    newLeft = workArea.Right - ActualWidth;
                    newTop = workArea.Top;
                    shouldSnap = true;
                }
                else if (nearLeft && nearBottom)
                {
                    // Bottom-left corner
                    newLeft = workArea.Left;
                    newTop = workArea.Bottom - ActualHeight;
                    shouldSnap = true;
                }
                else if (nearRight && nearBottom)
                {
                    // Bottom-right corner
                    newLeft = workArea.Right - ActualWidth;
                    newTop = workArea.Bottom - ActualHeight;
                    shouldSnap = true;
                }
                // Edge snapping (if not at a corner)
                else if (nearLeft)
                {
                    newLeft = workArea.Left;
                    shouldSnap = true;
                }
                else if (nearRight)
                {
                    newLeft = workArea.Right - ActualWidth;
                    shouldSnap = true;
                }
                else if (nearTop)
                {
                    newTop = workArea.Top;
                    shouldSnap = true;
                }
                else if (nearBottom)
                {
                    newTop = workArea.Bottom - ActualHeight;
                    shouldSnap = true;
                }
                
                // Apply snap with smooth animation
                if (shouldSnap && (Math.Abs(newLeft - Left) > 1 || Math.Abs(newTop - Top) > 1))
                {
                    AnimateSnapTo(newLeft, newTop);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Snap error: {ex.Message}");
            }
        }

        /// <summary>
        /// Animates the window snapping to target position for smooth UX.
        /// </summary>
        private void AnimateSnapTo(double targetLeft, double targetTop)
        {
            var duration = TimeSpan.FromMilliseconds(150);
            var ease = new System.Windows.Media.Animation.QuadraticEase 
            { 
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut 
            };
            
            var leftAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Left,
                To = targetLeft,
                Duration = duration,
                EasingFunction = ease
            };
            
            var topAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Top,
                To = targetTop,
                Duration = duration,
                EasingFunction = ease
            };
            
            // Apply animations
            BeginAnimation(LeftProperty, leftAnimation);
            BeginAnimation(TopProperty, topAnimation);
        }

        #endregion

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanForDevices();
        }

        private async Task ScanForDevices()
        {
            try
            {
                Console.WriteLine("ScanForDevices called");
                App.LogToFile("ScanForDevices", null);
                
                // Only show loading animation if we haven't found devices yet
                if (!_hasFoundDevices)
                {
                    StartLoadingAnimation();
                }

                // Use Windows API directly for better compatibility
                await GetWindowsBluetoothDevices();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in ScanForDevices: {ex}");
                StopLoadingAnimation();
                LoadingPanel.Visibility = Visibility.Visible;
                LoadingText.Text = $"Error: {ex.Message}";
            }
        }

        private async Task GetWindowsBluetoothDevices()
        {
            try
            {
                var deviceInfo = new WindowsBluetoothHelper();
                // Devices now include dongle HID battery support (Cloud II, Alpha/III, Pulsefire, etc.)
                var devices = await deviceInfo.GetConnectedDevicesAsync();

                // Only show connected devices
                var connectedDevices = devices.Where(d => d.IsConnected).ToList();

                // Stop animation immediately when we have results
                StopLoadingAnimation();
                
                // Always update laptop battery first
                UpdateLaptopBatteryCard();

                if (connectedDevices.Any())
                {
                    // Mark that we found devices - never show loading animation again
                    _hasFoundDevices = true;
                    
                    var currentDeviceIds = new HashSet<string>();
                    
                    foreach (var device in connectedDevices)
                    {
                        currentDeviceIds.Add(device.Id);
                        
                        if (_deviceCards.TryGetValue(device.Id, out var existingCard))
                        {
                            // Update existing card in-place
                            UpdateDeviceCard(existingCard, device);
                        }
                        else
                        {
                            // Add new device card
                            var newCard = CreateDeviceCard(device);
                            _deviceCards[device.Id] = newCard;
                            DeviceListPanel.Children.Add(newCard);
                        }
                    }
                    
                    // Remove devices that are no longer present
                    var toRemove = _deviceCards.Keys.Where(id => !currentDeviceIds.Contains(id)).ToList();
                    foreach (var id in toRemove)
                    {
                        if (_deviceCards.TryGetValue(id, out var card))
                        {
                            DeviceListPanel.Children.Remove(card);
                            _deviceCards.Remove(id);
                        }
                    }
                    
                    Console.WriteLine($"{connectedDevices.Count} device(s) connected");
                }
                else
                {
                    // Stop animation and show empty state
                    StopLoadingAnimation();
                    
                    // Clear all if no devices
                    DeviceListPanel.Children.Clear();
                    _deviceCards.Clear();
                    
                    // Still show laptop battery even if no Bluetooth devices
                    UpdateLaptopBatteryCard();
                    
                    LoadingPanel.Visibility = Visibility.Visible;
                    LoadingText.Visibility = Visibility.Visible;
                    SpinnerArc.Visibility = Visibility.Collapsed;
                    LoadingText.Text = "No Bluetooth devices found yet.\n\nTry:\n• Turn Bluetooth ON in Windows\n• Make sure your device is powered on\n• Pair/connect it in Settings";
                }
            }
            catch (Exception ex)
            {
                App.LogToFile("GetWindowsBluetoothDevices", ex);
                StopLoadingAnimation();
                LoadingPanel.Visibility = Visibility.Visible;
                LoadingText.Visibility = Visibility.Visible;
                SpinnerArc.Visibility = Visibility.Collapsed;
                LoadingText.Text = $"Error accessing Bluetooth:\n{ex.Message}\n\nTry:\n- Enable Bluetooth in Settings\n- Run as Administrator";
            }
        }

        /// <summary>
        /// Gets laptop battery info and creates/updates the laptop battery card.
        /// </summary>
        private void UpdateLaptopBatteryCard()
        {
            try
            {
                var powerStatus = WinForms.SystemInformation.PowerStatus;
                
                // Only show if laptop has a battery
                if (powerStatus.BatteryChargeStatus == WinForms.BatteryChargeStatus.NoSystemBattery)
                    return;
                
                int batteryPercent = (int)(powerStatus.BatteryLifePercent * 100);
                bool isCharging = powerStatus.PowerLineStatus == WinForms.PowerLineStatus.Online;
                string timeRemaining = "";
                
                if (powerStatus.BatteryLifeRemaining > 0 && !isCharging)
                {
                    var remaining = TimeSpan.FromSeconds(powerStatus.BatteryLifeRemaining);
                    timeRemaining = $"~{remaining.Hours}h {remaining.Minutes}m left";
                }
                else if (isCharging)
                {
                    timeRemaining = "Charging";
                }
                
                if (_laptopBatteryCard == null)
                {
                    _laptopBatteryCard = CreateLaptopBatteryCard(batteryPercent, isCharging, timeRemaining);
                    DeviceListPanel.Children.Insert(0, _laptopBatteryCard); // Add at top
                }
                else
                {
                    UpdateLaptopBatteryCardContent(_laptopBatteryCard, batteryPercent, isCharging, timeRemaining);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting laptop battery: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a card displaying the laptop's battery status.
        /// </summary>
        private Border CreateLaptopBatteryCard(int batteryPercent, bool isCharging, string timeRemaining)
        {
            // AURORA: Use completely different card design
            if (ThemeManager.CurrentTheme == AppTheme.Aurora)
            {
                return CreateAuroraLaptopCard(batteryPercent, isCharging, timeRemaining);
            }

            var card = new Border
            {
                Style = (Style)FindResource("DeviceItemStyle"),
                Tag = "LaptopBattery"
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left side - device info (matching Bluetooth device card style)
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            // Aurora uses gradient text, others use solid
            Brush nameBrush = ThemeManager.CurrentTheme == AppTheme.Aurora 
                ? CreateAuroraGradientBrush() 
                : (Brush)FindResource("PrimaryBrush");
            
            var nameText = new TextBlock
            {
                Text = "Laptop",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                FontFamily = ThemeManager.CurrentTheme == AppTheme.Aurora ? new FontFamily("Segoe UI") : new FontFamily("Consolas"),
                Foreground = nameBrush
            };
            infoPanel.Children.Add(nameText);

            var statusText = new TextBlock
            {
                Text = isCharging ? "● CHARGING" : "● ON BATTERY",
                FontSize = 10,
                Foreground = isCharging ? Brushes.Orange : Brushes.LimeGreen,
                Margin = new Thickness(0, 4, 0, 0)
            };
            infoPanel.Children.Add(statusText);

            var timeText = new TextBlock
            {
                Text = timeRemaining,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SubTextBrush"),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = string.IsNullOrEmpty(timeRemaining) ? Visibility.Collapsed : Visibility.Visible
            };
            infoPanel.Children.Add(timeText);

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // Right side - battery percentage (matching Bluetooth device card style)
            var batteryPanel = new StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var batteryIcon = new TextBlock
            {
                Text = isCharging ? "🔌" : "💻",
                FontSize = 24,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            batteryPanel.Children.Add(batteryIcon);

            var percentText = new TextBlock
            {
                Text = $"{batteryPercent}%",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = GetBatteryColor(batteryPercent),
                VerticalAlignment = VerticalAlignment.Center
            };
            batteryPanel.Children.Add(percentText);

            Grid.SetColumn(batteryPanel, 1);
            grid.Children.Add(batteryPanel);

            card.Child = grid;
            return card;
        }

        /// <summary>
        /// Updates an existing laptop battery card with new values.
        /// </summary>
        private void UpdateLaptopBatteryCardContent(Border card, int batteryPercent, bool isCharging, string timeRemaining)
        {
            if (card.Child is Grid grid)
            {
                // Update status (guard against unexpected/missing children)
                if (grid.Children.Count > 0 && grid.Children[0] is StackPanel infoPanel)
                {
                    if (infoPanel.Children.Count > 1 && infoPanel.Children[1] is TextBlock statusText)
                    {
                        statusText.Text = isCharging ? "● CHARGING" : "● ON BATTERY";
                        statusText.Foreground = isCharging ? Brushes.Orange : Brushes.LimeGreen;
                    }

                    // Update time remaining
                    if (infoPanel.Children.Count > 2 && infoPanel.Children[2] is TextBlock timeText)
                    {
                        timeText.Text = timeRemaining;
                        timeText.Visibility = string.IsNullOrEmpty(timeRemaining) ? Visibility.Collapsed : Visibility.Visible;
                    }
                }

                // Update battery display (guard against missing children)
                if (grid.Children.Count > 1 && grid.Children[1] is StackPanel batteryPanel)
                {
                    if (batteryPanel.Children.Count > 0 && batteryPanel.Children[0] is TextBlock icon)
                        icon.Text = isCharging ? "🔌" : "💻";
                    if (batteryPanel.Children.Count > 1 && batteryPanel.Children[1] is TextBlock percent)
                    {
                        percent.Text = $"{batteryPercent}%";
                        percent.Foreground = GetBatteryColor(batteryPercent);
                    }
                }
            }
        }

        private Border CreateDeviceCard(WindowsBluetoothDevice device)
        {
            // Store device for later reference
            _currentDevices[device.Id] = device;
            
            // Record battery reading for tracking (only for connected devices)
            if (device.BatteryLevel.HasValue && device.IsConnected)
            {
                BatteryTracker.RecordBattery(device.Id, device.Name, device.BatteryLevel.Value);
            }

            // Get display name (custom or original)
            string displayName = DeviceSettings.GetDisplayName(device.Id, device.Name);

            var deviceBorder = new Border
            {
                Style = (Style)FindResource("DeviceItemStyle"),
                Tag = device.Id  // Store device ID for later updates
            };

            // Apply Aurora special effects if using Aurora theme
            var theme = ThemeManager.CurrentTheme;
            if (theme == AppTheme.Aurora)
            {
                ApplyAuroraCardEffect(deviceBorder);
            }

            // Theme-aware context menu colors based on current theme
            var (menuBg, menuBorder, menuText) = theme switch
            {
                AppTheme.Pixel => (Color.FromRgb(37, 37, 37), Color.FromRgb(0, 170, 0), Color.FromRgb(0, 255, 0)),
                AppTheme.NeonDrift => (Color.FromRgb(37, 24, 50), Color.FromRgb(189, 147, 249), Color.FromRgb(232, 224, 240)),
                AppTheme.Moss => (Color.FromRgb(37, 45, 33), Color.FromRgb(74, 93, 62), Color.FromRgb(212, 207, 196)),
                AppTheme.Aurora => (Color.FromRgb(26, 35, 50), Color.FromRgb(0, 229, 160), Color.FromRgb(232, 244, 248)),
                _ => (Color.FromRgb(26, 26, 46), Color.FromRgb(0, 255, 255), Color.FromRgb(224, 224, 224)) // Retro
            };

            // Add context menu for renaming
            var contextMenu = new ContextMenu
            {
                Background = new SolidColorBrush(menuBg),
                BorderBrush = new SolidColorBrush(menuBorder),
                BorderThickness = new Thickness(2)
            };
            
            var renameItem = new MenuItem 
            { 
                Header = "✏️ Rename Device",
                Foreground = new SolidColorBrush(menuText)
            };
            renameItem.Click += (s, e) => RenameDevice(device.Id, device.Name);
            contextMenu.Items.Add(renameItem);
            
            if (DeviceSettings.HasCustomName(device.Id))
            {
                var resetItem = new MenuItem 
                { 
                    Header = "↩️ Reset to Original Name",
                    Foreground = new SolidColorBrush(menuText)
                };
                resetItem.Click += (s, e) => ResetDeviceName(device.Id);
                contextMenu.Items.Add(resetItem);
            }
            
            deviceBorder.ContextMenu = contextMenu;

            // Instead of returning a Border, add the device ViewModel to the ObservableCollection
            var vm = new BluetoothDeviceViewModel(device);
            _devices.Add(vm);
            // The GridView will use the DataTemplate to render the card
            return null;
        }

        private void UpdateDeviceCard(Border card, WindowsBluetoothDevice device)
        {
            // Update stored device
            _currentDevices[device.Id] = device;
            
            // Record battery reading for tracking (only for connected devices)
            if (device.BatteryLevel.HasValue && device.IsConnected)
            {
                BatteryTracker.RecordBattery(device.Id, device.Name, device.BatteryLevel.Value);
            }

            // Get display name
            string displayName = DeviceSettings.GetDisplayName(device.Id, device.Name);

            if (card.Child is not Grid grid) return;

            // Theme-aware colors for update
            bool isPixel = ThemeManager.CurrentTheme == AppTheme.Pixel;
            var nameColor = isPixel 
                ? (Brush)new SolidColorBrush(Color.FromRgb(0, 255, 0))  // Bright green on dark
                : new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(0, 255, 255), 0);
            var statsColor = isPixel
                ? Color.FromRgb(255, 170, 0)     // Amber for pixel dark
                : Color.FromRgb(255, 0, 255);    // Magenta for retro

            // Find and update elements (text + simple indicators)
            foreach (var child in grid.Children)
            {
                if (child is StackPanel stack)
                {
                    foreach (var stackChild in stack.Children)
                    {
                        if (stackChild is TextBlock tb)
                        {
                            switch (tb.Name)
                            {
                                case "DeviceName":
                                    tb.Text = displayName;
                                    tb.Foreground = nameColor;
                                    break;
                                case "StatusText":
                                    tb.Text = device.IsConnected ? "● ONLINE" : "○ OFFLINE";
                                    tb.Foreground = device.IsConnected 
                                        ? new SolidColorBrush(Color.FromRgb(0, 255, 136))  // Neon green
                                        : (SolidColorBrush)FindResource("SubTextBrush");
                                    break;
                                case "StatsText":
                                    tb.Text = device.BatteryLevel.HasValue && device.IsConnected ? BatteryTracker.GetSummaryText(device.Id) : "";
                                    tb.Foreground = new SolidColorBrush(statsColor);
                                    break;
                                case "BatteryIcon":
                                    tb.Text = GetBatteryIcon(device.BatteryLevel);
                                    break;
                                case "BatteryText":
                                    tb.Text = GetBatteryDisplayText(device);
                                    tb.Foreground = GetBatteryColor(device.BatteryLevel, device.IsConnected);
                                    tb.ToolTip = GetBatteryTooltip(device);
                                    break;
                            }
                        }
                        else if (stackChild is ProgressBar pb)
                        {
                            if (pb.Name == "BatteryProgress")
                            {
                                pb.Value = device.BatteryLevel ?? 0;
                            }
                        }
                        else if (stackChild is Grid innerGrid)
                        {
                            // Pixel theme battery bar fill update
                            foreach (var gChild in innerGrid.Children)
                            {
                                if (gChild is Rectangle r && r.Name == "BatteryFill")
                                {
                                    var level = device.BatteryLevel ?? 0;
                                    r.Width = Math.Max(4, level * 80 / 100);
                                }
                            }
                        }
                        else if (stackChild is StackPanel innerStack)
                        {
                            foreach (var innerChild in innerStack.Children)
                            {
                                if (innerChild is TextBlock innerTb)
                                {
                                    switch (innerTb.Name)
                                    {
                                        case "BatteryIcon":
                                            innerTb.Text = GetBatteryIcon(device.BatteryLevel);
                                            break;
                                        case "BatteryText":
                                            innerTb.Text = GetBatteryDisplayText(device);
                                            innerTb.Foreground = GetBatteryColor(device.BatteryLevel, device.IsConnected);
                                            innerTb.ToolTip = GetBatteryTooltip(device);
                                            break;
                                    }
                                }
                                else if (innerChild is ProgressBar innerPb)
                                {
                                    if (innerPb.Name == "BatteryProgress")
                                    {
                                        innerPb.Value = device.BatteryLevel ?? 0;
                                    }
                                }
                                else if (innerChild is Grid g)
                                {
                                    foreach (var gChild in g.Children)
                                    {
                                        if (gChild is Rectangle r && r.Name == "BatteryFill")
                                        {
                                            var level = device.BatteryLevel ?? 0;
                                            r.Width = Math.Max(4, level * 80 / 100);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private string GetBatteryDisplayText(WindowsBluetoothDevice device)
        {
            if (!device.BatteryLevel.HasValue)
                return "N/A";
            
            // Show "(last)" for disconnected devices (cached battery)
            if (!device.IsConnected)
                return $"{device.BatteryLevel}% (last)";
            
            // Add ~ prefix for fallback (less reliable) readings
            return device.IsBatteryFallback ? $"~{device.BatteryLevel}%" : $"{device.BatteryLevel}%";
        }

        /// <summary>
        /// Creates the beautiful Aurora gradient brush for text
        /// </summary>
        private LinearGradientBrush CreateAuroraGradientBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 160), 0));    // Aurora teal
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 208), 0.5));  // Bright cyan
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 184, 212), 1));    // Deep cyan
            return brush;
        }

        /// <summary>
        /// Applies Aurora-specific styling to a device card border
        /// </summary>
        private void ApplyAuroraCardEffect(Border border)
        {
            // Aurora glass gradient background
            var glassBg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(40, 26, 35, 50), 0));
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(25, 26, 35, 50), 1));
            border.Background = glassBg;
            
            // Aurora gradient border
            var borderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 0, 229, 160), 0));    // Teal
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 0, 184, 212), 0.5));  // Cyan
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 224, 64, 251), 1));   // Magenta
            border.BorderBrush = borderBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(16);
            border.Padding = new Thickness(18);
            
            // Soft glow effect
            border.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 229, 160),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.2
            };
            
            // Hover animation
            border.MouseEnter += (s, e) =>
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 229, 160),
                    BlurRadius = 25,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            };
            border.MouseLeave += (s, e) =>
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 229, 160),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.2
                };
            };
        }

        /// <summary>
        /// Creates Aurora-style laptop battery card with circular ring
        /// </summary>
        private Border CreateAuroraLaptopCard(int batteryPercent, bool isCharging, string timeRemaining)
        {
            var card = new Border
            {
                Tag = "LaptopBattery",
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 14),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16),
            };
            
            // Glass background with slight purple tint for laptop
            var glassBg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(45, 40, 30, 60), 0));
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(30, 20, 20, 40), 1));
            card.Background = glassBg;
            
            // Purple-tinted gradient border for laptop
            var borderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 224, 64, 251), 0));
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 0, 184, 212), 0.5));
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 229, 160), 1));
            card.BorderBrush = borderBrush;
            card.BorderThickness = new Thickness(1.5);
            
            // Purple glow for laptop
            card.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(224, 64, 251),
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.25
            };

            // Main layout
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Circular battery ring
            var ringContainer = new Grid
            {
                Width = 60,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var bgRing = new Ellipse
            {
                Width = 56,
                Height = 56,
                StrokeThickness = 5,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Fill = Brushes.Transparent
            };
            ringContainer.Children.Add(bgRing);

            var progressRing = CreateBatteryArc(batteryPercent, true);
            ringContainer.Children.Add(progressRing);

            // Charging icon or percentage
            var centerContent = new TextBlock
            {
                Text = isCharging ? "⚡" : $"{batteryPercent}%",
                FontSize = isCharging ? 20 : 13,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 248)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ringContainer.Children.Add(centerContent);

            Grid.SetColumn(ringContainer, 0);
            mainGrid.Children.Add(ringContainer);

            // Info panel
            var infoStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            // Laptop name with magenta gradient
            var laptopBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            laptopBrush.GradientStops.Add(new GradientStop(Color.FromRgb(224, 64, 251), 0));
            laptopBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 184, 212), 1));

            var nameText = new TextBlock
            {
                Text = "💻 Laptop",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = laptopBrush
            };
            infoStack.Children.Add(nameText);

            // Status
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var statusDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = isCharging 
                    ? new SolidColorBrush(Color.FromRgb(255, 215, 64))
                    : new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isCharging)
            {
                var pulseAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.4,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.8),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                statusDot.BeginAnimation(Ellipse.OpacityProperty, pulseAnim);
            }
            statusPanel.Children.Add(statusDot);
            
            var statusText = new TextBlock
            {
                Text = isCharging ? "Charging" : "On Battery",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(107, 138, 153)),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusPanel.Children.Add(statusText);
            infoStack.Children.Add(statusPanel);

            // Time remaining
            if (!string.IsNullOrEmpty(timeRemaining))
            {
                var timeText = new TextBlock
                {
                    Text = timeRemaining,
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 184, 212)),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                infoStack.Children.Add(timeText);
            }

            Grid.SetColumn(infoStack, 1);
            mainGrid.Children.Add(infoStack);

            card.Child = mainGrid;

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(224, 64, 251),
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
                card.RenderTransform = new ScaleTransform(1.02, 1.02);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
            };
            card.MouseLeave += (s, e) =>
            {
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(224, 64, 251),
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.25
                };
                card.RenderTransform = null;
            };

            return card;
        }

        /// <summary>
        /// Creates a completely unique Aurora-style device card with circular battery ring
        /// </summary>
        private Border CreateAuroraDeviceCard(WindowsBluetoothDevice device, string displayName)
        {
            var card = new Border
            {
                Tag = device.Id,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 14),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16),
            };
            
            // Glass background
            var glassBg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(45, 26, 35, 50), 0));
            glassBg.GradientStops.Add(new GradientStop(Color.FromArgb(30, 17, 24, 32), 1));
            card.Background = glassBg;
            
            // Animated gradient border
            var borderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 229, 160), 0));
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 0, 184, 212), 0.5));
            borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 224, 64, 251), 1));
            card.BorderBrush = borderBrush;
            card.BorderThickness = new Thickness(1.5);
            
            // Glow effect
            card.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 229, 160),
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.25
            };

            // Main horizontal layout: [Circular Battery Ring] [Device Info]
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Ring
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info

            // === LEFT: Circular Battery Ring ===
            var ringContainer = new Grid
            {
                Width = 60,
                Height = 60,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Background ring (dark)
            var bgRing = new Ellipse
            {
                Width = 56,
                Height = 56,
                StrokeThickness = 5,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Fill = Brushes.Transparent
            };
            ringContainer.Children.Add(bgRing);

            // Progress ring (colored based on battery)
            int batteryValue = device.BatteryLevel ?? 0;
            var progressRing = CreateBatteryArc(batteryValue, device.IsConnected);
            ringContainer.Children.Add(progressRing);

            // Battery percentage text in center
            var percentText = new TextBlock
            {
                Text = device.BatteryLevel.HasValue ? $"{device.BatteryLevel}%" : "?",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 248)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ringContainer.Children.Add(percentText);

            Grid.SetColumn(ringContainer, 0);
            mainGrid.Children.Add(ringContainer);

            // === RIGHT: Device Info ===
            var infoStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            // Device name with gradient
            var nameText = new TextBlock
            {
                Name = "DeviceName",
                Text = displayName,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = CreateAuroraGradientBrush(),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            };
            infoStack.Children.Add(nameText);

            // Status with icon
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var statusDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = device.IsConnected 
                    ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                    : new SolidColorBrush(Color.FromRgb(107, 138, 153)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            // Add pulsing animation for connected devices
            if (device.IsConnected)
            {
                var pulseAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.5,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                statusDot.BeginAnimation(Ellipse.OpacityProperty, pulseAnim);
            }
            statusPanel.Children.Add(statusDot);
            
            var statusText = new TextBlock
            {
                Name = "StatusText",
                Text = device.IsConnected ? "Connected" : "Offline",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(107, 138, 153)),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusPanel.Children.Add(statusText);
            infoStack.Children.Add(statusPanel);

            // Battery stats
            if (device.BatteryLevel.HasValue && device.IsConnected)
            {
                var statsText = new TextBlock
                {
                    Name = "StatsText",
                    Text = BatteryTracker.GetSummaryText(device.Id),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 64, 251)),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                infoStack.Children.Add(statsText);
            }

            Grid.SetColumn(infoStack, 1);
            mainGrid.Children.Add(infoStack);

            card.Child = mainGrid;

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 229, 160),
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
                card.RenderTransform = new ScaleTransform(1.02, 1.02);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
            };
            card.MouseLeave += (s, e) =>
            {
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 229, 160),
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.25
                };
                card.RenderTransform = null;
            };

            // Add context menu
            var contextMenu = new ContextMenu
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 35, 50)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 160)),
                BorderThickness = new Thickness(1)
            };
            var renameItem = new MenuItem
            {
                Header = "✏️ Rename Device",
                Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 248))
            };
            renameItem.Click += (s, e) => RenameDevice(device.Id, device.Name);
            contextMenu.Items.Add(renameItem);
            card.ContextMenu = contextMenu;

            return card;
        }

        /// <summary>
        /// Creates a circular arc path for the battery level indicator
        /// </summary>
        private Path CreateBatteryArc(int percentage, bool isConnected)
        {
            var path = new Path
            {
                StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 56,
                Height = 56
            };

            // Color based on battery level
            Color arcColor;
            if (!isConnected)
                arcColor = Color.FromRgb(107, 138, 153); // Grey for offline
            else if (percentage >= 50)
                arcColor = Color.FromRgb(0, 230, 118); // Green
            else if (percentage >= 20)
                arcColor = Color.FromRgb(255, 215, 64); // Amber
            else
                arcColor = Color.FromRgb(255, 82, 82); // Red

            // Create gradient for the arc
            var arcBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            arcBrush.GradientStops.Add(new GradientStop(arcColor, 0));
            arcBrush.GradientStops.Add(new GradientStop(Color.FromArgb(200, arcColor.R, arcColor.G, arcColor.B), 1));
            path.Stroke = arcBrush;

            // Add glow to the arc
            path.Effect = new DropShadowEffect
            {
                Color = arcColor,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.6
            };

            // Calculate arc
            double angle = (percentage / 100.0) * 360;
            double radius = 25.5;
            double centerX = 28;
            double centerY = 28;

            // Start from top (12 o'clock position)
            double startAngle = -90;
            double endAngle = startAngle + angle;

            double startX = centerX + radius * Math.Cos(startAngle * Math.PI / 180);
            double startY = centerY + radius * Math.Sin(startAngle * Math.PI / 180);
            double endX = centerX + radius * Math.Cos(endAngle * Math.PI / 180);
            double endY = centerY + radius * Math.Sin(endAngle * Math.PI / 180);

            bool isLargeArc = angle > 180;

            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(startX, startY) };
            
            var arcSegment = new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            };
            
            figure.Segments.Add(arcSegment);
            geometry.Figures.Add(figure);
            path.Data = geometry;

            return path;
        }

        private string GetBatteryIcon(int? batteryLevel)
        {
            if (!batteryLevel.HasValue) return "🔋";
            if (batteryLevel >= 80) return "🔋";
            if (batteryLevel >= 50) return "🔋";
            if (batteryLevel >= 20) return "[!!]";
            return "[!!]";
        }

        private string? GetBatteryTooltip(WindowsBluetoothDevice device)
        {
            if (!device.IsConnected && device.BatteryLevel.HasValue)
                return "Last known battery level (device disconnected)";
            if (device.IsBatteryFallback)
                return "Battery reading may be less accurate (from HFP AG)";
            return null;
        }

        private SolidColorBrush GetBatteryColor(int? batteryLevel, bool isConnected = true)
        {
            if (!batteryLevel.HasValue) 
                return (SolidColorBrush)FindResource("SubTextBrush");
            
            // Dim the color for disconnected devices (cached battery)
            if (!isConnected)
                return (SolidColorBrush)FindResource("SubTextBrush");
            
            // Retro neon colors
            if (batteryLevel >= 50)
                return new SolidColorBrush(Color.FromRgb(0, 255, 136)); // Neon green
            if (batteryLevel >= 20)
                return new SolidColorBrush(Color.FromRgb(255, 184, 0)); // Neon amber
            
            return new SolidColorBrush(Color.FromRgb(255, 51, 102)); // Neon red/pink
        }

        private void RenameDevice(string deviceId, string originalName)
        {
            string currentName = DeviceSettings.GetDisplayName(deviceId, originalName);
            string? newName = Interaction.InputBox(
                "Enter a custom name for this device:",
                "Rename Device",
                currentName);
            
            if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
            {
                DeviceSettings.SetCustomName(deviceId, newName);
                
                // Update the card immediately
                if (_deviceCards.TryGetValue(deviceId, out var card) && _currentDevices.TryGetValue(deviceId, out var device))
                {
                    UpdateDeviceCard(card, device);
                }
            }
        }

        private void ResetDeviceName(string deviceId)
        {
            DeviceSettings.ClearCustomName(deviceId);
            
            // Update the card immediately
            if (_deviceCards.TryGetValue(deviceId, out var card) && _currentDevices.TryGetValue(deviceId, out var device))
            {
                UpdateDeviceCard(card, device);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stop all timers to prevent memory leaks
            refreshTimer?.Stop();
            refreshTimer = null;
            
            networkSpeedTimer?.Stop();
            networkSpeedTimer = null;
            
            marqueeTimer?.Stop();
            marqueeTimer = null;
            
            progressAnimationTimer?.Stop();
            progressAnimationTimer = null;
            
            // Dispose network helper
            _networkSpeedHelper?.Dispose();
            _networkSpeedHelper = null;
            
            // Clear collections to free memory
            _currentDevices.Clear();
            _deviceCards.Clear();
            _championQuotes.Clear();
            
            // Dispose tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            base.OnClosed(e);
        }

        #region System Tray Icon

        /// <summary>
        /// Initializes the system tray icon with context menu.
        /// </summary>
        private void InitializeTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon();
            _trayIcon.Text = "Froggy - Bluetooth Widget";
            
            // Create icon from embedded resource or use a default
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "froggy.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _trayIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    // Use default application icon
                    _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                }
            }
            catch
            {
                // Fallback to default
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            
            // Context menu
            var contextMenu = new WinForms.ContextMenuStrip();
            
            var showItem = new WinForms.ToolStripMenuItem("Show Froggy");
            showItem.Click += (s, e) => ShowFromTray();
            contextMenu.Items.Add(showItem);
            
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            
            var exitItem = new WinForms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            contextMenu.Items.Add(exitItem);
            
            _trayIcon.ContextMenuStrip = contextMenu;
            
            // Double-click to show
            _trayIcon.DoubleClick += (s, e) => ShowFromTray();
            
            _trayIcon.Visible = true;
        }

        /// <summary>
        /// Shows the window from the system tray.
        /// </summary>
        private void ShowFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        /// <summary>
        /// Override to minimize to tray instead of closing.
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
                _trayIcon?.ShowBalloonTip(1000, "Froggy", "Minimized to tray. Double-click to restore.", WinForms.ToolTipIcon.Info);
            }
            base.OnStateChanged(e);
        }

        #endregion
    }
}

