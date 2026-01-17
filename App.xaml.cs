using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AutoUpdaterDotNET;

namespace BluetoothWidget
{
    public partial class App : Application
    {
        // Prevent showing the same fatal error dialog repeatedly (one per process run)
        private static bool _hasShownFatalError = false;

        // Current version - UPDATE THIS when releasing new versions
        public const string CurrentVersion = "1.2.0";
        
        // URL to your update XML file (hosted on GitHub)
        private const string UpdateUrl = "https://raw.githubusercontent.com/shoam321/froggy/main/update.xml";
        
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            
            // Check for updates on startup (silently, won't block the app)
            try
            {
                AutoUpdater.InstalledVersion = new Version(CurrentVersion);
                AutoUpdater.ShowSkipButton = true;
                AutoUpdater.ShowRemindLaterButton = true;
                AutoUpdater.RunUpdateAsAdmin = false;
                AutoUpdater.Start(UpdateUrl);
            }
            catch
            {
                // Ignore update check failures - don't break the app
            }
            
            base.OnStartup(e);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogToFile("DispatcherUnhandledException", e.Exception);
            // Avoid flooding the user with repeated dialogs if many exceptions occur.
            if (!_hasShownFatalError)
            {
                _hasShownFatalError = true;
                MessageBox.Show($"Unexpected error: {e.Exception.Message}\n\nDetails were written to %LOCALAPPDATA%\\BluetoothWidget\\log.txt",
                    "BluetoothWidget Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            // Keep app alive but mark exception handled so WPF doesn't repeatedly route it.
            e.Handled = true;
        }

        private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            LogToFile("UnhandledException", e.ExceptionObject as Exception);
        }

        internal static void LogToFile(string category, Exception? ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BluetoothWidget");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "log.txt");
                File.AppendAllText(path,
                    $"{DateTime.Now:O} [{category}] {(ex?.ToString() ?? "(no exception)")}{Environment.NewLine}");
                try
                {
                    var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");
                    File.AppendAllText(localPath, $"{DateTime.Now:O} [{category}] {(ex?.ToString() ?? "(no exception)")}{Environment.NewLine}");
                }
                catch
                {
                    // ignore
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}

