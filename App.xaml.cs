using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DotaPingMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // Устанавливаем стабильную частоту кадров анимаций WPF на 60 FPS для максимальной плавности UI
        try
        {
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline),
                new FrameworkPropertyMetadata(60));
        }
        catch { }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static string GetSafeCrashLogPath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "DotaPingMonitor");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "crash.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "dotapingmonitor_crash.log");
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string logPath = GetSafeCrashLogPath();
            File.WriteAllText(logPath, $"[{DateTime.Now}] UI Exception:\n{e.Exception}");
        }
        catch { }

        try
        {
            MessageBox.Show(
                e.Exception.ToString(),
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }

        e.Handled = true;
    }

    private void OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        try
        {
            string logPath = GetSafeCrashLogPath();
            File.WriteAllText(logPath, $"[{DateTime.Now}] Unhandled Exception:\n{e.ExceptionObject}");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var db = new Data.DatabaseService();
            db.ClearAll();
        }
        catch
        {
            // Ignore during process shutdown
        }

        base.OnExit(e);
    }
}
