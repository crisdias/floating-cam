using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FloatingCam;

/// <summary>
/// Translucent orange dot that follows the mouse cursor (a screen pointer, like
/// Canva's). Always-on-top, click-through and non-activating, so it never steals
/// focus or blocks the mouse from the app underneath. Shown while a global hotkey
/// is held; OBS's screen capture picks it up like any other on-screen window.
/// </summary>
public partial class PointerOverlay : System.Windows.Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;   // click-through
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x8000000;
    private const int WS_EX_TOOLWINDOW = 0x80;     // hide from Alt+Tab

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public PointerOverlay()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>Centers the dot at the given cursor position (physical screen pixels).</summary>
    public void MoveTo(int physicalX, int physicalY)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        // Physical pixels -> device-independent units (DIP).
        var m = source.CompositionTarget.TransformFromDevice;
        double x = physicalX * m.M11;
        double y = physicalY * m.M22;

        Left = x - Width / 2;
        Top = y - Height / 2;
    }
}
