using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatingCam;

/// <summary>
/// Framing adjustment window. Shows the full webcam signal with a draggable guide
/// rectangle indicating the region the floating box will display. The guide uses the
/// exact same crop rectangle (MainWindow.CropRect) the main window applies via
/// ImageBrush.Viewbox — so the preview matches the result 1:1. Zoom resizes; drag
/// repositions.
/// </summary>
public partial class FramingWindow : System.Windows.Window
{
    private readonly MainWindow _main;
    private bool _dragging;
    private System.Windows.Point _lastPos;
    private bool _syncing;

    private double _pw = 380;
    private double _ph = 380 * 9.0 / 16.0;

    public FramingWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();

        UpdatePreviewSize();
        PreviewImage.Source = _main.VideoSource;
        _main.VideoSourceChanged += OnVideoSourceChanged;

        ZoomSlider.Value = _main.FramingZoom;
        ZoomSlider.ValueChanged += (_, _) => OnZoomChanged();

        ResetButton.Click += (_, _) => { _main.SetFraming(1.0, 0.5, 0.5); SyncFromMain(); };
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => _main.VideoSourceChanged -= OnVideoSourceChanged;

        Loaded += (_, _) => SyncFromMain();
    }

    private void OnVideoSourceChanged(object? sender, EventArgs e)
    {
        PreviewImage.Source = _main.VideoSource;
        UpdatePreviewSize();
        SyncFromMain();
    }

    private void UpdatePreviewSize()
    {
        const double previewWidth = 380;
        double aspect = _main.CameraAspect;
        if (aspect <= 0) aspect = 16.0 / 9.0;
        _pw = previewWidth;
        _ph = previewWidth / aspect;

        PreviewCanvas.Width = _pw;
        PreviewCanvas.Height = _ph;
        PreviewImage.Width = _pw;
        PreviewImage.Height = _ph;
        PreviewImage.Stretch = Stretch.UniformToFill;
    }

    private void OnZoomChanged()
    {
        if (_syncing) return;
        _main.SetFraming(ZoomSlider.Value, _main.FramingCenterX, _main.FramingCenterY);
        SyncFromMain();
    }

    // Draws the guide and the mask from the main window's actual crop.
    private void SyncFromMain()
    {
        _syncing = true;
        ZoomSlider.Value = _main.FramingZoom;
        ZoomLabel.Text = $"{_main.FramingZoom:0.0}x";

        var crop = _main.CropRect(_main.FramingZoom, _main.FramingCenterX, _main.FramingCenterY);
        double rx = crop.X * _pw;
        double ry = crop.Y * _ph;
        double rw = crop.Width * _pw;
        double rh = crop.Height * _ph;

        Canvas.SetLeft(CropRect, rx);
        Canvas.SetTop(CropRect, ry);
        CropRect.Width = rw;
        CropRect.Height = rh;

        // Dark mask = preview area minus the guide rectangle (even-odd rule).
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, _pw, _ph)));
        group.Children.Add(new RectangleGeometry(new Rect(rx, ry, rw, rh)));
        DimPath.Data = group;

        _syncing = false;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _lastPos = e.GetPosition(PreviewCanvas);
        PreviewCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var pos = e.GetPosition(PreviewCanvas);
        double dx = pos.X - _lastPos.X;
        double dy = pos.Y - _lastPos.Y;
        _lastPos = pos;

        // Dragging moves the guide toward the cursor (1 preview px = 1/_pw of the camera).
        double newCx = _main.FramingCenterX + dx / _pw;
        double newCy = _main.FramingCenterY + dy / _ph;
        _main.SetFraming(_main.FramingZoom, newCx, newCy);
        SyncFromMain();
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        PreviewCanvas.ReleaseMouseCapture();
    }
}
