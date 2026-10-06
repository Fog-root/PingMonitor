using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.ViewModels;

public partial class MainViewModel
{
    private readonly SpeedtestService _speedtestService = new();
    private readonly SpeedtestHistoryService _speedtestHistory = new();

    private DispatcherTimer? _speedtestRenderTimer;
    private bool _isSpeedtestInitialized;

    // 60 FPS LERP & Smooth Animation Variables
    private double _currentSpeedMbps = 0.0;
    private double _targetSpeedMbps = 0.0;
    private double _currentGaugeAngle = -120.0;
    private double _targetGaugeAngle = -120.0;
    private double _currentMaxSpeedScale = 25.0;
    private double _targetMaxSpeedScale = 25.0;

    // Высокоплотный поток координат волны для непрерывного горизонтального движения
    private const int WaveBufferLength = 64;
    private readonly double[] _waveStream = new double[WaveBufferLength];
    private int _renderFrameCount = 0;

    [ObservableProperty]
    private bool _isSpeedtestView;

    [ObservableProperty]
    private bool _isSpeedtestRunning;

    [ObservableProperty]
    private string _speedtestButtonText = "СТАРТ";

    [ObservableProperty]
    private string _speedtestPhaseBadgeText = "ГОТОВ К ЗАМЕРУ";

    [ObservableProperty]
    private Brush _speedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#59D499")!;

    [ObservableProperty]
    private Brush _speedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1859D499")!;

    [ObservableProperty]
    private string _speedtestHeroSpeedText = "0.0";

    [ObservableProperty]
    private string _speedtestHeroUnitText = "Мбит/с";

    [ObservableProperty]
    private double _speedtestGaugeAngle = -120.0;

    [ObservableProperty]
    private double _speedtestProgressRatio = 0.0;

    [ObservableProperty]
    private string _speedtestDownloadText = "--";

    [ObservableProperty]
    private string _speedtestDownloadPeakText = "--";

    [ObservableProperty]
    private string _speedtestUploadText = "--";

    [ObservableProperty]
    private string _speedtestPingText = "-- ms";

    [ObservableProperty]
    private string _speedtestJitterText = "±-- ms";

    [ObservableProperty]
    private string _speedtestClientIp = "--";

    [ObservableProperty]
    private string _speedtestIspName = "--";

    [ObservableProperty]
    private string _speedtestServerLocation = "--";

    [ObservableProperty]
    private ObservableCollection<SpeedtestResult> _speedtestHistoryItems = new();

    [ObservableProperty]
    private bool _hasSpeedtestHistory;

    public Visibility SpeedtestHistoryListVisibility => HasSpeedtestHistory ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpeedtestNoHistoryVisibility => HasSpeedtestHistory ? Visibility.Collapsed : Visibility.Visible;

    partial void OnHasSpeedtestHistoryChanged(bool value)
    {
        OnPropertyChanged(nameof(SpeedtestHistoryListVisibility));
        OnPropertyChanged(nameof(SpeedtestNoHistoryVisibility));
    }

    [ObservableProperty]
    private Geometry? _speedtestWaveStrokeGeometry;

    [ObservableProperty]
    private Geometry? _speedtestWaveFillGeometry;

    [ObservableProperty]
    private Brush _speedtestWaveFillBrush = Brushes.Transparent;

    public Visibility SpeedtestVisibility => IsSpeedtestView ? Visibility.Visible : Visibility.Collapsed;

    public void EnsureSpeedtestInitialized()
    {
        if (_isSpeedtestInitialized) return;

        _speedtestService.ProgressChanged += OnSpeedtestProgress;
        _speedtestService.LiveSpeedSampled += OnSpeedtestLiveSpeed;
        _speedtestService.Completed += OnSpeedtestCompleted;
        _speedtestService.ErrorOccurred += OnSpeedtestError;

        var history = _speedtestHistory.Load();
        SpeedtestHistoryItems.Clear();
        foreach (var item in history)
        {
            SpeedtestHistoryItems.Add(item);
        }
        HasSpeedtestHistory = SpeedtestHistoryItems.Count > 0;

        if (history.Count > 0)
        {
            var latest = history[0];
            _lastReportDownloadSpeed = latest.DownloadSpeedMbps;
            _lastReportDownloadPeak = latest.DownloadPeakMbps;
            _lastReportUploadSpeed = latest.UploadSpeedMbps;
            _lastReportPing = latest.PingMs;
            _lastReportJitter = latest.JitterMs;

            SpeedtestDownloadText = LocalizationService.FormatSpeed(latest.DownloadSpeedMbps);
            SpeedtestDownloadPeakText = LocalizationService.FormatSpeed(latest.DownloadPeakMbps);
            SpeedtestUploadText = LocalizationService.FormatSpeed(latest.UploadSpeedMbps);
            SpeedtestPingText = $"{latest.PingMs:F0} ms";
            SpeedtestJitterText = LocalizationService.FormatJitter(latest.JitterMs);
            if (!string.IsNullOrEmpty(latest.ClientIp)) SpeedtestClientIp = latest.ClientIp;
            if (!string.IsNullOrEmpty(latest.IspName)) SpeedtestIspName = latest.IspName;
            if (!string.IsNullOrEmpty(latest.ServerLocation)) SpeedtestServerLocation = latest.ServerFullText;
        }

        UpdateSpeedtestLocalization();
        UpdateSpeedtestTheme(ThemeService.Current);

        // Инициализация 60 FPS цикла интерполяции (16 ms)
        _speedtestRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _speedtestRenderTimer.Tick += OnSpeedtestRenderTick;
        _speedtestRenderTimer.Start();

        _isSpeedtestInitialized = true;
    }

    public void UpdateSpeedtestTheme(ThemeDefinition theme)
    {
        try
        {
            var accentColor = (Color)ColorConverter.ConvertFromString(theme.AccentColor);
            var fillBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x45, accentColor.R, accentColor.G, accentColor.B), 0.0));
            fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x15, accentColor.R, accentColor.G, accentColor.B), 0.5));
            fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accentColor.R, accentColor.G, accentColor.B), 1.0));
            fillBrush.Freeze();
            SpeedtestWaveFillBrush = fillBrush;
        }
        catch
        {
            SpeedtestWaveFillBrush = (Brush)new BrushConverter().ConvertFromString("#2557C1FF")!;
        }
    }

    public void PauseSpeedtestRender()
    {
        _speedtestRenderTimer?.Stop();
    }

    public void ResumeSpeedtestRender()
    {
        EnsureSpeedtestInitialized();
        if (_speedtestRenderTimer != null && !_speedtestRenderTimer.IsEnabled)
        {
            _speedtestRenderTimer.Start();
        }
    }

    // Последние зафиксированные метрики для динамической перелокализации
    private double _lastReportDownloadSpeed = 0.0;
    private double _lastReportDownloadPeak = 0.0;
    private double _lastReportUploadSpeed = 0.0;
    private double _lastReportPing = -1.0;
    private double _lastReportJitter = -1.0;

    public void UpdateSpeedtestLocalization()
    {
        SpeedtestHeroUnitText = LocalizationService.IsRussian ? "Мбит/с" : "Mbps";
        SpeedtestHeroSpeedText = LocalizationService.FormatNumber(_currentSpeedMbps, "0.0");

        if (_lastReportDownloadSpeed > 0)
        {
            SpeedtestDownloadText = LocalizationService.FormatSpeed(_lastReportDownloadSpeed);
        }
        if (_lastReportDownloadPeak > 0)
        {
            SpeedtestDownloadPeakText = LocalizationService.FormatSpeed(_lastReportDownloadPeak);
        }
        if (_lastReportUploadSpeed > 0)
        {
            SpeedtestUploadText = LocalizationService.FormatSpeed(_lastReportUploadSpeed);
        }
        if (_lastReportJitter >= 0)
        {
            SpeedtestJitterText = LocalizationService.FormatJitter(_lastReportJitter);
        }

        if (!IsSpeedtestRunning)
        {
            SpeedtestButtonText = LocalizationService.Get("SpeedtestStart");
            SpeedtestPhaseBadgeText = LocalizationService.Get("SpeedtestIdle");
            SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#59D499")!;
            SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1859D499")!;
        }
        else
        {
            SpeedtestButtonText = LocalizationService.Get("SpeedtestCancel");
        }

        foreach (var item in SpeedtestHistoryItems)
        {
            item.RefreshLocalization();
        }
    }

    [RelayCommand]
    public void SwitchToSpeedtest()
    {
        IsTaskManagerView = false;
        IsSpeedtestView = true;
        IsNavMenuOpen = false;
        _taskManagerService.Stop();
        EnsureSpeedtestInitialized();
        ResumeSpeedtestRender();

        OnPropertyChanged(nameof(DotaPingVisibility));
        OnPropertyChanged(nameof(TaskManagerVisibility));
        OnPropertyChanged(nameof(SpeedtestVisibility));
        OnPropertyChanged(nameof(DotaAutoMatchButtonVisibility));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(HeaderLogoGlyph));
        OnPropertyChanged(nameof(HeaderLogoForeground));
        OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    public void ToggleSpeedtest()
    {
        EnsureSpeedtestInitialized();

        if (IsSpeedtestRunning)
        {
            _speedtestService.Cancel();
            IsSpeedtestRunning = false;
            _targetSpeedMbps = 0.0;
            _targetGaugeAngle = -120.0;
            SpeedtestProgressRatio = 0.0;

            SpeedtestButtonText = LocalizationService.Get("SpeedtestStart");
            SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ОТМЕНЕНО" : "CANCELLED";
            SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#FFC533")!;
            SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#18FFC533")!;
            return;
        }

        IsSpeedtestRunning = true;
        SpeedtestButtonText = LocalizationService.Get("SpeedtestCancel");
        SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ПОДКЛЮЧЕНИЕ К СЕРВЕРУ..." : "CONNECTING TO SERVER...";
        SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#57C1FF")!;
        SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1857C1FF")!;
        SpeedtestProgressRatio = 0.0;

        _targetSpeedMbps = 0.0;
        _currentSpeedMbps = 0.0;
        _targetGaugeAngle = -120.0;
        _currentGaugeAngle = -120.0;
        SpeedtestGaugeAngle = -120.0;
        SpeedtestHeroSpeedText = "0.0";
        SpeedtestDownloadText = "--";
        SpeedtestDownloadPeakText = "--";
        SpeedtestUploadText = "--";
        SpeedtestPingText = "-- ms";
        SpeedtestJitterText = "±-- ms";

        Array.Clear(_waveStream, 0, _waveStream.Length);
        _currentMaxSpeedScale = 25.0;
        _targetMaxSpeedScale = 25.0;

        _ = Task.Run(async () =>
        {
            try
            {
                await _speedtestService.StartTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnSpeedtestError(ex.Message);
            }
        });
    }

    [RelayCommand]
    public void ClearSpeedtestHistory()
    {
        _speedtestHistory.Clear();
        SpeedtestHistoryItems.Clear();
        HasSpeedtestHistory = false;
    }

    private void OnSpeedtestProgress(SpeedtestProgressReport report)
    {
        Dispatch(() =>
        {
            SpeedtestProgressRatio = report.TotalProgress;

            switch (report.Phase)
            {
                case SpeedtestPhase.Cancelled:
                    IsSpeedtestRunning = false;
                    _targetSpeedMbps = 0.0;
                    _targetGaugeAngle = -120.0;
                    SpeedtestProgressRatio = 0.0;
                    SpeedtestButtonText = LocalizationService.Get("SpeedtestStart");
                    SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ОТМЕНЕНО" : "CANCELLED";
                    SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#FFC533")!;
                    SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#18FFC533")!;
                    break;

                case SpeedtestPhase.Connecting:
                    SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ПОДКЛЮЧЕНИЕ К СЕРВЕРУ..." : "CONNECTING TO SERVER...";
                    SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#57C1FF")!;
                    SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1857C1FF")!;
                    break;

                case SpeedtestPhase.Latency:
                    SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ИЗМЕРЕНИЕ ЗАДЕРЖКИ (PING & JITTER)" : "MEASURING LATENCY & JITTER";
                    SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#57C1FF")!;
                    SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1857C1FF")!;
                    break;

                case SpeedtestPhase.Download:
                    SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ЗАМЕР СКОРОСТИ СКАЧИВАНИЯ (DOWNLOAD)" : "TESTING DOWNLOAD SPEED";
                    SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#59D499")!;
                    SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1859D499")!;
                    _lastReportDownloadSpeed = report.CurrentSpeedMbps;
                    _lastReportDownloadPeak = report.PeakSpeedMbps;
                    SpeedtestDownloadText = LocalizationService.FormatSpeed(report.CurrentSpeedMbps);
                    if (report.PeakSpeedMbps > 0)
                    {
                        SpeedtestDownloadPeakText = LocalizationService.FormatSpeed(report.PeakSpeedMbps);
                    }
                    break;

                case SpeedtestPhase.Upload:
                    SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ЗАМЕР СКОРОСТИ ОТДАЧИ (UPLOAD)" : "TESTING UPLOAD SPEED";
                    SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#57C1FF")!;
                    SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1857C1FF")!;
                    _lastReportUploadSpeed = report.CurrentSpeedMbps;
                    SpeedtestUploadText = LocalizationService.FormatSpeed(report.CurrentSpeedMbps);
                    break;
            }

            if (!string.IsNullOrEmpty(report.ClientIp)) SpeedtestClientIp = report.ClientIp;
            if (!string.IsNullOrEmpty(report.IspName)) SpeedtestIspName = report.IspName;
            if (!string.IsNullOrEmpty(report.ServerLocation))
            {
                SpeedtestServerLocation = !string.IsNullOrEmpty(report.ServerColo)
                    ? $"{report.ServerLocation} ({report.ServerColo})"
                    : report.ServerLocation;
            }

            if (report.PingMs > 0)
            {
                _lastReportPing = report.PingMs;
                SpeedtestPingText = $"{report.PingMs:F0} ms";
            }
            if (report.JitterMs >= 0)
            {
                _lastReportJitter = report.JitterMs;
                SpeedtestJitterText = LocalizationService.FormatJitter(report.JitterMs);
            }
        });
    }

    private void OnSpeedtestLiveSpeed(double speedMbps)
    {
        Dispatch(() =>
        {
            _targetSpeedMbps = Math.Max(0.0, speedMbps);
            _targetGaugeAngle = CalculateGaugeAngle(speedMbps);
        });
    }

    private void OnSpeedtestCompleted(SpeedtestResult result)
    {
        Dispatch(() =>
        {
            IsSpeedtestRunning = false;
            SpeedtestButtonText = LocalizationService.Get("SpeedtestStart");
            SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? "ТЕСТИРОВАНИЕ ЗАВЕРШЕНО" : "TEST COMPLETED";
            SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#59D499")!;
            SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#1859D499")!;
            SpeedtestProgressRatio = 1.0;

            _lastReportDownloadSpeed = result.DownloadSpeedMbps;
            _lastReportDownloadPeak = result.DownloadPeakMbps;
            _lastReportUploadSpeed = result.UploadSpeedMbps;
            _lastReportPing = result.PingMs;
            _lastReportJitter = result.JitterMs;

            SpeedtestDownloadText = LocalizationService.FormatSpeed(result.DownloadSpeedMbps);
            SpeedtestDownloadPeakText = LocalizationService.FormatSpeed(result.DownloadPeakMbps);
            SpeedtestUploadText = LocalizationService.FormatSpeed(result.UploadSpeedMbps);
            SpeedtestPingText = $"{result.PingMs:F0} ms";
            SpeedtestJitterText = LocalizationService.FormatJitter(result.JitterMs);

            _targetSpeedMbps = result.DownloadSpeedMbps;
            _targetGaugeAngle = CalculateGaugeAngle(result.DownloadSpeedMbps);

            if (!string.IsNullOrEmpty(result.ClientIp)) SpeedtestClientIp = result.ClientIp;
            if (!string.IsNullOrEmpty(result.IspName)) SpeedtestIspName = result.IspName;
            if (!string.IsNullOrEmpty(result.ServerLocation))
            {
                SpeedtestServerLocation = result.ServerFullText;
            }

            var updatedHistory = _speedtestHistory.AddResult(result);
            SpeedtestHistoryItems.Clear();
            foreach (var h in updatedHistory)
            {
                SpeedtestHistoryItems.Add(h);
            }
            HasSpeedtestHistory = SpeedtestHistoryItems.Count > 0;
        });
    }

    private void OnSpeedtestError(string message)
    {
        Dispatch(() =>
        {
            IsSpeedtestRunning = false;
            _targetSpeedMbps = 0.0;
            _targetGaugeAngle = -120.0;

            SpeedtestButtonText = LocalizationService.Get("SpeedtestStart");
            SpeedtestPhaseBadgeText = LocalizationService.IsRussian ? $"ОШИБКА: {message}" : $"ERROR: {message}";
            SpeedtestPhaseBadgeForeground = (Brush)new BrushConverter().ConvertFromString("#FF6161")!;
            SpeedtestPhaseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#18FF6161")!;
        });
    }

    /// <summary>
    /// Кадровый цикл обновления со скоростью 60 FPS (~16 ms).
    /// Выполняет экспоненциальную интерполяцию (LERP) чисел и стрелки,
    /// а также плавный горизонтальный сдвиг волны скорости с генерацией кубического сплайна Безье.
    /// </summary>
    private void OnSpeedtestRenderTick(object? sender, EventArgs e)
    {
        if (!IsSpeedtestView) return;

        // 1. Интерполяция текущего числа скорости (LERP)
        double speedDelta = _targetSpeedMbps - _currentSpeedMbps;
        if (Math.Abs(speedDelta) > 0.03)
        {
            _currentSpeedMbps += speedDelta * 0.16;
        }
        else
        {
            _currentSpeedMbps = _targetSpeedMbps;
        }
        SpeedtestHeroSpeedText = LocalizationService.FormatNumber(_currentSpeedMbps, "0.0");

        // 2. Интерполяция стрелки спидометра (LERP)
        double angleDelta = _targetGaugeAngle - _currentGaugeAngle;
        if (Math.Abs(angleDelta) > 0.05)
        {
            _currentGaugeAngle += angleDelta * 0.16;
        }
        else
        {
            _currentGaugeAngle = _targetGaugeAngle;
        }
        SpeedtestGaugeAngle = _currentGaugeAngle;

        // 3. Плавный горизонтальный сдвиг волны скорости
        _renderFrameCount++;
        if (IsSpeedtestRunning)
        {
            // Сдвигаем точки влево каждые 2 кадра (~33 ms = 30 сдвигов в секунду)
            if (_renderFrameCount % 2 == 0)
            {
                Array.Copy(_waveStream, 1, _waveStream, 0, WaveBufferLength - 1);
                _waveStream[WaveBufferLength - 1] = _currentSpeedMbps;
            }
        }
        else if (_currentSpeedMbps < 0.05 && _waveStream.Max() > 0.01)
        {
            // Плавное затухание после остановки теста
            if (_renderFrameCount % 2 == 0)
            {
                Array.Copy(_waveStream, 1, _waveStream, 0, WaveBufferLength - 1);
                _waveStream[WaveBufferLength - 1] = 0.0;
            }
        }

        // 4. Плавная интерполяция вертикального масштаба графика
        double peakInStream = _waveStream.Max();
        _targetMaxSpeedScale = Math.Max(20.0, Math.Max(peakInStream, _targetSpeedMbps) * 1.15);
        _currentMaxSpeedScale += (_targetMaxSpeedScale - _currentMaxSpeedScale) * 0.08;

        // 5. Построение сглаженного кубического сплайна Безье (Catmull-Rom)
        const double canvasWidth = 680.0;
        const double canvasHeight = 64.0;
        const double topMargin = 6.0;
        const double bottomMargin = 4.0;
        double usableHeight = canvasHeight - topMargin - bottomMargin;

        var points = new Point[WaveBufferLength];
        double stepX = canvasWidth / (WaveBufferLength - 1);

        for (int i = 0; i < WaveBufferLength; i++)
        {
            double x = i * stepX;
            double val = _waveStream[i];
            double normalized = Math.Clamp(val / _currentMaxSpeedScale, 0.0, 1.0);
            double y = (canvasHeight - bottomMargin) - (normalized * usableHeight);
            points[i] = new Point(x, y);
        }

        var (strokeGeo, fillGeo) = BuildCubicSplineGeometries(points, canvasWidth, canvasHeight);
        SpeedtestWaveStrokeGeometry = strokeGeo;
        SpeedtestWaveFillGeometry = fillGeo;
    }

    /// <summary>
    /// Преобразует дискретные координаты в непрерывные кривые Безье (Catmull-Rom to Cubic Bezier)
    /// с гарантией непрерывности первой производной C1 и защитой от выбросов.
    /// </summary>
    private static (Geometry stroke, Geometry fill) BuildCubicSplineGeometries(
        IReadOnlyList<Point> points,
        double width,
        double height)
    {
        if (points == null || points.Count < 2)
        {
            return (Geometry.Empty, Geometry.Empty);
        }

        var strokeFigure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        var fillFigure = new PathFigure
        {
            StartPoint = new Point(0, height),
            IsClosed = true,
            IsFilled = true
        };
        fillFigure.Segments.Add(new LineSegment(points[0], false));

        int n = points.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Point p0 = points[i];
            Point p1 = points[i + 1];
            Point pPrev = (i > 0) ? points[i - 1] : new Point(2 * p0.X - p1.X, 2 * p0.Y - p1.Y);
            Point pNext = (i + 2 < n) ? points[i + 2] : new Point(2 * p1.X - p0.X, 2 * p1.Y - p0.Y);

            // Преобразование Catmull-Rom сплайна в контрольные точки кубического Безье:
            // C1 = P0 + (P1 - P_prev) / 6
            // C2 = P1 - (P_next - P0) / 6
            double c1X = p0.X + (p1.X - pPrev.X) / 6.0;
            double c1Y = p0.Y + (p1.Y - pPrev.Y) / 6.0;

            double c2X = p1.X - (pNext.X - p0.X) / 6.0;
            double c2Y = p1.Y - (pNext.Y - p0.Y) / 6.0;

            // Ограничение контрольных точек рамками графика (no vertical overshoot)
            c1Y = Math.Clamp(c1Y, 4.0, height);
            c2Y = Math.Clamp(c2Y, 4.0, height);

            var bezier = new BezierSegment(new Point(c1X, c1Y), new Point(c2X, c2Y), p1, true);
            strokeFigure.Segments.Add(bezier);
            fillFigure.Segments.Add(bezier);
        }

        fillFigure.Segments.Add(new LineSegment(new Point(width, height), false));

        var strokeGeo = new PathGeometry();
        strokeGeo.Figures.Add(strokeFigure);
        strokeGeo.Freeze();

        var fillGeo = new PathGeometry();
        fillGeo.Figures.Add(fillFigure);
        fillGeo.Freeze();

        return (strokeGeo, fillGeo);
    }

    private static double CalculateGaugeAngle(double speedMbps)
    {
        if (speedMbps <= 0) return -120.0;
        if (speedMbps >= 1000) return 120.0;

        // Нелинейная калибровка радиальной шкалы (-120° .. +120°):
        // 0 -> -120°
        // 10 Mbps -> -75°
        // 50 Mbps -> -30°
        // 100 Mbps -> 0°
        // 250 Mbps -> 45°
        // 500 Mbps -> 85°
        // 1000 Mbps -> 120°
        double angle;
        if (speedMbps <= 10)
        {
            angle = -120.0 + (speedMbps / 10.0) * 45.0;
        }
        else if (speedMbps <= 50)
        {
            angle = -75.0 + ((speedMbps - 10.0) / 40.0) * 45.0;
        }
        else if (speedMbps <= 100)
        {
            angle = -30.0 + ((speedMbps - 50.0) / 50.0) * 30.0;
        }
        else if (speedMbps <= 250)
        {
            angle = 0.0 + ((speedMbps - 100.0) / 150.0) * 45.0;
        }
        else if (speedMbps <= 500)
        {
            angle = 45.0 + ((speedMbps - 250.0) / 250.0) * 40.0;
        }
        else
        {
            angle = 85.0 + ((speedMbps - 500.0) / 500.0) * 35.0;
        }

        return Math.Clamp(angle, -120.0, 120.0);
    }
}
