using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClassicAvatar.Services;

namespace ClassicAvatar
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static string LogPath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClassicAvatar");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "crash.log");
            }
        }

        public App()
        {
            this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log(e.Exception);
                e.SetObserved();
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Restores the last C2 colorway / language; Light + zh-CN on a
            // first run. The shell never depends on VControls Light/Dark.
            C2Appearance.Initialize();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception);
            e.Handled = true;

            if (this.MainWindow is MainWindow window && window.IsLoaded)
            {
                window.ShowFatalError(e.Exception);
            }
        }

        private static void Log(Exception? exception)
        {
            if (exception == null) return;

            try
            {
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception)
            {
            }
        }
    }
}
