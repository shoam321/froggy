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
        private DispatcherTimer? progressAnimationTimer;
        private DispatcherTimer? networkSpeedTimer;
        private DispatcherTimer? marqueeTimer;
        private NetworkSpeedHelper? _networkSpeedHelper;
        private double _marqueePosition = 0;
        private int _currentQuoteIndex = 0;
        
        // Leona and Kayle famous quotes from League of Legends
        private readonly string[] _championQuotes = new string[]
        {
            // Leona - The Radiant Dawn
            "* The dawn has arrived! *",
            "* I will protect you. *",
            "* The sun always rises. *",
            "* Stand and fight! *",
            "* Daylight! Find purchase! *",
            "* Next time, try to leave a dent. *",
            "* They will not escape the light. *",
            "* Chosen of the sun! *",
            // Kayle - The Righteous
            "// Only the divine may judge. //",
            "// In the name of justice! //",
            "// Come forth, you will find honor in death. //",
            "// Into the fray! //",
            "// An eye for an eye. //",
            "// Lead me to battle. //",
            "// Your judgment is at hand. //",
            "// I bring justice! //",
            "// Holy fervor! //"
        };
        private Dictionary<string, Border> _deviceCards = new();
        private Dictionary<string, WindowsBluetoothDevice> _currentDevices = new();
        private bool _isFirstScan = true;
        private bool _isLoading = false;
        private bool _hasFoundDevices = false; // Once true, never show loading animation again
        private Border? _laptopBatteryCard = null; // Card for laptop battery
        
        // System tray icon
        private WinForms.NotifyIcon? _trayIcon;
        
        // Text size settings
        private TextSizeLevel _currentTextSize = TextSizeLevel.Medium;
        
        // Screen edge snapping - increased for better "smash" detection
        private const int SnapDistance = 30; // Pixels from edge to trigger snap
        private const int CornerSnapDistance = 50; // Larger snap zone for corners

        public MainWindow()
        {
            // Apply saved theme before InitializeComponent
            ThemeManager.ApplyTheme();
            
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            
            // Subscribe to theme changes
            ThemeManager.ThemeChanged += OnThemeChanged;
            
            // Initialize system tray icon
            InitializeTrayIcon();
            
            // Apply theme-specific styling
            ApplyThemeSpecificStyles();
            
            // Load and apply saved text size
            LoadSavedTextSize();
            
            // Add screen edge snapping via Windows messages for better "smash" detection
            this.SourceInitialized += MainWindow_SourceInitialized;
            
            // Check for mascot GIF
            LoadMascot();
            
            // Start splash video
            PlaySplashVideo();
        }

        /// <summary>
        /// Loads the mascot GIF if it exists.
        /// </summary>
        private void LoadMascot()
        {
            try
            {
                var mascotPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mascot.gif");
                if (System.IO.File.Exists(mascotPath))
                {
                    var uri = new Uri(mascotPath);
                    WpfAnimatedGif.ImageBehavior.SetAnimatedSource(MascotImage, new System.Windows.Media.Imaging.BitmapImage(uri));
                    MascotImage.Visibility = Visibility.Visible;
                    Console.WriteLine("Mascot loaded!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"No mascot: {ex.Message}");
            }
        }

        #region Splash Video

        /// <summary>
        /// Plays the splash video on app startup.
        /// </summary>
        private void PlaySplashVideo()
        {
            try
            {
                // Get the video path from the output directory
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var videoPath = System.IO.Path.Combine(exeDir, "Assets", "animation.mp4");
                
                if (System.IO.File.Exists(videoPath))
                {
                    SplashVideo.Source = new Uri(videoPath, UriKind.Absolute);
                    SplashVideo.Play();
                    VideoSplashOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    // No video file found, skip splash
                    Console.WriteLine($"Splash video not found at: {videoPath}");
                    HideSplashVideo();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing splash video: {ex.Message}");
                HideSplashVideo();
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

        #endregion

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
            LoadingText.Text = "Finding devices...";
            
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
            // Clear and rebuild all cards with new theme
            foreach (var kvp in _currentDevices.ToList())
            {
                if (_deviceCards.TryGetValue(kvp.Key, out var card))
                {
                    UpdateDeviceCard(card, kvp.Value);
                }
            }
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
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
                
                // Start marquee animation immediately
                InitializeMarquee();
                
                await ScanForDevices();
                
                // Set up auto-refresh every 30 seconds for Bluetooth
                refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                refreshTimer.Tick += async (s, args) => await ScanForDevices();
                refreshTimer.Start();
                Console.WriteLine("Auto-refresh timer started");

                // Initialize network speed monitoring
                InitializeNetworkSpeedMonitor();
            }
            catch (Exception ex)
            {
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
                    Interval = TimeSpan.FromSeconds(2)
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
            
            // Set initial quote
            MarqueeText.Text = _championQuotes[_currentQuoteIndex];
            
            // Create timer for smooth animation (~60fps)
            marqueeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
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
                // Move to next quote
                _currentQuoteIndex = (_currentQuoteIndex + 1) % _championQuotes.Length;
                MarqueeText.Text = _championQuotes[_currentQuoteIndex];
                
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
                    LoadingText.Text = "No Bluetooth devices found\n\nMake sure devices are:\n- Paired in Windows Settings\n- Turned on and connected";
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
            
            var nameText = new TextBlock
            {
                Text = "Laptop",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryBrush")
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
                // Update status
                if (grid.Children[0] is StackPanel infoPanel)
                {
                    if (infoPanel.Children[1] is TextBlock statusText)
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

                // Update battery display
                if (grid.Children[1] is StackPanel batteryPanel)
                {
                    if (batteryPanel.Children[0] is TextBlock icon)
                        icon.Text = isCharging ? "🔌" : "💻";
                    if (batteryPanel.Children[1] is TextBlock percent)
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

            // Theme-aware context menu colors based on current theme
            var theme = ThemeManager.CurrentTheme;
            var (menuBg, menuBorder, menuText) = theme switch
            {
                AppTheme.Pixel => (Color.FromRgb(37, 37, 37), Color.FromRgb(0, 170, 0), Color.FromRgb(0, 255, 0)),
                AppTheme.NeonDrift => (Color.FromRgb(37, 24, 50), Color.FromRgb(189, 147, 249), Color.FromRgb(232, 224, 240)),
                AppTheme.Moss => (Color.FromRgb(37, 45, 33), Color.FromRgb(74, 93, 62), Color.FromRgb(212, 207, 196)),
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

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { Name = "InfoStack" };

            // Theme font
            var themeFont = new FontFamily("Consolas");

            // Theme-aware colors for device cards
            Brush nameColor = theme switch
            {
                AppTheme.Pixel => new SolidColorBrush(Color.FromRgb(0, 255, 0)),          // Bright green
                AppTheme.NeonDrift => new SolidColorBrush(Color.FromRgb(0, 255, 239)),    // Cyan glow
                AppTheme.Moss => new SolidColorBrush(Color.FromRgb(212, 207, 196)),       // Warm parchment
                _ => new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(0, 255, 255), 0) // Retro gradient
            };
            
            Color onlineColor = theme switch
            {
                AppTheme.Pixel => Color.FromRgb(0, 255, 136),       // Neon green
                AppTheme.NeonDrift => Color.FromRgb(80, 250, 123),  // Neon green
                AppTheme.Moss => Color.FromRgb(152, 195, 121),      // Bright moss
                _ => Color.FromRgb(0, 255, 136)                      // Retro neon
            };
            
            Color statsColor = theme switch
            {
                AppTheme.Pixel => Color.FromRgb(255, 170, 0),       // Amber
                AppTheme.NeonDrift => Color.FromRgb(255, 121, 198), // Hot pink
                AppTheme.Moss => Color.FromRgb(209, 154, 102),      // Autumn orange
                _ => Color.FromRgb(255, 0, 255)                      // Magenta
            };

            var nameText = new TextBlock
            {
                Name = "DeviceName",
                Text = displayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                FontFamily = themeFont,
                Foreground = nameColor
            };
            infoStack.Children.Add(nameText);

            var statusText = new TextBlock
            {
                Name = "StatusText",
                Text = device.IsConnected ? "● ONLINE" : "○ OFFLINE",
                FontSize = 10,
                FontFamily = themeFont,
                Foreground = device.IsConnected 
                    ? new SolidColorBrush(onlineColor)
                    : (SolidColorBrush)FindResource("SubTextBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            infoStack.Children.Add(statusText);

            // Battery stats (drain rate & time remaining) - only for connected devices
            var statsLabel = new TextBlock
            {
                Name = "StatsText",
                Text = device.BatteryLevel.HasValue && device.IsConnected ? BatteryTracker.GetSummaryText(device.Id) : "",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = themeFont,
                Foreground = new SolidColorBrush(statsColor),
                Margin = new Thickness(0, 6, 0, 0)
            };
            infoStack.Children.Add(statsLabel);

            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            // Battery indicator
            var batteryStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var batteryRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var batteryIcon = new TextBlock
            {
                Name = "BatteryIcon",
                Text = GetBatteryIcon(device.BatteryLevel),
                FontSize = 24,
                Margin = new Thickness(0, 0, 8, 0)
            };
            batteryRow.Children.Add(batteryIcon);

            var batteryText = new TextBlock
            {
                Name = "BatteryText",
                Text = GetBatteryDisplayText(device),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = GetBatteryColor(device.BatteryLevel, device.IsConnected),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = GetBatteryTooltip(device)
            };
            batteryRow.Children.Add(batteryText);

            batteryStack.Children.Add(batteryRow);

            Grid.SetColumn(batteryStack, 1);
            grid.Children.Add(batteryStack);

            deviceBorder.Child = grid;
            return deviceBorder;
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

            // Find and update text elements
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
            refreshTimer?.Stop();
            
            // Dispose tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            
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

