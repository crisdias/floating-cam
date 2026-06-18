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
    }

    private Settings _settings = new();
    private bool _restoring;

    public MainWindow()
    {
        InitializeComponent();

        MenuClose.Click += (_, _) => Close();
        MenuMirror.Click += (_, _) => { _mirror = MenuMirror.IsChecked; SaveSettings(); };
        MenuRound.Click += (_, _) => SetShape(MenuRound.IsChecked ? ShapeMode.Rounded : ShapeMode.Normal);
        MenuCircle.Click += (_, _) => SetShape(MenuCircle.IsChecked ? ShapeMode.Circle : ShapeMode.Normal);

        // O grip de redimensionar só aparece quando a janela tem foco.
        Activated += (_, _) => ResizeMode = ResizeMode.CanResizeWithGrip;
        Deactivated += (_, _) => ResizeMode = ResizeMode.NoResize;

        SizeChanged += (_, _) => UpdateClip();
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

    // Garante que o canto superior esquerdo cai dentro da área de trabalho virtual
    // (evita restaurar a janela fora da tela se um monitor foi desconectado).
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

        // Restaura espelho e formato salvos.
        _restoring = true;
        _mirror = _settings.Mirror;
        MenuMirror.IsChecked = _mirror;
        SetShape((ShapeMode)Math.Clamp(_settings.Shape, 0, 2));
        _restoring = false;

        UpdateClip();

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

        // Abre a câmera salva, se ainda existir; senão a primeira disponível.
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

        // Item para reescanear caso uma câmera seja conectada depois.
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

        // Abrir a câmera pode bloquear ~1s; faz fora da UI.
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

                App.Log($"Webcam {index} aberta OK.");
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

    private void StartCaptureThread()
    {
        _running = true;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "CameraCapture" };
        _captureThread.Start();
    }

    private void CaptureLoop()
    {
        App.Log("CaptureLoop iniciado");
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

            var clone = frame.Clone();
            try
            {
                Dispatcher.Invoke(() => RenderFrame(clone));
            }
            catch (TaskCanceledException)
            {
                clone.Dispose();
                break; // janela fechando
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
                VideoImage.Source = _bitmap;
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
