using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace FloatingCam;

public partial class MainWindow : System.Windows.Window
{
    private VideoCapture? _capture;
    private readonly object _captureLock = new();
    private Thread? _captureThread;
    private volatile bool _running;
    private volatile bool _mirror;

    private WriteableBitmap? _bitmap;
    private bool _firstFrameLogged;
    private int _currentIndex = -1;

    private enum ShapeMode { Normal, Rounded, Circle }
    private ShapeMode _shape = ShapeMode.Normal;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FloatingCam", "settings.json");

    private sealed class Settings
    {
        public double Width { get; set; } = 320;
        public double Height { get; set; } = 240;
        public double? Left { get; set; }
        public double? Top { get; set; }
        public int CameraIndex { get; set; } = -1;
        public bool Mirror { get; set; }
        public int Shape { get; set; }
        public double Zoom { get; set; } = 1.0;
        public double CenterX { get; set; } = 0.5;
        public double CenterY { get; set; } = 0.5;
    }

    private Settings _settings = new();
    private bool _restoring;

    // Framing: zoom (>=1) and crop center in normalized camera coordinates.
    private double _zoom = 1.0;
    private double _centerX = 0.5;
    private double _centerY = 0.5;
    private readonly ImageBrush _videoBrush = new() { Stretch = Stretch.Fill };
    private FramingWindow? _framingWindow;

    /// <summary>Live webcam bitmap (updated in-place on every frame).</summary>
    public WriteableBitmap? VideoSource => _bitmap;
    /// <summary>Raised when the bitmap is recreated (resolution change).</summary>
    public event EventHandler? VideoSourceChanged;

    public double FramingZoom => _zoom;
    public double FramingCenterX => _centerX;
    public double FramingCenterY => _centerY;

    /// <summary>Width/height ratio of the captured webcam frame (for the framing preview).</summary>
    public double CameraAspect =>
        _bitmap is { PixelHeight: > 0 } ? (double)_bitmap.PixelWidth / _bitmap.PixelHeight : 16.0 / 9.0;

    public double CameraWidthPx => _bitmap?.PixelWidth ?? 0;
    public double CameraHeightPx => _bitmap?.PixelHeight ?? 0;

    /// <summary>
    /// Fraction (0..1) of the camera frame the window shows, per axis, for a given zoom.
    /// Accounts for the UniformToFill crop (when window aspect differs from the camera's).
    /// </summary>
    public (double w, double h) VisibleFraction(double zoom)
    {
        double ww = RootBorder.ActualWidth, wh = RootBorder.ActualHeight;
        double cw = CameraWidthPx, ch = CameraHeightPx;
        if (ww <= 0 || wh <= 0 || cw <= 0 || ch <= 0 || zoom <= 0)
            return (1.0 / Math.Max(zoom, 1.0), 1.0 / Math.Max(zoom, 1.0));

        double fill = Math.Max(ww / cw, wh / ch); // UniformToFill scale
        double wn = ww / (cw * fill * zoom);
        double hn = wh / (ch * fill * zoom);
        return (Math.Min(1.0, wn), Math.Min(1.0, hn));
    }

    public MainWindow()
    {
        InitializeComponent();
        VideoRect.Fill = _videoBrush;

        MenuClose.Click += (_, _) => Close();
        MenuFraming.Click += (_, _) => OpenFramingWindow();
        MenuMirror.Click += (_, _) => { _mirror = MenuMirror.IsChecked; SaveSettings(); };
        MenuRound.Click += (_, _) => SetShape(MenuRound.IsChecked ? ShapeMode.Rounded : ShapeMode.Normal);
        MenuCircle.Click += (_, _) => SetShape(MenuCircle.IsChecked ? ShapeMode.Circle : ShapeMode.Normal);

        // The resize grip only shows while the window has focus.
        Activated += (_, _) => ResizeMode = ResizeMode.CanResizeWithGrip;
        Deactivated += (_, _) => ResizeMode = ResizeMode.NoResize;

        SizeChanged += (_, _) => { UpdateClip(); ApplyFramingToMain(); };
        Loaded += MainWindow_Loaded;
        Closing += (_, _) => SaveSettings();
        Closed += (_, _) => StopCapture();

        LoadSettings();
        ApplyWindowGeometry();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch (Exception ex)
        {
            App.Log($"LoadSettings falhou: {ex.Message}");
            _settings = new Settings();
        }
    }

    private void ApplyWindowGeometry()
    {
        Width = _settings.Width;
        Height = _settings.Height;

        if (_settings.Left is double left && _settings.Top is double top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    // Ensures the top-left corner falls inside the virtual desktop
    // (avoids restoring the window off-screen if a monitor was disconnected).
    private static bool IsOnScreen(double left, double top)
    {
        double vx = SystemParameters.VirtualScreenLeft;
        double vy = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth;
        double vh = SystemParameters.VirtualScreenHeight;
        return left >= vx && top >= vy && left <= vx + vw - 50 && top <= vy + vh - 50;
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Width = Width;
            _settings.Height = Height;
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.CameraIndex = _currentIndex;
            _settings.Mirror = _mirror;
            _settings.Shape = (int)_shape;
            _settings.Zoom = _zoom;
            _settings.CenterX = _centerX;
            _settings.CenterY = _centerY;

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            App.Log($"SaveSettings falhou: {ex.Message}");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log($"Loaded: restaurando W={_settings.Width} H={_settings.Height} L={_settings.Left} T={_settings.Top} Shape={_settings.Shape} Mirror={_settings.Mirror} Cam={_settings.CameraIndex}");

        // Restore the saved mirror and shape.
        _restoring = true;
        _mirror = _settings.Mirror;
        MenuMirror.IsChecked = _mirror;
        SetShape((ShapeMode)Math.Clamp(_settings.Shape, 0, 2));
        _zoom = _settings.Zoom;
        _centerX = _settings.CenterX;
        _centerY = _settings.CenterY;
        _restoring = false;

        UpdateClip();
        ApplyFramingToMain();

        List<CameraEnumerator.Camera> cams;
        try
        {
            cams = CameraEnumerator.List();
            App.Log($"Enumeração: {cams.Count} câmera(s): {string.Join(", ", cams.Select(c => $"[{c.Index}] {c.Name}"))}");
        }
        catch (Exception ex)
        {
            App.Log($"Enumeração FALHOU: {ex}");
            cams = new List<CameraEnumerator.Camera>();
        }

        BuildCameraMenu();
        StartCaptureThread();

        // Open the saved camera if it still exists; otherwise the first available one.
        if (cams.Count > 0)
        {
            bool savedExists = cams.Any(c => c.Index == _settings.CameraIndex);
            SwitchCamera(savedExists ? _settings.CameraIndex : cams[0].Index);
        }
        else
        {
            App.Log("Nenhuma câmera para abrir por padrão.");
        }
    }

    private void BuildCameraMenu()
    {
        MenuCameras.Items.Clear();
        var cams = CameraEnumerator.List();

        if (cams.Count == 0)
        {
            MenuCameras.Items.Add(new MenuItem { Header = "(nenhuma webcam encontrada)", IsEnabled = false });
            return;
        }

        foreach (var cam in cams)
        {
            var item = new MenuItem
            {
                Header = cam.Name,
                IsCheckable = true,
                IsChecked = cam.Index == _currentIndex,
                Tag = cam.Index,
            };
            item.Click += (s, _) =>
            {
                int idx = (int)((MenuItem)s).Tag;
                SwitchCamera(idx);
            };
            MenuCameras.Items.Add(item);
        }

        // Lets the user rescan if a camera is connected later.
        MenuCameras.Items.Add(new Separator());
        var rescan = new MenuItem { Header = "Atualizar lista" };
        rescan.Click += (_, _) => BuildCameraMenu();
        MenuCameras.Items.Add(rescan);
    }

    private void SwitchCamera(int index)
    {
        _currentIndex = index;
        foreach (var obj in MenuCameras.Items)
            if (obj is MenuItem mi && mi.Tag is int tag)
                mi.IsChecked = tag == index;

        if (!_restoring) SaveSettings();

        // Opening the camera can block ~1s; do it off the UI thread.
        Task.Run(() =>
        {
            try
            {
                App.Log($"Abrindo webcam índice {index}...");
                var cap = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened())
                {
                    App.Log($"VideoCapture.IsOpened() = false para índice {index}");
                    cap.Dispose();
                    Dispatcher.Invoke(() =>
                        MessageBox.Show(this, $"Não foi possível abrir a webcam (índice {index}).",
                            "FloatingCam", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                // Try HD 16:9 with MJPG (compressed). USB3 cameras (e.g. Brio) accept it and
                // deliver 30fps. Cameras that only do raw format (YUY2) saturate USB at HD
                // and the FPS collapses — in that case we drop to a smaller 16:9 that runs smooth.
                cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                cap.Set(VideoCaptureProperties.FrameWidth, 1280);
                cap.Set(VideoCaptureProperties.FrameHeight, 720);
                cap.Set(VideoCaptureProperties.Fps, 30);

                bool isMjpg = DecodeFourCC(cap.Get(VideoCaptureProperties.FourCC)) == "MJPG";
                if (!isMjpg)
                {
                    cap.Set(VideoCaptureProperties.FrameWidth, 640);
                    cap.Set(VideoCaptureProperties.FrameHeight, 360);
                    cap.Set(VideoCaptureProperties.Fps, 30);
                }

                App.Log($"Webcam {index} aberta OK. Resolução: {cap.Get(VideoCaptureProperties.FrameWidth)}x{cap.Get(VideoCaptureProperties.FrameHeight)} " +
                        $"fourcc={DecodeFourCC(cap.Get(VideoCaptureProperties.FourCC))} (mjpg={isMjpg})");
                lock (_captureLock)
                {
                    _capture?.Dispose();
                    _capture = cap;
                }
            }
            catch (Exception ex)
            {
                App.Log($"Falha ao abrir webcam {index}: {ex}");
            }
        });
    }

    private static string DecodeFourCC(double v)
    {
        int code = (int)v;
        if (code == 0) return "(0)";
        return new string(new[]
        {
            (char)(code & 0xFF),
            (char)((code >> 8) & 0xFF),
            (char)((code >> 16) & 0xFF),
            (char)((code >> 24) & 0xFF),
        });
    }

    private void StartCaptureThread()
    {
        _running = true;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "CameraCapture" };
        _captureThread.Start();
    }

    private void CaptureLoop()
    {
        App.Log("CaptureLoop iniciado");
        var fpsWatch = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        bool fpsLogged = false;
        while (_running)
        {
          try
          {
            VideoCapture? cap;
            lock (_captureLock) { cap = _capture; }

            if (cap is null)
            {
                Thread.Sleep(50);
                continue;
            }

            using var frame = new Mat();
            bool ok;
            lock (_captureLock) { ok = _capture is not null && _capture.Read(frame); }

            if (!ok || frame.Empty())
            {
                Thread.Sleep(5);
                continue;
            }

            if (_mirror)
                Cv2.Flip(frame, frame, FlipMode.Y);

            // Measure the real FPS once (~3s after starting) for diagnostics.
            if (!fpsLogged)
            {
                frames++;
                if (fpsWatch.ElapsedMilliseconds >= 3000)
                {
                    App.Log($"FPS medido: {frames * 1000.0 / fpsWatch.ElapsedMilliseconds:0.0}");
                    fpsLogged = true;
                }
            }

            var clone = frame.Clone();
            try
            {
                Dispatcher.Invoke(() => RenderFrame(clone));
            }
            catch (TaskCanceledException)
            {
                clone.Dispose();
                break; // window closing
            }
          }
          catch (Exception ex)
          {
              App.Log($"Erro no CaptureLoop: {ex}");
              Thread.Sleep(50);
          }
        }
        App.Log("CaptureLoop encerrado");
    }

    private void RenderFrame(Mat frame)
    {
        try
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null);
                _videoBrush.ImageSource = _bitmap;
                // The resolution is only known when the 1st frame arrives; recompute the
                // crop now (otherwise the aspect is wrong and the image distorts).
                ApplyFramingToMain();
                VideoSourceChanged?.Invoke(this, EventArgs.Empty);
            }
            WriteableBitmapConverter.ToWriteableBitmap(frame, _bitmap);
            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                App.Log($"Primeiro frame renderizado: {frame.Width}x{frame.Height}");
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    private void SetShape(ShapeMode mode)
    {
        _shape = mode;
        MenuRound.IsChecked = mode == ShapeMode.Rounded;
        MenuCircle.IsChecked = mode == ShapeMode.Circle;
        UpdateClip();
        if (!_restoring) SaveSettings();
    }

    private void UpdateClip()
    {
        double w = RootBorder.ActualWidth;
        double h = RootBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;

        ClipGeometry.Rect = new System.Windows.Rect(0, 0, w, h);
        switch (_shape)
        {
            case ShapeMode.Circle:
                ClipGeometry.RadiusX = w / 2;
                ClipGeometry.RadiusY = h / 2;
                break;
            case ShapeMode.Rounded:
                double r = Math.Min(24, Math.Min(w, h) / 2);
                ClipGeometry.RadiusX = r;
                ClipGeometry.RadiusY = r;
                break;
            default:
                ClipGeometry.RadiusX = 0;
                ClipGeometry.RadiusY = 0;
                break;
        }
    }

    /// <summary>
    /// Rectangle (normalized, camera coords) the window displays, for a given zoom and center.
    /// The center is clamped so the crop stays inside the frame. Same source of truth used by
    /// the window (ImageBrush.Viewbox) and by the framing selector.
    /// </summary>
    public System.Windows.Rect CropRect(double zoom, double cx, double cy)
    {
        var (wn, hn) = VisibleFraction(zoom);
        cx = Math.Clamp(cx, wn / 2, 1 - wn / 2);
        cy = Math.Clamp(cy, hn / 2, 1 - hn / 2);
        return new System.Windows.Rect(cx - wn / 2, cy - hn / 2, wn, hn);
    }

    private void ApplyFramingToMain()
    {
        var crop = CropRect(_zoom, _centerX, _centerY);
        _videoBrush.Viewbox = crop;
        // Keep the fields holding the already-clamped center.
        _centerX = crop.X + crop.Width / 2;
        _centerY = crop.Y + crop.Height / 2;
    }

    /// <summary>Sets the framing (zoom + normalized center), applies it and saves.</summary>
    public void SetFraming(double zoom, double centerX, double centerY)
    {
        _zoom = Math.Clamp(zoom, 1.0, 4.0);
        _centerX = centerX;
        _centerY = centerY;
        ApplyFramingToMain(); // clamps the center
        if (!_restoring) SaveSettings();
    }

    private void OpenFramingWindow()
    {
        if (_framingWindow is not null)
        {
            _framingWindow.Activate();
            return;
        }
        _framingWindow = new FramingWindow(this) { Owner = this };
        _framingWindow.Closed += (_, _) => _framingWindow = null;
        _framingWindow.Show();
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void StopCapture()
    {
        _running = false;
        _captureThread?.Join(500);
        lock (_captureLock)
        {
            _capture?.Dispose();
            _capture = null;
        }
    }
}
