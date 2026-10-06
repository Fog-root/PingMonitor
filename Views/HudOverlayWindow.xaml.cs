using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Views;

public partial class HudOverlayWindow : Window
{
    private readonly OverlayConfigService _configService;
    private OverlayConfig _config;
    private bool _isLocked;
    private bool _isHovered;
    private bool _isDragging;
    private readonly DispatcherTimer _hoverTimer;
    private readonly Action<bool> _onOptimizationModeChanged;

    private static readonly Dictionary<string, SolidColorBrush> _cachedBrushes = new();
    private static SolidColorBrush GetCachedBrush(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.Transparent;
        if (!_cachedBrushes.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            _cachedBrushes[hex] = brush;
        }
        return brush;
    }

    public event Action<bool>? StateChangedNotification;

    // =========================================================
    // Win32 API Constants & Functions
    // =========================================================

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public HudOverlayWindow(OverlayConfigService configService)
    {
        _configService = configService;
        _config = _configService.Load();
        InitializeComponent();

        if (_config.Scale < 0.65 || _config.Scale > 2.5)
        {
            _config.Scale = 1.0;
        }

        SetScale(_config.Scale);
        RestoreWindowPosition();
        ThemeService.ThemeChanged += OnThemeChanged;
        _onOptimizationModeChanged = _ =>
        {
            OnThemeChanged(ThemeService.Current);
            UpdateOptimizationMenuHeader();
        };
        ThemeService.OptimizationModeChanged += _onOptimizationModeChanged;
        LocalizationService.LanguageChanged += OnOverlayLanguageChanged;

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _hoverTimer.Tick += HoverTimer_Tick;

        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _hoverTimer.Start();
            }
            else
            {
                _hoverTimer.Stop();
                SetHovered(false, immediate: true);
            }
        };

        Loaded += (_, _) =>
        {
            double initialOp = _config.BaseOpacity > 0 ? _config.BaseOpacity : (_config.Opacity > 0 ? _config.Opacity : 0.92);
            _config.BaseOpacity = initialOp;
            _config.Opacity = initialOp;
            Opacity = initialOp;

            SetPreset(string.IsNullOrWhiteSpace(_config.LayoutPreset) ? "classic_bar" : _config.LayoutPreset);
            UpdateSparklineVisibility();
            UpdateFpsVisibility();
            UpdateCpuVisibility();
            UpdateGpuVisibility();
            UpdateRamVisibility();
            UpdateContextMenuHotkeys();
            UpdateContextMenuLocalization();
            OnThemeChanged(ThemeService.Current);
            UpdateOptimizationMenuHeader();
            if (IsVisible)
            {
                _hoverTimer.Start();
            }
        };
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            SetHovered(false);
            return;
        }

        if (!_isLocked)
        {
            return;
        }

        bool isContextMenuOpen = (PillBorder?.ContextMenu?.IsOpen == true) ||
                                 (MinimalBorder?.ContextMenu?.IsOpen == true) ||
                                 (VerticalStackBorder?.ContextMenu?.IsOpen == true) ||
                                 (NetGraphBorder?.ContextMenu?.IsOpen == true) ||
                                 (CyberHudBorder?.ContextMenu?.IsOpen == true);
        if (_isDragging || isContextMenuOpen)
        {
            SetHovered(false);
            return;
        }

        if (!GetCursorPos(out POINT pt))
        {
            SetHovered(false);
            return;
        }

        Point dipPoint;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            dipPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(pt.X, pt.Y));
        }
        else
        {
            dipPoint = new Point(pt.X, pt.Y);
        }

        FrameworkElement? activeBorder = GetActiveLayoutBorder();
        bool isInside = false;
        if (activeBorder != null && activeBorder.IsVisible)
        {
            try
            {
                Point cardTopLeft = activeBorder.PointToScreen(new Point(0, 0));
                Point cardTopLeftDip = source?.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice.Transform(cardTopLeft)
                    : cardTopLeft;
                Rect cardBounds = new Rect(cardTopLeftDip.X - 4, cardTopLeftDip.Y - 4, (activeBorder.ActualWidth * _config.Scale) + 8, (activeBorder.ActualHeight * _config.Scale) + 8);
                isInside = cardBounds.Contains(dipPoint);
            }
            catch
            {
                Rect bounds = new Rect(Left + 10, Top + 10, Math.Max(ActualWidth - 20, 1), Math.Max(ActualHeight - 20, 1));
                isInside = bounds.Contains(dipPoint);
            }
        }
        else
        {
            Rect bounds = new Rect(Left + 10, Top + 10, Math.Max(ActualWidth - 20, 1), Math.Max(ActualHeight - 20, 1));
            isInside = bounds.Contains(dipPoint);
        }
        SetHovered(isInside);
    }

    private void SetHovered(bool hovered, bool immediate = false)
    {
        if (_isHovered == hovered && !immediate) return;
        _isHovered = hovered;

        double baseOp = _config.BaseOpacity > 0 ? _config.BaseOpacity : (_config.Opacity > 0 ? _config.Opacity : 0.92);
        // При наведении курсора на оверлей (включая закрепленный режим) плавно снижаем прозрачность до 25-30% от базовой
        double targetOpacity = _isHovered 
            ? Math.Max(0.20, baseOp * 0.30) 
            : baseOp;

        if (immediate || ThemeService.IsOptimizedMode)
        {
            BeginAnimation(UIElement.OpacityProperty, null);
            Opacity = targetOpacity;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void RestoreWindowPosition()
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vWidth = SystemParameters.VirtualScreenWidth;
        double vHeight = SystemParameters.VirtualScreenHeight;

        // Проверяем, попадают ли сохраненные координаты в границы активных мониторов
        if (_config.Left >= vLeft && _config.Left < vLeft + vWidth - 50 &&
            _config.Top >= vTop && _config.Top < vTop + vHeight - 30)
        {
            Left = _config.Left;
            Top = _config.Top;
        }
        else
        {
            // По умолчанию: правый верхний угол с отступом
            Left = Math.Max(40, SystemParameters.PrimaryScreenWidth - 240);
            Top = 40;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Исключаем окно из Alt+Tab
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Применяем сохраненное состояние фиксации
        SetLocked(_config.IsLocked);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hoverTimer.Stop();
        ThemeService.ThemeChanged -= OnThemeChanged;
        ThemeService.OptimizationModeChanged -= _onOptimizationModeChanged;
        LocalizationService.LanguageChanged -= OnOverlayLanguageChanged;
        SaveConfig();
        base.OnClosed(e);
    }

    // =========================================================
    // Блокировка и сквозные клики (Click-Through)
    // =========================================================

    public bool IsLocked => _isLocked;

    public void ToggleLock()
    {
        SetLocked(!_isLocked);
    }

    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        _config.IsLocked = locked;
        _configService.Save(_config);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        string lockPathData = "M 3,5.5 V 4 C 3,2.2 4.3,1 6,1 C 7.7,1 9,2.2 9,4 M 10,5.5 H 2 C 1.4,5.5 1,5.9 1,6.5 V 11 C 1,11.6 1.4,12 2,12 H 10 C 10.6,12 11,11.6 11,11 V 6.5 C 11,5.9 10.6,5.5 10,5.5 Z";
        var lockStroke = _isLocked
            ? GetCachedBrush(ThemeService.Current.AccentGreen)
            : GetCachedBrush(ThemeService.Current.TextMuted);
        double strokeThickness = ThemeService.IsOptimizedMode ? 1.0 : 1.2;

        void ApplyLockIcon(System.Windows.Shapes.Path? icon)
        {
            if (icon == null) return;
            icon.Data = Geometry.Parse(lockPathData);
            icon.Stroke = lockStroke;
            icon.StrokeThickness = strokeThickness;
        }

        ApplyLockIcon(LockVectorIcon);
        ApplyLockIcon(VerticalLockIcon);
        ApplyLockIcon(NetGraphLockIcon);
        ApplyLockIcon(CyberLockIcon);

        Cursor activeCursor = _isLocked ? Cursors.Arrow : Cursors.SizeAll;
        if (PillBorder != null) PillBorder.Cursor = activeCursor;
        if (MinimalBorder != null) MinimalBorder.Cursor = activeCursor;
        if (VerticalStackBorder != null) VerticalStackBorder.Cursor = activeCursor;
        if (NetGraphBorder != null) NetGraphBorder.Cursor = activeCursor;
        if (CyberHudBorder != null) CyberHudBorder.Cursor = activeCursor;

        if (_isLocked)
        {
            // Включаем WS_EX_TRANSPARENT: клики мыши проходят насквозь в игру
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

            if (LockBtn != null) LockBtn.ToolTip = LocalizationService.IsRussian
                ? "Заблокировано (клики сквозь оверлей)\nНажмите Ctrl+Shift+L для разблокировки"
                : "Locked (click-through in game)\nPress Ctrl+Shift+L to unlock";
            if (ResizeDivider != null) ResizeDivider.Visibility = Visibility.Collapsed;
            if (ResizeGrip != null) ResizeGrip.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Выключаем WS_EX_TRANSPARENT: окно можно перетаскивать и нажимать кнопки
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

            if (LockBtn != null) LockBtn.ToolTip = LocalizationService.IsRussian
                ? "Закрепить оверлей (сквозные клики)\nХоткей: Ctrl+Shift+L"
                : "Lock overlay (click-through)\nHotkey: Ctrl+Shift+L";
            if (ResizeDivider != null) ResizeDivider.Visibility = Visibility.Visible;
            if (ResizeGrip != null) ResizeGrip.Visibility = Visibility.Visible;
        }

        var cm = GetContextMenu();
        if (cm != null)
        {
            var lockItem = FindMenuItemByTag(cm.Items, "lock_toggle");
            if (lockItem != null)
            {
                lockItem.Header = _isLocked
                    ? (LocalizationService.IsRussian ? "🔓 Разблокировать (перемещение)" : "🔓 Unlock (Drag & Move)")
                    : (LocalizationService.IsRussian ? "🔒 Зафиксировать (сквозные клики)" : "🔒 Lock (Click-through)");
            }
        }

        UpdateScaleUi();
        StateChangedNotification?.Invoke(IsVisible);
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            _hoverTimer.Stop();
            SetHovered(false, immediate: true);
            _config.IsEnabled = false;
        }
        else
        {
            SetHovered(false, immediate: true);
            Show();
            Topmost = true;
            _hoverTimer.Start();
            _config.IsEnabled = true;
        }

        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private double _lastLossPercent = 0.0;
    private long _lastPingMs = -1;
    private bool _lastIsTimeout = false;
    private bool _lastIsSpike = false;

    // =========================================================
    // Обновление данных из сетевых сервисов
    // =========================================================

    private readonly List<long> _sparklineSamples = new();

    private double _lastJitterMs = 0.0;
    private int _lastFps = -1;
    private int _lastCpu = -1;
    private int _lastGpu = -1;
    private int _lastRam = -1;
    private double _lastRamUsedGb = 0.0;
    private double _lastRamTotalGb = 0.0;

    public void UpdateLivePing(long pingMs, bool isTimeout, bool isSpike, double lossPercent = 0.0, double jitterMs = 0.0)
    {
        _lastLossPercent = lossPercent;
        _lastPingMs = pingMs;
        _lastIsTimeout = isTimeout;
        _lastIsSpike = isSpike;
        _lastJitterMs = jitterMs;
        UpdateScaleUi();

        var (hex, softHex) = ThemeService.GetPingColor(isTimeout ? -1 : pingMs);
        string pingStr = (isTimeout || pingMs <= 0) ? "-- ms" : $"{pingMs} ms";
        var brush = GetCachedBrush(hex);

        // 1. Classic Bar
        if (PingText != null)
        {
            PingText.Text = pingStr;
            PingText.Foreground = brush;
        }
        if (StatusDot != null) StatusDot.Fill = brush;
        if (StatusDotGlow != null && !ThemeService.IsOptimizedMode)
        {
            StatusDotGlow.Fill = GetCachedBrush(softHex);
        }

        // 2. Minimal
        if (MinimalPingText != null)
        {
            MinimalPingText.Text = pingStr;
            MinimalPingText.Foreground = brush;
        }

        // 3. Vertical Stack
        if (VerticalPingText != null)
        {
            VerticalPingText.Text = pingStr;
            VerticalPingText.Foreground = brush;
        }
        UpdateSignalBars(pingMs, isTimeout);

        // 4. NetGraph
        if (NetGraphPingText != null)
        {
            NetGraphPingText.Text = pingStr;
            NetGraphPingText.Foreground = brush;
        }
        if (NetGraphLossText != null)
        {
            NetGraphLossText.Text = $"{lossPercent:F1}%";
            NetGraphLossText.Foreground = lossPercent > 0.0 ? GetCachedBrush(ThemeService.Current.AccentRed) : GetCachedBrush("#FFFFFF");
        }
        if (NetGraphJitterText != null)
        {
            NetGraphJitterText.Text = jitterMs > 0 ? $"±{jitterMs:F1} ms" : "±0.0 ms";
        }

        // 5. Cyber HUD
        if (CyberPingText != null)
        {
            CyberPingText.Text = pingStr;
            CyberPingText.Foreground = brush;
        }
        if (CyberPingDot != null) CyberPingDot.Fill = brush;

        // Обновляем бегущий мини-график кардиограммы
        RecordSparklineSample(pingMs, isTimeout, isSpike, hex, softHex);
    }

    private bool _isInMatch;
    private string _matchClusterCode = string.Empty;
    private string _currentServerCode = "FRA";

    public void UpdateServerCode(string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            _currentServerCode = code.Trim().ToUpperInvariant();
        }
        ApplyServerCodeToAllBadges();
    }

    public void UpdateMatchStatus(bool isInMatch, string clusterCode, string relayIp)
    {
        _isInMatch = isInMatch;
        _matchClusterCode = clusterCode ?? string.Empty;

        if (isInMatch && !string.IsNullOrWhiteSpace(clusterCode))
        {
            _currentServerCode = clusterCode.Trim().ToUpperInvariant();
        }

        if (HudMatchBadge != null) HudMatchBadge.Visibility = Visibility.Collapsed;
        if (NetGraphMatchBadge != null) NetGraphMatchBadge.Visibility = Visibility.Collapsed;

        ApplyServerCodeToAllBadges();
    }

    private void ApplyServerCodeToAllBadges()
    {
        bool showBadge = _isInMatch && !string.IsNullOrWhiteSpace(_matchClusterCode);
        string label = showBadge ? _matchClusterCode.Trim().ToUpperInvariant() : string.Empty;
        var visibility = showBadge ? Visibility.Visible : Visibility.Collapsed;

        if (ClassicRegionText != null) ClassicRegionText.Text = label;
        if (MinimalRegionText != null) MinimalRegionText.Text = label;
        if (VerticalRegionText != null) VerticalRegionText.Text = label;
        if (NetGraphRegionText != null) NetGraphRegionText.Text = label;
        if (CyberRegionText != null) CyberRegionText.Text = label;

        if (ClassicRegionBadge != null) ClassicRegionBadge.Visibility = visibility;
        if (MinimalRegionBadge != null) MinimalRegionBadge.Visibility = visibility;
        if (VerticalRegionBadge != null) VerticalRegionBadge.Visibility = visibility;
        if (NetGraphRegionBadge != null) NetGraphRegionBadge.Visibility = visibility;
        if (CyberRegionBadge != null) CyberRegionBadge.Visibility = visibility;
    }

    private void UpdateSignalBars(long pingMs, bool isTimeout)
    {
        if (SigBar1 == null || SigBar2 == null || SigBar3 == null || SigBar4 == null) return;
        var muted = GetCachedBrush("#2A303C");
        var green = GetCachedBrush(ThemeService.Current.AccentGreen);
        var yellow = GetCachedBrush(ThemeService.Current.AccentYellow);
        var red = GetCachedBrush(ThemeService.Current.AccentRed);

        if (isTimeout || pingMs <= 0)
        {
            SigBar1.Fill = red;
            SigBar2.Fill = muted;
            SigBar3.Fill = muted;
            SigBar4.Fill = muted;
        }
        else if (pingMs <= 60)
        {
            SigBar1.Fill = green;
            SigBar2.Fill = green;
            SigBar3.Fill = green;
            SigBar4.Fill = green;
        }
        else if (pingMs <= 100)
        {
            SigBar1.Fill = green;
            SigBar2.Fill = green;
            SigBar3.Fill = green;
            SigBar4.Fill = muted;
        }
        else if (pingMs <= 160)
        {
            SigBar1.Fill = yellow;
            SigBar2.Fill = yellow;
            SigBar3.Fill = muted;
            SigBar4.Fill = muted;
        }
        else
        {
            SigBar1.Fill = red;
            SigBar2.Fill = muted;
            SigBar3.Fill = muted;
            SigBar4.Fill = muted;
        }
    }

    private void RecordSparklineSample(long pingMs, bool isTimeout, bool isSpike, string hex, string softHex)
    {
        long sample = (isTimeout || pingMs <= 0) ? -1 : pingMs;
        _sparklineSamples.Add(sample);

        // Храним последние 35 замера (~35 секунд реального времени)
        if (_sparklineSamples.Count > 35)
        {
            _sparklineSamples.RemoveAt(0);
        }

        RenderClassicSparkline(pingMs, isTimeout, hex, softHex);
        RenderNetGraphSparkline(hex);
    }

    private void RenderClassicSparkline(long pingMs, bool isTimeout, string hex, string softHex)
    {
        if (!_config.ShowSparkline || SparklineLine == null || SparklineFill == null)
            return;

        int count = _sparklineSamples.Count;
        if (count < 1)
            return;

        double w = 48.0;
        double h = 13.0;

        var valid = _sparklineSamples.Where(x => x > 0).ToList();
        double maxPing = valid.Count > 0 ? valid.Max() : 80;
        maxPing = Math.Max(maxPing, 80);
        maxPing = Math.Ceiling(maxPing / 20) * 20;

        var points = new PointCollection();
        if (count == 1)
        {
            double y = _sparklineSamples[0] <= 0
                ? 1.0
                : h - (Math.Clamp((double)_sparklineSamples[0] / maxPing, 0.0, 1.0) * (h - 2.5)) - 1.0;
            points.Add(new Point(0, y));
            points.Add(new Point(w, y));
        }
        else
        {
            double stepX = w / (count - 1);
            for (int i = 0; i < count; i++)
            {
                double x = (double)i * stepX;
                long val = _sparklineSamples[i];
                double y;
                if (val <= 0)
                {
                    y = 1.0; // Таймаут вылетает на верхнюю границу
                }
                else
                {
                    double normalized = Math.Clamp((double)val / maxPing, 0.0, 1.0);
                    y = h - (normalized * (h - 2.5)) - 1.0;
                }
                points.Add(new Point(x, y));
            }
        }

        SparklineLine.Points = points;

        var fillPoints = new PointCollection();
        fillPoints.Add(new Point(0, h));
        foreach (var pt in points)
        {
            fillPoints.Add(pt);
        }
        fillPoints.Add(new Point(w, h));
        SparklineFill.Points = fillPoints;

        if (ThemeService.IsOptimizedMode)
        {
            if (SparklineHeadDot != null)
            {
                SparklineHeadDot.Visibility = Visibility.Collapsed;
            }
            SparklineLine.Stroke = GetCachedBrush(hex);
            SparklineFill.Points = null;
            SparklineFill.Fill = Brushes.Transparent;
        }
        else
        {
            // Позиционируем пульсирующую головную точку графика
            if (SparklineHeadDot != null)
            {
                var lastPt = points.Last();
                Canvas.SetLeft(SparklineHeadDot, lastPt.X - 1.5);
                Canvas.SetTop(SparklineHeadDot, lastPt.Y - 1.5);
                SparklineHeadDot.Visibility = Visibility.Visible;
                SparklineHeadDot.Fill = GetCachedBrush(hex);
            }

            // Построение градиента графика, идентичного главному окну
            var theme = ThemeService.Current;
            var greenColor = (Color)ColorConverter.ConvertFromString(theme.AccentGreen);
            var yellowColor = (Color)ColorConverter.ConvertFromString(theme.AccentYellow);
            var redColor = (Color)ColorConverter.ConvertFromString(theme.AccentRed);

            double GetOffsetForPing(double ping)
            {
                double clamped = Math.Clamp(ping, 0, maxPing);
                double yPos = h - ((clamped / maxPing) * (h - 2.5)) - 1.0;
                return Math.Clamp(1.0 - (yPos / h), 0.0, 1.0);
            }

            var lineStops = new GradientStopCollection();
            var fillStops = new GradientStopCollection();

            lineStops.Add(new GradientStop(greenColor, 0.0));
            fillStops.Add(new GradientStop(Color.FromArgb(0x00, greenColor.R, greenColor.G, greenColor.B), 0.0));

            double offGreen = GetOffsetForPing(75.0);
            lineStops.Add(new GradientStop(greenColor, offGreen));
            fillStops.Add(new GradientStop(Color.FromArgb(0x20, greenColor.R, greenColor.G, greenColor.B), offGreen));

            if (maxPing > 75.0)
            {
                double offYellowStart = GetOffsetForPing(Math.Min(maxPing, 100.0));
                lineStops.Add(new GradientStop(yellowColor, offYellowStart));
                fillStops.Add(new GradientStop(Color.FromArgb(0x25, yellowColor.R, yellowColor.G, yellowColor.B), offYellowStart));

                if (maxPing > 100.0)
                {
                    double offYellowEnd = GetOffsetForPing(Math.Min(maxPing, 160.0));
                    lineStops.Add(new GradientStop(yellowColor, offYellowEnd));
                    fillStops.Add(new GradientStop(Color.FromArgb(0x2A, yellowColor.R, yellowColor.G, yellowColor.B), offYellowEnd));

                    if (maxPing > 160.0)
                    {
                        double offRedStart = GetOffsetForPing(Math.Min(maxPing, 200.0));
                        lineStops.Add(new GradientStop(redColor, offRedStart));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x30, redColor.R, redColor.G, redColor.B), offRedStart));

                        lineStops.Add(new GradientStop(redColor, 1.0));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x38, redColor.R, redColor.G, redColor.B), 1.0));
                    }
                    else
                    {
                        lineStops.Add(new GradientStop(yellowColor, 1.0));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x2A, yellowColor.R, yellowColor.G, yellowColor.B), 1.0));
                    }
                }
                else
                {
                    lineStops.Add(new GradientStop(yellowColor, 1.0));
                    fillStops.Add(new GradientStop(Color.FromArgb(0x25, yellowColor.R, yellowColor.G, yellowColor.B), 1.0));
                }
            }
            else
            {
                lineStops.Add(new GradientStop(greenColor, 1.0));
                fillStops.Add(new GradientStop(Color.FromArgb(0x20, greenColor.R, greenColor.G, greenColor.B), 1.0));
            }

            var lineBrush = new LinearGradientBrush(lineStops, new Point(0, h), new Point(0, 0))
            {
                MappingMode = BrushMappingMode.Absolute
            };
            SparklineLine.Stroke = lineBrush;

            var fillBrush = new LinearGradientBrush(fillStops, new Point(0, h), new Point(0, 0))
            {
                MappingMode = BrushMappingMode.Absolute
            };
            SparklineFill.Fill = fillBrush;
        }

        long minVal = valid.Count > 0 ? valid.Min() : 0;
        long maxVal = valid.Count > 0 ? valid.Max() : 0;
        string lossTxt = _lastLossPercent > 0 ? $" • Потери: {_lastLossPercent:F1}%" : "";
        SparklineBorder.ToolTip = $"Кардиограмма сети ({count} сек)\nОтклик: {(isTimeout ? "Таймаут" : $"{pingMs} ms")} (мин: {minVal}, макс: {maxVal}){lossTxt}";
    }

    private void RenderNetGraphSparkline(string hex)
    {
        if (NetGraphSparklineLine == null || NetGraphSparklineFill == null) return;
        int count = _sparklineSamples.Count;
        if (count < 1) return;

        double w = 216.0;
        double h = 20.0;

        var valid = _sparklineSamples.Where(x => x > 0).ToList();
        double maxPing = valid.Count > 0 ? valid.Max() : 80;
        maxPing = Math.Max(maxPing, 80);
        maxPing = Math.Ceiling(maxPing / 20) * 20;

        var points = new PointCollection();
        if (count == 1)
        {
            double y = _sparklineSamples[0] <= 0 ? 1.0 : h - (Math.Clamp((double)_sparklineSamples[0] / maxPing, 0.0, 1.0) * (h - 2.5)) - 1.0;
            points.Add(new Point(0, y));
            points.Add(new Point(w, y));
        }
        else
        {
            double stepX = w / (count - 1);
            for (int i = 0; i < count; i++)
            {
                double x = (double)i * stepX;
                long val = _sparklineSamples[i];
                double y = val <= 0 ? 1.0 : h - (Math.Clamp((double)val / maxPing, 0.0, 1.0) * (h - 2.5)) - 1.0;
                points.Add(new Point(x, y));
            }
        }

        NetGraphSparklineLine.Points = points;
        NetGraphSparklineLine.Stroke = GetCachedBrush(hex);

        if (ThemeService.IsOptimizedMode)
        {
            NetGraphSparklineFill.Points = null;
            NetGraphSparklineFill.Fill = Brushes.Transparent;
        }
        else
        {
            var fillPoints = new PointCollection();
            fillPoints.Add(new Point(0, h));
            foreach (var pt in points) fillPoints.Add(pt);
            fillPoints.Add(new Point(w, h));
            NetGraphSparklineFill.Points = fillPoints;
            try
            {
                var fillCol = (Color)ColorConverter.ConvertFromString(hex);
                NetGraphSparklineFill.Fill = new SolidColorBrush(Color.FromArgb(0x28, fillCol.R, fillCol.G, fillCol.B));
            }
            catch
            {
                NetGraphSparklineFill.Fill = Brushes.Transparent;
            }
        }
    }

    private void UpdateSparklineVisibility()
    {
        var vis = _config.ShowSparkline ? Visibility.Visible : Visibility.Collapsed;

        // 1. Classic Bar
        if (SparklineBorder != null) SparklineBorder.Visibility = vis;

        // 4. NetGraph
        if (NetGraphSparklineBorder != null) NetGraphSparklineBorder.Visibility = vis;

        var cm = GetContextMenu();
        if (cm != null)
        {
            var mi = FindMenuItemByTag(cm.Items, "sparkline_toggle");
            if (mi != null)
            {
                string title = LocalizationService.Get("SparklineMenu");
                if (string.IsNullOrEmpty(title)) title = "Кардиограмма сети";
                mi.Header = _config.ShowSparkline ? $"✓ {title}" : $"  {title}";
            }
        }
    }

    public bool IsSparklineVisible => _config.ShowSparkline;

    public void ToggleSparkline()
    {
        _config.ShowSparkline = !_config.ShowSparkline;
        UpdateSparklineVisibility();
        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private void MenuSparklineToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleSparkline();
    }

    public bool IsFpsVisible => _config.ShowFps;

    public void ToggleFps()
    {
        _config.ShowFps = !_config.ShowFps;
        UpdateFpsVisibility();
        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private void MenuFpsToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleFps();
    }

    private void UpdateFpsVisibility()
    {
        var vis = _config.ShowFps ? Visibility.Visible : Visibility.Collapsed;

        // 1. Classic Bar
        if (FpsPanel != null) FpsPanel.Visibility = vis;
        if (FpsDivider != null) FpsDivider.Visibility = vis;

        // 2. Minimal
        if (MinimalFpsText != null) MinimalFpsText.Visibility = vis;
        if (MinimalDot1 != null) MinimalDot1.Visibility = vis;

        // 3. Vertical Stack
        if (VerticalFpsRow != null) VerticalFpsRow.Visibility = vis;

        // 4. NetGraph
        if (NetGraphFpsPanel != null) NetGraphFpsPanel.Visibility = vis;

        // 5. Cyber HUD
        if (CyberFpsBorder != null) CyberFpsBorder.Visibility = vis;
        if (CyberFpsDivider != null) CyberFpsDivider.Visibility = vis;

        var cm = GetContextMenu();
        if (cm != null)
        {
            var mi = FindMenuItemByTag(cm.Items, "fps_toggle");
            if (mi != null)
            {
                string title = LocalizationService.Get("FpsMenu");
                if (string.IsNullOrEmpty(title)) title = "Кадры в секунду (FPS)";
                mi.Header = _config.ShowFps ? $"✓ {title}" : $"  {title}";
            }
        }
    }

    public void UpdateFps(int fps)
    {
        _lastFps = fps;
        Dispatcher.InvokeAsync(() =>
        {
            string fpsValStr = fps <= 0 ? "--" : $"{fps}";
            var colorHex = fps <= 0 ? ThemeService.Current.TextMuted : (fps >= 60 ? ThemeService.Current.AccentGreen : (fps >= 30 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed));
            var brush = GetCachedBrush(colorHex);

            // Classic Bar
            if (FpsValueText != null)
            {
                FpsValueText.Text = fpsValStr;
                FpsValueText.Foreground = brush;
            }

            // Minimal
            if (MinimalFpsText != null)
            {
                MinimalFpsText.Text = fps <= 0 ? "-- FPS" : $"{fps} FPS";
                MinimalFpsText.Foreground = brush;
            }

            // Vertical Stack
            if (VerticalFpsText != null)
            {
                VerticalFpsText.Text = fpsValStr;
                VerticalFpsText.Foreground = brush;
            }

            // NetGraph
            if (NetGraphFpsText != null)
            {
                NetGraphFpsText.Text = fpsValStr;
                NetGraphFpsText.Foreground = brush;
            }

            // Cyber HUD
            if (CyberFpsText != null)
            {
                CyberFpsText.Text = fpsValStr;
                CyberFpsText.Foreground = GetCachedBrush("#FFFFFF");
            }
        });
    }

    public bool IsCpuVisible => _config.ShowCpu;

    public void ToggleCpu()
    {
        _config.ShowCpu = !_config.ShowCpu;
        UpdateCpuVisibility();
        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private void MenuCpuToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleCpu();
    }

    private void UpdateCpuVisibility()
    {
        var vis = _config.ShowCpu ? Visibility.Visible : Visibility.Collapsed;

        // 1. Classic Bar
        if (CpuPanel != null) CpuPanel.Visibility = vis;
        if (CpuDivider != null) CpuDivider.Visibility = vis;

        // 2. Minimal
        if (MinimalCpuText != null) MinimalCpuText.Visibility = vis;
        if (MinimalDot2 != null) MinimalDot2.Visibility = vis;

        // 3. Vertical Stack
        if (VerticalCpuRow != null) VerticalCpuRow.Visibility = vis;

        // 4. NetGraph
        if (NetGraphCpuPanel != null) NetGraphCpuPanel.Visibility = vis;

        // 5. Cyber HUD
        if (CyberCpuBorder != null) CyberCpuBorder.Visibility = vis;
        if (CyberCpuDivider != null) CyberCpuDivider.Visibility = vis;

        var cm = GetContextMenu();
        if (cm != null)
        {
            var mi = FindMenuItemByTag(cm.Items, "cpu_toggle");
            if (mi != null)
            {
                string title = LocalizationService.Get("CpuMenu");
                if (string.IsNullOrEmpty(title)) title = "Нагрузка ЦП (CPU)";
                mi.Header = _config.ShowCpu ? $"✓ {title}" : $"  {title}";
            }
        }
    }

    public void UpdateCpu(int cpuPercent)
    {
        _lastCpu = cpuPercent;
        Dispatcher.InvokeAsync(() =>
        {
            string cpuValStr = cpuPercent < 0 ? "--%" : $"{cpuPercent}%";
            var colorHex = cpuPercent < 0 ? ThemeService.Current.TextMuted : (cpuPercent < 60 ? ThemeService.Current.AccentGreen : (cpuPercent < 85 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed));
            var brush = GetCachedBrush(colorHex);

            // Classic Bar
            if (CpuValueText != null)
            {
                CpuValueText.Text = cpuValStr;
                CpuValueText.Foreground = brush;
            }

            // Minimal
            if (MinimalCpuText != null)
            {
                MinimalCpuText.Text = $"CPU {cpuValStr}";
                MinimalCpuText.Foreground = GetCachedBrush(ThemeService.Current.AccentBlue);
            }

            // Vertical Stack
            if (VerticalCpuText != null)
            {
                VerticalCpuText.Text = cpuValStr;
                VerticalCpuText.Foreground = GetCachedBrush("#FFFFFF");
            }
            if (VerticalCpuBar != null)
            {
                double totalW = 110.0;
                double pct = Math.Clamp(cpuPercent, 0, 100) / 100.0;
                VerticalCpuBar.Width = Math.Max(0, totalW * pct);
                VerticalCpuBar.Background = brush;
            }

            // NetGraph
            if (NetGraphCpuText != null)
            {
                NetGraphCpuText.Text = cpuValStr;
                NetGraphCpuText.Foreground = GetCachedBrush("#FFFFFF");
            }

            // Cyber HUD
            if (CyberCpuText != null)
            {
                CyberCpuText.Text = cpuValStr;
                CyberCpuText.Foreground = GetCachedBrush("#FFFFFF");
            }
        });
    }

    public bool IsGpuVisible => _config.ShowGpu;

    public void ToggleGpu()
    {
        _config.ShowGpu = !_config.ShowGpu;
        UpdateGpuVisibility();
        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private void MenuGpuToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleGpu();
    }

    private void UpdateGpuVisibility()
    {
        var vis = _config.ShowGpu ? Visibility.Visible : Visibility.Collapsed;

        // 1. Classic Bar
        if (GpuPanel != null) GpuPanel.Visibility = vis;
        if (GpuDivider != null) GpuDivider.Visibility = vis;

        // 2. Minimal
        if (MinimalGpuText != null) MinimalGpuText.Visibility = vis;
        if (MinimalDot3 != null) MinimalDot3.Visibility = vis;

        // 3. Vertical Stack
        if (VerticalGpuRow != null) VerticalGpuRow.Visibility = vis;

        // 4. NetGraph
        if (NetGraphGpuPanel != null) NetGraphGpuPanel.Visibility = vis;

        // 5. Cyber HUD
        if (CyberGpuBorder != null) CyberGpuBorder.Visibility = vis;
        if (CyberGpuDivider != null) CyberGpuDivider.Visibility = vis;

        var cm = GetContextMenu();
        if (cm != null)
        {
            var mi = FindMenuItemByTag(cm.Items, "gpu_toggle");
            if (mi != null)
            {
                string title = LocalizationService.Get("GpuMenu");
                if (string.IsNullOrEmpty(title)) title = "Нагрузка ГП (GPU)";
                mi.Header = _config.ShowGpu ? $"✓ {title}" : $"  {title}";
            }
        }
    }

    public void UpdateGpu(int gpuPercent)
    {
        _lastGpu = gpuPercent;
        Dispatcher.InvokeAsync(() =>
        {
            string gpuValStr = gpuPercent < 0 ? "--%" : $"{gpuPercent}%";
            var colorHex = gpuPercent < 0 ? ThemeService.Current.TextMuted : (gpuPercent < 60 ? ThemeService.Current.AccentGreen : (gpuPercent < 85 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed));
            var brush = GetCachedBrush(colorHex);

            // Classic Bar
            if (GpuValueText != null)
            {
                GpuValueText.Text = gpuValStr;
                GpuValueText.Foreground = brush;
            }

            // Minimal
            if (MinimalGpuText != null)
            {
                MinimalGpuText.Text = $"GPU {gpuValStr}";
                MinimalGpuText.Foreground = GetCachedBrush("#BD93F9");
            }

            // Vertical Stack
            if (VerticalGpuText != null)
            {
                VerticalGpuText.Text = gpuValStr;
                VerticalGpuText.Foreground = GetCachedBrush("#FFFFFF");
            }
            if (VerticalGpuBar != null)
            {
                double totalW = 110.0;
                double pct = Math.Clamp(gpuPercent, 0, 100) / 100.0;
                VerticalGpuBar.Width = Math.Max(0, totalW * pct);
                VerticalGpuBar.Background = brush;
            }

            // NetGraph
            if (NetGraphGpuText != null)
            {
                NetGraphGpuText.Text = gpuValStr;
                NetGraphGpuText.Foreground = GetCachedBrush("#FFFFFF");
            }

            // Cyber HUD
            if (CyberGpuText != null)
            {
                CyberGpuText.Text = gpuValStr;
                CyberGpuText.Foreground = GetCachedBrush("#FFFFFF");
            }
        });
    }

    public bool IsRamVisible => _config.ShowRam;

    public void ToggleRam()
    {
        _config.ShowRam = !_config.ShowRam;
        UpdateRamVisibility();
        SaveConfig();
        StateChangedNotification?.Invoke(IsVisible);
    }

    private void MenuRamToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleRam();
    }

    private void UpdateRamVisibility()
    {
        var vis = _config.ShowRam ? Visibility.Visible : Visibility.Collapsed;

        // 1. Classic Bar
        if (RamPanel != null) RamPanel.Visibility = vis;
        if (RamDivider != null) RamDivider.Visibility = vis;

        // 2. Minimal
        if (MinimalRamText != null) MinimalRamText.Visibility = vis;
        if (MinimalDotRam != null) MinimalDotRam.Visibility = vis;

        // 3. Vertical Stack
        if (VerticalRamRow != null) VerticalRamRow.Visibility = vis;

        // 4. NetGraph
        if (NetGraphRamPanel != null) NetGraphRamPanel.Visibility = vis;
        if (NetGraphRamText != null) NetGraphRamText.Visibility = vis;
        if (NetGraphRamLabel != null) NetGraphRamLabel.Visibility = vis;

        // 5. Cyber HUD
        if (CyberRamBorder != null) CyberRamBorder.Visibility = vis;
        if (CyberRamDivider != null) CyberRamDivider.Visibility = vis;

        var cm = GetContextMenu();
        if (cm != null)
        {
            var mi = FindMenuItemByTag(cm.Items, "ram_toggle");
            if (mi != null)
            {
                string title = LocalizationService.Get("RamMenu");
                if (string.IsNullOrEmpty(title)) title = "Нагрузка ОЗУ (RAM)";
                mi.Header = _config.ShowRam ? $"✓ {title}" : $"  {title}";
            }
        }
    }

    public void UpdateRam(int ramPercent, double usedGb = 0, double totalGb = 0)
    {
        _lastRam = ramPercent;
        _lastRamUsedGb = usedGb;
        _lastRamTotalGb = totalGb;

        Dispatcher.InvokeAsync(() =>
        {
            string ramValStr = ramPercent < 0 ? "--%" : $"{ramPercent}%";
            var colorHex = ramPercent < 0 
                ? ThemeService.Current.TextMuted 
                : (ramPercent < 65 ? "#2DD4BF" : (ramPercent < 85 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed));
            var brush = GetCachedBrush(colorHex);

            // 1. Classic Bar
            if (RamValueText != null)
            {
                RamValueText.Text = ramValStr;
                RamValueText.Foreground = GetCachedBrush("#FFFFFF");
                if (usedGb > 0 && totalGb > 0 && RamPanel != null)
                {
                    RamPanel.ToolTip = $"Оперативная память (ОЗУ): {usedGb:F1} / {totalGb:F1} ГБ ({ramValStr})";
                }
            }

            // 2. Minimal
            if (MinimalRamText != null)
            {
                MinimalRamText.Text = $"RAM {ramValStr}";
                MinimalRamText.Foreground = GetCachedBrush("#2DD4BF");
            }

            // 3. Vertical Stack
            if (VerticalRamText != null)
            {
                VerticalRamText.Text = ramValStr;
                VerticalRamText.Foreground = GetCachedBrush("#FFFFFF");
            }
            if (VerticalRamBar != null)
            {
                double totalW = 110.0;
                double pct = Math.Clamp(ramPercent, 0, 100) / 100.0;
                VerticalRamBar.Width = Math.Max(0, totalW * pct);
                VerticalRamBar.Background = brush;
            }

            // 4. NetGraph
            if (NetGraphRamText != null)
            {
                NetGraphRamText.Text = ramValStr;
                NetGraphRamText.Foreground = GetCachedBrush("#FFFFFF");
            }

            // 5. Cyber HUD
            if (CyberRamText != null)
            {
                CyberRamText.Text = ramValStr;
                CyberRamText.Foreground = GetCachedBrush("#FFFFFF");
            }
        });
    }

    public void UpdateTelemetry(OverlayTelemetryData data)
    {
        if (data == null) return;
        UpdateLivePing(data.Ping, data.IsTimeout, data.IsSpike, data.PacketLoss, data.Jitter);
        UpdateFps(data.Fps);
        UpdateCpu(data.Cpu);
        UpdateGpu(data.Gpu);
        UpdateRam(data.Ram, data.RamUsedGb, data.RamTotalGb);
    }

    private FrameworkElement? GetActiveLayoutBorder()
    {
        return (_config.LayoutPreset?.ToLowerInvariant()) switch
        {
            "minimal" => MinimalBorder,
            "vertical_stack" => VerticalStackBorder,
            "net_graph" => NetGraphBorder,
            "cyber_hud" => CyberHudBorder,
            _ => PillBorder
        };
    }

    // =========================================================
    // Пресеты вида и настройка прозрачности оверлея
    // =========================================================

    public string CurrentPresetId => _config.LayoutPreset ?? "classic_bar";

    public void SetPreset(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId)) presetId = "classic_bar";
        _config.LayoutPreset = presetId;
        SaveConfig();

        if (PillBorder != null) PillBorder.Visibility = Visibility.Collapsed;
        if (MinimalBorder != null) MinimalBorder.Visibility = Visibility.Collapsed;
        if (VerticalStackBorder != null) VerticalStackBorder.Visibility = Visibility.Collapsed;
        if (NetGraphBorder != null) NetGraphBorder.Visibility = Visibility.Collapsed;
        if (CyberHudBorder != null) CyberHudBorder.Visibility = Visibility.Collapsed;

        switch (presetId.ToLowerInvariant())
        {
            case "minimal":
                if (MinimalBorder != null) MinimalBorder.Visibility = Visibility.Visible;
                break;
            case "vertical_stack":
                if (VerticalStackBorder != null) VerticalStackBorder.Visibility = Visibility.Visible;
                break;
            case "net_graph":
                if (NetGraphBorder != null) NetGraphBorder.Visibility = Visibility.Visible;
                break;
            case "cyber_hud":
                if (CyberHudBorder != null) CyberHudBorder.Visibility = Visibility.Visible;
                break;
            case "classic_bar":
            default:
                if (PillBorder != null) PillBorder.Visibility = Visibility.Visible;
                break;
        }

        UpdateLayoutMenuChecks();
        EnsureWindowOnScreen();

        if (_lastPingMs > 0 || _lastIsTimeout)
        {
            UpdateLivePing(_lastPingMs, _lastIsTimeout, _lastIsSpike, _lastLossPercent, _lastJitterMs);
        }
        UpdateFps(_lastFps);
        UpdateCpu(_lastCpu);
        UpdateGpu(_lastGpu);
        UpdateRam(_lastRam, _lastRamUsedGb, _lastRamTotalGb);
        UpdateSparklineVisibility();
        UpdateFpsVisibility();
        UpdateCpuVisibility();
        UpdateGpuVisibility();
        UpdateRamVisibility();
        UpdateMatchStatus(_isInMatch, _matchClusterCode, string.Empty);
    }

    private void PresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            SetPreset(tag);
        }
    }

    private ContextMenu? GetContextMenu() => TryFindResource("OverlayContextMenu") as ContextMenu;

    private static MenuItem? FindMenuItemByTag(ItemCollection items, string tag)
    {
        foreach (var item in items)
        {
            if (item is MenuItem mi)
            {
                if (tag.Equals(mi.Tag as string, StringComparison.OrdinalIgnoreCase))
                    return mi;
                if (mi.Items.Count > 0)
                {
                    var found = FindMenuItemByTag(mi.Items, tag);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    private (Slider? slider, TextBlock? label) GetOpacitySliderControls()
    {
        var cm = GetContextMenu();
        if (cm == null) return (null, null);
        var opacitySubmenu = FindMenuItemByTag(cm.Items, "opacity_submenu");
        if (opacitySubmenu == null) return (null, null);
        foreach (var item in opacitySubmenu.Items)
        {
            if (item is MenuItem mi && mi.Header is StackPanel sp)
            {
                TextBlock? tb = null;
                Slider? sl = null;
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock t) tb = t;
                    if (child is Slider s) sl = s;
                }
                return (sl, tb);
            }
        }
        return (null, null);
    }

    private void UpdateLayoutMenuChecks()
    {
        var cm = GetContextMenu();
        if (cm == null) return;
        var layoutSubmenu = FindMenuItemByTag(cm.Items, "layout_submenu");
        if (layoutSubmenu == null) return;

        string current = CurrentPresetId.ToLowerInvariant();
        void SetCheck(string tag, string locKey, string defaultTitle)
        {
            var item = FindMenuItemByTag(layoutSubmenu.Items, tag);
            if (item == null) return;
            string title = LocalizationService.Get(locKey);
            if (string.IsNullOrEmpty(title)) title = defaultTitle;
            item.Header = current == tag ? $"✓ {title}" : $"  {title}";
        }

        SetCheck("classic_bar", "PresetClassicBar", "Classic Bar (По умолчанию)");
        SetCheck("minimal", "PresetMinimal", "Ultra-Minimalist (Микро-режим)");
        SetCheck("vertical_stack", "PresetVerticalStack", "Vertical Widget (Угловой стек)");
        SetCheck("net_graph", "PresetNetGraph", "Pro Gamer (NetGraph телеметрия)");
        SetCheck("cyber_hud", "PresetCyberHud", "Cyber HUD (Футуристичный)");
    }

    public double BaseOpacity => _config.BaseOpacity > 0 ? _config.BaseOpacity : 0.92;

    public void SetBaseOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.10, 1.00);
        _config.BaseOpacity = Math.Round(opacity, 2);
        _config.Opacity = _config.BaseOpacity;
        SaveConfig();
        UpdateOpacityMenuChecks();

        if (!_isHovered)
        {
            if (ThemeService.IsOptimizedMode)
            {
                BeginAnimation(UIElement.OpacityProperty, null);
                Opacity = _config.BaseOpacity;
            }
            else
            {
                var anim = new DoubleAnimation
                {
                    To = _config.BaseOpacity,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }
    }

    private void OpacityPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double op))
        {
            SetBaseOpacity(op);
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider s && s.IsLoaded)
        {
            SetBaseOpacity(e.NewValue);
        }
    }

    private void UpdateOpacityMenuChecks()
    {
        var cm = GetContextMenu();
        if (cm == null) return;
        var opacitySubmenu = FindMenuItemByTag(cm.Items, "opacity_submenu");
        if (opacitySubmenu == null) return;

        double current = BaseOpacity;
        int pct = (int)Math.Round(current * 100);

        void SetCheck(string tag, double target, string locKey, string defaultTitle)
        {
            var item = FindMenuItemByTag(opacitySubmenu.Items, tag);
            if (item == null) return;
            string title = LocalizationService.Get(locKey);
            if (string.IsNullOrEmpty(title)) title = defaultTitle;
            bool isMatch = Math.Abs(current - target) < 0.03;
            item.Header = isMatch ? $"✓ {title}" : $"  {title}";
        }

        SetCheck("1.00", 1.00, "Opacity100", "100% (Непрозрачный)");
        SetCheck("0.85", 0.85, "Opacity85", "85% (Оптимальная)");
        SetCheck("0.75", 0.75, "Opacity75", "75%");
        SetCheck("0.50", 0.50, "Opacity50", "50% (Полупрозрачный)");
        SetCheck("0.25", 0.25, "Opacity25", "25% (Призрачный)");

        var (slider, label) = GetOpacitySliderControls();
        if (slider != null && Math.Abs(slider.Value - current) > 0.01)
        {
            slider.Value = current;
        }
        if (label != null)
        {
            label.Text = $"{LocalizationService.Get("OverlayOpacity")}: {pct}%";
        }
    }

    private void UpdateScaleMenuChecks()
    {
        var cm = GetContextMenu();
        if (cm == null) return;
        var scaleHeader = FindMenuItemByTag(cm.Items, "scale_header");
        if (scaleHeader != null)
        {
            scaleHeader.Header = LocalizationService.Get("OverlayScaleMenu");
        }

        double current = _config.Scale;
        void SetCheck(string tag, double target, string locKey, string defaultTitle)
        {
            var item = FindMenuItemByTag(cm.Items, tag);
            if (item == null) return;
            string title = LocalizationService.Get(locKey);
            if (string.IsNullOrEmpty(title)) title = defaultTitle;
            bool isMatch = Math.Abs(current - target) < 0.04;
            item.Header = isMatch ? $"✓ {title}" : $"  {title}";
        }

        SetCheck("0.75", 0.75, "Scale75", "75% (Компактный)");
        SetCheck("0.90", 0.90, "Scale90", "90%");
        SetCheck("1.00", 1.00, "Scale100", "100% (По умолчанию)");
        SetCheck("1.15", 1.15, "Scale115", "115%");
        SetCheck("1.30", 1.30, "Scale130", "130% (Увеличенный)");
        SetCheck("1.50", 1.50, "Scale150", "150% (Большой / 2K)");
        SetCheck("1.80", 1.80, "Scale180", "180% (4K мониторы)");
    }

    private void UpdateContextMenuLocalization()
    {
        var cm = GetContextMenu();
        if (cm != null)
        {
            var layoutSubmenu = FindMenuItemByTag(cm.Items, "layout_submenu");
            if (layoutSubmenu != null)
            {
                layoutSubmenu.Header = LocalizationService.Get("OverlayLayout");
            }
            var opacitySubmenu = FindMenuItemByTag(cm.Items, "opacity_submenu");
            if (opacitySubmenu != null)
            {
                opacitySubmenu.Header = LocalizationService.Get("OverlayOpacity");
            }
            var themeSubmenu = FindMenuItemByTag(cm.Items, "theme_submenu");
            if (themeSubmenu != null)
            {
                themeSubmenu.Header = $"🎨 {LocalizationService.Get("ThemeMenu")}";
            }
            var optItem = FindMenuItemByTag(cm.Items, "opt_toggle");
            if (optItem != null)
            {
                optItem.Header = ThemeService.IsOptimizedMode
                    ? (LocalizationService.IsRussian ? "✓ Оптимизация интерфейса" : "✓ Interface Optimization")
                    : (LocalizationService.IsRussian ? "  Оптимизация интерфейса" : "  Interface Optimization");
            }
            var lockItem = FindMenuItemByTag(cm.Items, "lock_toggle");
            if (lockItem != null)
            {
                lockItem.Header = _isLocked
                    ? (LocalizationService.IsRussian ? "🔓 Разблокировать (перемещение)" : "🔓 Unlock (Drag & Move)")
                    : (LocalizationService.IsRussian ? "🔒 Зафиксировать (сквозные клики)" : "🔒 Lock (Click-through)");
            }
            var closeItem = FindMenuItemByTag(cm.Items, "close_toggle");
            if (closeItem != null)
            {
                closeItem.Header = LocalizationService.IsRussian ? "✕ Скрыть оверлей" : "✕ Hide Overlay";
            }
        }
        UpdateLayoutMenuChecks();
        UpdateOpacityMenuChecks();
        UpdateScaleMenuChecks();
        UpdateScaleUi();
        UpdateSparklineVisibility();
        UpdateFpsVisibility();
        UpdateCpuVisibility();
        UpdateGpuVisibility();
        UpdateRamVisibility();
    }

    private void OnOverlayLanguageChanged(string lang)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateContextMenuLocalization();
            UpdateContextMenuHotkeys();
            UpdateOptimizationMenuHeader();
        });
    }

    private void OnThemeChanged(ThemeDefinition theme)
    {
        try
        {
            var sparklineBg = GetCachedBrush(theme.HudSparklineBg);
            var sparklineBorder = GetCachedBrush(theme.HudSparklineBorder);
            var dividerBrush = GetCachedBrush(theme.BorderColor);
            var mutedBrush = GetCachedBrush(theme.TextMuted);

            if (PillBorder != null)
            {
                PillBorder.Background = GetCachedBrush(theme.HudPillBg);
                PillBorder.BorderBrush = GetCachedBrush(theme.HudBorder);
                PillBorder.Effect = null;

                if (ThemeService.IsOptimizedMode)
                {
                    // Режим "Оптимизация": строгий плоский прямоугольный вид диспетчера задач без размытия
                    PillBorder.CornerRadius = new CornerRadius(3);
                    PillBorder.Padding = new Thickness(8, 4, 8, 4);
                }
                else
                {
                    // Стандартный капсульный HUD с чистой hairline-границей
                    PillBorder.CornerRadius = new CornerRadius(16);
                    PillBorder.Padding = new Thickness(12, 6, 12, 6);
                }
            }

            // 2. Minimal
            if (MinimalBorder != null)
            {
                if (ThemeService.IsOptimizedMode)
                {
                    if (MinimalPingText != null) MinimalPingText.Effect = null;
                    if (MinimalFpsText != null) MinimalFpsText.Effect = null;
                    if (MinimalCpuText != null) MinimalCpuText.Effect = null;
                    if (MinimalGpuText != null) MinimalGpuText.Effect = null;
                    if (MinimalRamText != null) MinimalRamText.Effect = null;
                }
                else
                {
                    if (MinimalPingText != null && MinimalPingShadow != null) MinimalPingText.Effect = MinimalPingShadow;
                    if (MinimalFpsText != null && MinimalFpsShadow != null) MinimalFpsText.Effect = MinimalFpsShadow;
                    if (MinimalCpuText != null && MinimalCpuShadow != null) MinimalCpuText.Effect = MinimalCpuShadow;
                    if (MinimalGpuText != null && MinimalGpuShadow != null) MinimalGpuText.Effect = MinimalGpuShadow;
                    if (MinimalRamText != null && MinimalRamShadow != null) MinimalRamText.Effect = MinimalRamShadow;
                }
            }

            // 3. Vertical Stack
            if (VerticalStackBorder != null)
            {
                VerticalStackBorder.Effect = null;
                VerticalStackBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(3) : new CornerRadius(10);
            }

            // 4. NetGraph
            if (NetGraphBorder != null)
            {
                NetGraphBorder.Effect = null;
                NetGraphBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(2) : new CornerRadius(6);
            }

            // 5. Cyber HUD
            if (CyberHudBorder != null)
            {
                var accentBrush = GetCachedBrush(theme.AccentGlow ?? theme.AccentBlue);
                CyberHudBorder.BorderBrush = accentBrush;
                if (ThemeService.IsOptimizedMode)
                {
                    CyberHudBorder.Effect = null;
                    CyberHudBorder.CornerRadius = new CornerRadius(3);
                }
                else
                {
                    if (CyberHudShadow != null)
                    {
                        try
                        {
                            CyberHudShadow.Color = (Color)ColorConverter.ConvertFromString(theme.AccentGlow ?? theme.AccentBlue);
                        }
                        catch { }
                        CyberHudBorder.Effect = CyberHudShadow;
                    }
                    CyberHudBorder.CornerRadius = new CornerRadius(8);
                }
            }

            // Статусная точка: в оптимизации убираем неоновые блики/свечение
            if (StatusDotGlow != null)
            {
                StatusDotGlow.Visibility = ThemeService.IsOptimizedMode ? Visibility.Collapsed : Visibility.Visible;
            }
            if (StatusDot != null)
            {
                StatusDot.Width = ThemeService.IsOptimizedMode ? 5 : 6;
                StatusDot.Height = ThemeService.IsOptimizedMode ? 5 : 6;
            }

            // Текст пинга: в режиме оптимизации моноширинный табличный шрифт
            if (PingText != null)
            {
                PingText.FontFamily = ThemeService.IsOptimizedMode
                    ? new FontFamily("Consolas, Segoe UI, sans-serif")
                    : new FontFamily("Segoe UI, Inter, sans-serif");
                PingText.FontSize = ThemeService.IsOptimizedMode ? 12.5 : 13.5;
                PingText.FontWeight = ThemeService.IsOptimizedMode ? FontWeights.SemiBold : FontWeights.Bold;
            }

            // Кардиограмма / Sparkline: в оптимизации компактный контур без градиентных заливок и вспышек
            if (SparklineBorder != null)
            {
                SparklineBorder.Background = sparklineBg;
                SparklineBorder.BorderBrush = sparklineBorder;
                SparklineBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(1) : new CornerRadius(3);
                SparklineBorder.Width = ThemeService.IsOptimizedMode ? 44 : 48;
                SparklineBorder.Height = ThemeService.IsOptimizedMode ? 14 : 15;
            }
            if (SparklineLine != null)
            {
                SparklineLine.StrokeThickness = ThemeService.IsOptimizedMode ? 1.0 : 1.2;
                RenderOptions.SetEdgeMode(SparklineLine, ThemeService.IsOptimizedMode ? EdgeMode.Aliased : EdgeMode.Unspecified);
            }
            if (SparklineHeadDot != null)
            {
                SparklineHeadDot.Visibility = Visibility.Collapsed;
            }
            if (SparklineFill != null && ThemeService.IsOptimizedMode)
            {
                SparklineFill.Points = null;
                SparklineFill.Fill = Brushes.Transparent;
            }

            // Разделители
            if (FpsDivider != null) FpsDivider.Fill = dividerBrush;
            if (CpuDivider != null) CpuDivider.Fill = dividerBrush;
            if (GpuDivider != null) GpuDivider.Fill = dividerBrush;
            if (ResizeDivider != null) ResizeDivider.Fill = dividerBrush;

            // Блок FPS (в оптимизации чистый плоский лейбл без неоновых подложек, как в диспетчере задач)
            if (FpsBadgeBorder != null)
            {
                FpsBadgeBorder.Background = ThemeService.IsOptimizedMode ? Brushes.Transparent : GetCachedBrush(theme.AccentYellowSoft);
                FpsBadgeBorder.Padding = ThemeService.IsOptimizedMode ? new Thickness(0) : new Thickness(5, 1.5, 5, 1.5);
                FpsBadgeBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(4);
            }
            if (FpsBadgeText != null)
            {
                FpsBadgeText.Foreground = ThemeService.IsOptimizedMode ? mutedBrush : GetCachedBrush(theme.AccentYellow);
                FpsBadgeText.FontSize = ThemeService.IsOptimizedMode ? 10.5 : 10;
                FpsBadgeText.FontWeight = ThemeService.IsOptimizedMode ? FontWeights.Normal : FontWeights.Bold;
            }
            if (FpsValueText != null)
            {
                FpsValueText.Margin = ThemeService.IsOptimizedMode ? new Thickness(3, 0, 0, 0) : new Thickness(6, 0, 0, 0);
                FpsValueText.FontFamily = ThemeService.IsOptimizedMode ? new FontFamily("Consolas, Segoe UI, sans-serif") : new FontFamily("Segoe UI, Inter, sans-serif");
                FpsValueText.FontSize = ThemeService.IsOptimizedMode ? 11.5 : 12;
            }

            // Блок CPU (в оптимизации чистый плоский лейбл)
            if (CpuBadgeBorder != null)
            {
                CpuBadgeBorder.Background = ThemeService.IsOptimizedMode ? Brushes.Transparent : GetCachedBrush(theme.AccentBlueSoft);
                CpuBadgeBorder.Padding = ThemeService.IsOptimizedMode ? new Thickness(0) : new Thickness(5, 1.5, 5, 1.5);
                CpuBadgeBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(4);
            }
            if (CpuBadgeText != null)
            {
                CpuBadgeText.Foreground = ThemeService.IsOptimizedMode ? mutedBrush : GetCachedBrush(theme.AccentBlue);
                CpuBadgeText.FontSize = ThemeService.IsOptimizedMode ? 10.5 : 10;
                CpuBadgeText.FontWeight = ThemeService.IsOptimizedMode ? FontWeights.Normal : FontWeights.Bold;
            }
            if (CpuValueText != null)
            {
                CpuValueText.Margin = ThemeService.IsOptimizedMode ? new Thickness(3, 0, 0, 0) : new Thickness(6, 0, 0, 0);
                CpuValueText.FontFamily = ThemeService.IsOptimizedMode ? new FontFamily("Consolas, Segoe UI, sans-serif") : new FontFamily("Segoe UI, Inter, sans-serif");
                CpuValueText.FontSize = ThemeService.IsOptimizedMode ? 11.5 : 12;
            }

            // Блок GPU (в оптимизации чистый плоский лейбл)
            if (GpuBadgeBorder != null)
            {
                GpuBadgeBorder.Background = ThemeService.IsOptimizedMode ? Brushes.Transparent : GetCachedBrush("#18BD93F9");
                GpuBadgeBorder.Padding = ThemeService.IsOptimizedMode ? new Thickness(0) : new Thickness(5, 1.5, 5, 1.5);
                GpuBadgeBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(4);
            }
            if (GpuBadgeText != null)
            {
                GpuBadgeText.Foreground = ThemeService.IsOptimizedMode ? mutedBrush : GetCachedBrush("#BD93F9");
                GpuBadgeText.FontSize = ThemeService.IsOptimizedMode ? 10.5 : 10;
                GpuBadgeText.FontWeight = ThemeService.IsOptimizedMode ? FontWeights.Normal : FontWeights.Bold;
            }
            if (GpuValueText != null)
            {
                GpuValueText.Margin = ThemeService.IsOptimizedMode ? new Thickness(3, 0, 0, 0) : new Thickness(6, 0, 0, 0);
                GpuValueText.FontFamily = ThemeService.IsOptimizedMode ? new FontFamily("Consolas, Segoe UI, sans-serif") : new FontFamily("Segoe UI, Inter, sans-serif");
                GpuValueText.FontSize = ThemeService.IsOptimizedMode ? 11.5 : 12;
            }

            if (RamDivider != null) RamDivider.Fill = dividerBrush;

            // Блок RAM (в оптимизации чистый плоский лейбл)
            if (RamBadgeBorder != null)
            {
                RamBadgeBorder.Background = ThemeService.IsOptimizedMode ? Brushes.Transparent : GetCachedBrush("#182DD4BF");
                RamBadgeBorder.Padding = ThemeService.IsOptimizedMode ? new Thickness(0) : new Thickness(5, 1.5, 5, 1.5);
                RamBadgeBorder.CornerRadius = ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(4);
            }
            if (RamBadgeText != null)
            {
                RamBadgeText.Foreground = ThemeService.IsOptimizedMode ? mutedBrush : GetCachedBrush("#2DD4BF");
                RamBadgeText.FontSize = ThemeService.IsOptimizedMode ? 10.5 : 10;
                RamBadgeText.FontWeight = ThemeService.IsOptimizedMode ? FontWeights.Normal : FontWeights.Bold;
            }
            if (RamValueText != null)
            {
                RamValueText.Margin = ThemeService.IsOptimizedMode ? new Thickness(3, 0, 0, 0) : new Thickness(6, 0, 0, 0);
                RamValueText.FontFamily = ThemeService.IsOptimizedMode ? new FontFamily("Consolas, Segoe UI, sans-serif") : new FontFamily("Segoe UI, Inter, sans-serif");
                RamValueText.FontSize = ThemeService.IsOptimizedMode ? 11.5 : 12;
            }

            // Кнопки управления
            if (ControlsPanel != null)
            {
                ControlsPanel.Margin = ThemeService.IsOptimizedMode ? new Thickness(7, 0, 0, 0) : new Thickness(9, 0, 0, 0);
            }
            if (LockVectorIcon != null)
            {
                LockVectorIcon.StrokeThickness = ThemeService.IsOptimizedMode ? 1.0 : 1.2;
                if (!_isLocked) LockVectorIcon.Stroke = mutedBrush;
            }
            if (CloseVectorIcon != null)
            {
                CloseVectorIcon.StrokeThickness = ThemeService.IsOptimizedMode ? 1.0 : 1.3;
                CloseVectorIcon.Stroke = mutedBrush;
            }

            if (_lastPingMs > 0 || _lastIsTimeout)
            {
                UpdateLivePing(_lastPingMs, _lastIsTimeout, _lastIsSpike, _lastLossPercent, _lastJitterMs);
            }

            UpdateThemeMenuChecks();
        }
        catch
        {
            // Ignore color parsing errors
        }
    }

    private void UpdateThemeMenuChecks()
    {
        var cm = GetContextMenu();
        if (cm == null) return;
        var subMenu = FindMenuItemByTag(cm.Items, "theme_submenu");
        if (subMenu != null)
        {
            subMenu.Header = $"🎨 {LocalizationService.Get("ThemeMenu")}";
            foreach (var subItem in subMenu.Items)
            {
                if (subItem is MenuItem mi && mi.Tag is string tag)
                {
                    bool isSelected = tag.Equals(ThemeService.Current.Id, StringComparison.OrdinalIgnoreCase);
                    string rawName = mi.Header?.ToString()?.TrimStart('✓', ' ') ?? "";
                    mi.Header = isSelected ? $"✓ {rawName}" : $"  {rawName}";
                }
            }
        }
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string themeId)
        {
            ThemeService.ApplyTheme(themeId);
            _config.Theme = themeId;
            SaveConfig();
            UpdateThemeMenuChecks();
        }
    }

    public void UpdateBestSdr(string code, string name, long pingMs)
    {
        // SDR region badge was removed from overlay HUD pill as requested
    }

    // =========================================================
    // Масштабирование и изменение размера оверлея
    // =========================================================

    public double CurrentScale => _config.Scale;

    public void SetScale(double scale)
    {
        scale = Math.Clamp(scale, 0.65, 2.5);
        _config.Scale = Math.Round(scale, 2);

        if (OverlayScaleTransform != null)
        {
            OverlayScaleTransform.ScaleX = _config.Scale;
            OverlayScaleTransform.ScaleY = _config.Scale;
        }

        UpdateScaleUi();
        UpdateScaleMenuChecks();
        EnsureWindowOnScreen();
    }

    private void UpdateScaleUi()
    {
        int percent = (int)Math.Round(_config.Scale * 100);
        string scaleText = $"{percent}%";

        if (ResizeGrip != null)
        {
            ResizeGrip.ToolTip = LocalizationService.IsRussian
                ? $"Масштаб: {scaleText}\nПотяните для изменения размера\nКолесико мыши: масштаб\nДвойной клик: сброс 100%"
                : $"Scale: {scaleText}\nDrag to resize\nMouse wheel: scale\nDouble click: reset 100%";
        }

        if (PillBorder != null && !_isLocked)
        {
            if (LocalizationService.IsRussian)
            {
                string lossInfo = _lastLossPercent > 0 ? $" • Потери: {_lastLossPercent:F1}%" : "";
                PillBorder.ToolTip = $"Dota 2 Ping HUD ({scaleText}{lossInfo})\nЗажмите ЛКМ для перемещения\nКолесико мыши: масштаб (70% - 250%)\nПКМ: меню размеров\nCtrl+Shift+L: закрепить";
            }
            else
            {
                string lossInfo = _lastLossPercent > 0 ? $" • Loss: {_lastLossPercent:F1}%" : "";
                PillBorder.ToolTip = $"Dota 2 Ping HUD ({scaleText}{lossInfo})\nHold LMB to drag\nMouse wheel: scale (70% - 250%)\nRMB: context menu\nCtrl+Shift+L: lock/pin";
            }
        }
    }

    private void EnsureWindowOnScreen()
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vWidth = SystemParameters.VirtualScreenWidth;
        double vHeight = SystemParameters.VirtualScreenHeight;

        double w = ActualWidth > 0 ? ActualWidth : 220 * _config.Scale;
        double h = ActualHeight > 0 ? ActualHeight : 45 * _config.Scale;

        if (Left + w > vLeft + vWidth)
            Left = Math.Max(vLeft, vLeft + vWidth - w - 10);
        if (Top + h > vTop + vHeight)
            Top = Math.Max(vTop, vTop + vHeight - h - 10);
        if (Left < vLeft)
            Left = vLeft + 10;
        if (Top < vTop)
            Top = vTop + 10;
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isLocked) return;

        double step = e.Delta > 0 ? 0.05 : -0.05;
        double newScale = Math.Clamp(_config.Scale + step, 0.65, 2.5);
        SetScale(newScale);
        SaveConfig();
        e.Handled = true;
    }

    // =========================================================
    // Интерактивное изменение размера за ручку (Resize Grip)
    // =========================================================

    private bool _isResizing;
    private Point _resizeStartMousePos;
    private double _startScale;

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked) return;

        if (e.ClickCount == 2)
        {
            // Двойной клик по ручке сбрасывает масштаб к 100%
            SetScale(1.0);
            SaveConfig();
            e.Handled = true;
            return;
        }

        _isResizing = true;
        _resizeStartMousePos = PointToScreen(e.GetPosition(this));
        _startScale = _config.Scale;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;

        var currentMousePos = PointToScreen(e.GetPosition(this));
        double deltaX = currentMousePos.X - _resizeStartMousePos.X;
        double deltaY = currentMousePos.Y - _resizeStartMousePos.Y;

        double deltaScale = (deltaX + deltaY * 1.5) / 200.0;
        double newScale = Math.Clamp(_startScale + deltaScale, 0.65, 2.5);

        SetScale(newScale);
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ResizeGrip.ReleaseMouseCapture();
            SaveConfig();
            e.Handled = true;
        }
    }

    // =========================================================
    // Контекстное меню пресетов размера
    // =========================================================

    private void ScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double scale))
        {
            SetScale(scale);
            SaveConfig();
        }
    }

    private void ContextLock_Click(object sender, RoutedEventArgs e)
    {
        ToggleLock();
    }

    private void ContextClose_Click(object sender, RoutedEventArgs e)
    {
        ToggleVisibility();
    }

    private void OptimizationToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.SetOptimizationMode(!ThemeService.IsOptimizedMode);
        var cfg = _configService.Load();
        cfg.IsOptimizedMode = ThemeService.IsOptimizedMode;
        _configService.Save(cfg);
        UpdateOptimizationMenuHeader();
    }

    private void UpdateOptimizationMenuHeader()
    {
        var cm = GetContextMenu();
        if (cm == null) return;
        var optItem = FindMenuItemByTag(cm.Items, "opt_toggle");
        if (optItem != null)
        {
            optItem.Header = ThemeService.IsOptimizedMode
                ? (LocalizationService.IsRussian ? "✓ Оптимизация интерфейса" : "✓ Interface Optimization")
                : (LocalizationService.IsRussian ? "  Оптимизация интерфейса" : "  Interface Optimization");
        }
    }

    // =========================================================
    // Перетаскивание и события мыши
    // =========================================================

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isLocked)
        {
            _isDragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                _isDragging = false;
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SaveConfig();
    }

    private void LockBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleLock();
    }

    private void CloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleVisibility();
    }

    public void SyncConfig(OverlayConfig cfg)
    {
        _config = cfg;
        SetPreset(_config.LayoutPreset);
        SetBaseOpacity(_config.BaseOpacity);
        UpdateContextMenuHotkeys();
        UpdateOptimizationMenuHeader();
    }

    private void UpdateContextMenuHotkeys()
    {
        var cm = GetContextMenu();
        if (cm == null) return;

        foreach (var item in cm.Items)
        {
            if (item is MenuItem mi && mi.Tag is string tag)
            {
                if (tag.Equals("close_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyOverlay != null)
                {
                    mi.InputGestureText = _config.HotkeyOverlay.DisplayText;
                }
                else if (tag.Equals("lock_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyLock != null)
                {
                    mi.InputGestureText = _config.HotkeyLock.DisplayText;
                }
                else if (tag.Equals("sparkline_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeySparkline != null)
                {
                    mi.InputGestureText = _config.HotkeySparkline.DisplayText;
                }
                else if (tag.Equals("fps_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyFps != null)
                {
                    mi.InputGestureText = _config.HotkeyFps.DisplayText;
                }
                else if (tag.Equals("cpu_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyCpu != null)
                {
                    mi.InputGestureText = _config.HotkeyCpu.DisplayText;
                }
                else if (tag.Equals("gpu_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyGpu != null)
                {
                    mi.InputGestureText = _config.HotkeyGpu.DisplayText;
                }
                else if (tag.Equals("ram_toggle", StringComparison.OrdinalIgnoreCase) && _config.HotkeyRam != null)
                {
                    mi.InputGestureText = _config.HotkeyRam.DisplayText;
                }
            }
        }
    }

    private void SaveConfig(bool? overrideIsEnabled = null)
    {
        var diskCfg = _configService.Load();
        diskCfg.Left = Left;
        diskCfg.Top = Top;
        diskCfg.Scale = Math.Round(_config.Scale, 2);
        diskCfg.IsLocked = _isLocked;
        diskCfg.BaseOpacity = _config.BaseOpacity > 0 ? _config.BaseOpacity : 0.92;
        diskCfg.Opacity = diskCfg.BaseOpacity;
        diskCfg.LayoutPreset = string.IsNullOrWhiteSpace(_config.LayoutPreset) ? "classic_bar" : _config.LayoutPreset;

        if (overrideIsEnabled.HasValue)
        {
            diskCfg.IsEnabled = overrideIsEnabled.Value;
        }
        else
        {
            diskCfg.IsEnabled = _config.IsEnabled;
        }
        diskCfg.ShowSparkline = _config.ShowSparkline;
        diskCfg.ShowFps = _config.ShowFps;
        diskCfg.ShowCpu = _config.ShowCpu;
        diskCfg.ShowGpu = _config.ShowGpu;
        diskCfg.ShowRam = _config.ShowRam;
        if (!string.IsNullOrEmpty(_config.Theme))
        {
            diskCfg.Theme = _config.Theme;
        }

        // Гарантированно сохраняем хоткеи
        if (_config.HotkeyOverlay != null) diskCfg.HotkeyOverlay = _config.HotkeyOverlay;
        if (_config.HotkeyLock != null) diskCfg.HotkeyLock = _config.HotkeyLock;
        if (_config.HotkeySparkline != null) diskCfg.HotkeySparkline = _config.HotkeySparkline;
        if (_config.HotkeyFps != null) diskCfg.HotkeyFps = _config.HotkeyFps;
        if (_config.HotkeyCpu != null) diskCfg.HotkeyCpu = _config.HotkeyCpu;
        if (_config.HotkeyGpu != null) diskCfg.HotkeyGpu = _config.HotkeyGpu;

        _config = diskCfg;
        _configService.Save(_config);
    }
}
