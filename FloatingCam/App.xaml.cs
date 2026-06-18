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

    // Mantém o mutex vivo enquanto o app roda (instância única).
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
            // Já existe uma instância: não abre uma segunda (evita sobrescrever
            // as configurações com valores padrão ao fechar a duplicata).
            Log("Segunda instância detectada — encerrando.");
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); }
        catch { /* log é best-effort */ }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"DISPATCHER EXCEPTION: {e.Exception}");
        e.Handled = true; // não derruba o app por erro de UI
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"DOMAIN EXCEPTION: {e.ExceptionObject}");
    }
}
