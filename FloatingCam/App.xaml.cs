using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FloatingCam;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "floatingcam.log");

    // Keeps the mutex alive while the app runs (single instance).
    private Mutex? _instanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, "FloatingCam.SingleInstance", out bool isNew);
        if (!isNew)
        {
            // An instance already exists: don't open a second one (avoids overwriting
            // the settings with default values when the duplicate is closed).
            Log("Segunda instância detectada — encerrando.");
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); }
        catch { /* logging is best-effort */ }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"DISPATCHER EXCEPTION: {e.Exception}");
        e.Handled = true; // don't crash the app on a UI error
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"DOMAIN EXCEPTION: {e.ExceptionObject}");
    }
}
