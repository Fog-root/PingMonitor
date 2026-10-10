using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;
using DotaPingMonitor.ViewModels;
using DotaPingMonitor.Views;

namespace DotaPingMonitor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly List<(Point Pos, PingRecord Record)> _graphPoints = new();
    private readonly List<PingRecord> _allSessionRecords = new();
    private List<PingRecord> _lastRecords = new();
    private Point? _currentMousePosition;
    private ScrollViewer? _historyScrollViewer;
    private bool _isChartLive = true;
    private bool _isUpdatingScrollBar;

    private int CurrentWindowSize => Math.Max(10, _viewModel.ChartIntervalMinutes * 60);
    private readonly Action<bool> _onOptimizationModeChanged;
    private TrayService? _trayService;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        bool isStartup = StartupService.IsStartupLaunch();
        if (isStartup)
        {
            WindowState = WindowState.Minimized;
            Visibility = Visibility.Hidden;
        }

        _trayService = new TrayService(
            mainWindow: this,
            toggleOverlayAction: () => _viewModel?.ToggleOverlay(),
            explicitExitAction: () =>
            {
                _isExplicitExit = true;
                Close();
            }
        );
        _trayService.Initialize();

        if (isStartup)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.GraphDataUpdated += OnGraphDataUpdated;
        _viewModel.RequestOpenMtr += OnRequestOpenMtr;
        _viewModel.RequestHistoryAutoScroll += OnRequestHistoryAutoScroll;

        ThemeService.ThemeChanged += OnThemeChanged;
        _onOptimizationModeChanged = _ => OnThemeChanged(ThemeService.Current);
        ThemeService.OptimizationModeChanged += _onOptimizationModeChanged;

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            UpdateStatusDotAnimation();
        };
        Activated += (_, _) => _viewModel.SetWindowFocusState(true);
        Deactivated += (_, _) =>
        {
            HideHoverIndicator();
            _viewModel.SetWindowFocusState(false);
        };

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                StatusDot?.BeginAnimation(UIElement.OpacityProperty, null);
                _viewModel.SetWindowFocusState(false);
            }
            else
            {
                UpdateStatusDotAnimation();
                UpdateChartDisplay();
            }
        };

        IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue)
            {
                StatusDot?.BeginAnimation(UIElement.OpacityProperty, null);
                _viewModel.SetWindowFocusState(false);
            }
            else
            {
                UpdateStatusDotAnimation();
                UpdateChartDisplay();
            }
        };

        Action<string> onLanguageChanged = _ => Dispatcher.Invoke(() =>
        {
            UpdateChartDisplay();
            UpdateLiveIndicator();
        });
        LocalizationService.LanguageChanged += onLanguageChanged;

        Closing += (sender, e) =>
        {
            if (!_isExplicitExit && _trayService?.MinimizeToTrayOnClose == true)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _trayService?.Dispose();
            LocalizationService.LanguageChanged -= onLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            ThemeService.OptimizationModeChanged -= _onOptimizationModeChanged;
            _viewModel.Cleanup();
        };
    }

    private void LangRu_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.SetLanguageCommand.Execute("ru");
    }

    private void LangEn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.SetLanguageCommand.Execute("en");
    }

    // =========================================================
    // VIEW-SPECIFIC EVENT HANDLERS
    // =========================================================

    private DateTime _navPopupClosedTime = DateTime.MinValue;

    private void NavMenuButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void NavMenuButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((DateTime.UtcNow - _navPopupClosedTime).TotalMilliseconds < 250)
        {
            return;
        }
        _viewModel.ToggleNavMenu();
    }

    private void NavMenuButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleNavMenu();
    }

    private void NavMenuPopup_Closed(object sender, EventArgs e)
    {
        _navPopupClosedTime = DateTime.UtcNow;
    }

    private void NavDotaPingItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void NavDotaPingItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.SwitchToDotaPing();
        UpdateChartDisplay();
    }

    private void NavTaskManagerItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void NavTaskManagerItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.SwitchToTaskManager();
    }

    private void NavSpeedtestItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void NavSpeedtestItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.SwitchToSpeedtest();
    }

    private void ServerCombo_DropDownOpened(object sender, EventArgs e)
    {
        _ = _viewModel.RefreshAllTargetsPingAsync();
    }

    private void ServerItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PingTargetItemViewModel target && !target.IsCustom)
        {
            e.Handled = true;
        }
    }

    private async void DeleteCustomServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is PingTargetItemViewModel target)
        {
            ServerCombo.IsDropDownOpen = false;
            await _viewModel.DeleteCustomServerAsync(target);
        }
    }

    private async void DeleteCustomServerItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is PingTargetItemViewModel target)
        {
            ServerCombo.IsDropDownOpen = false;
            await _viewModel.DeleteCustomServerAsync(target);
        }
    }

    private async void DeleteCustomServerItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is PingTargetItemViewModel target)
        {
            ServerCombo.IsDropDownOpen = false;
            await _viewModel.DeleteCustomServerAsync(target);
        }
    }

    private void ProcessesListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            _ = _viewModel.EndTask();
        }
    }

    private void HudContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshOverlayMenuHeaders();
    }

    private void HkOverlayBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleOverlay();
    }

    private void HkOverlayBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("overlay", "Включение / скрыть HUD-оверлей");
    }

    private void HkLockBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleLock();
    }

    private void HkLockBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("lock", "Сквозные клики / фиксация оверлея");
    }

    private void HkSparklineBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleSparkline();
    }

    private void HkSparklineBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("sparkline", "Мини-график кардиограммы в HUD");
    }

    private void HkFpsBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleFps();
    }

    private void HkFpsBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("fps", "Счетчик кадров (FPS) в HUD");
    }

    private void HkCpuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleCpu();
    }

    private void HkCpuBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("cpu", "Нагрузка процессора (CPU) в HUD");
    }

    private void HkGpuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleGpu();
    }

    private void HkGpuBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("gpu", "Нагрузка видеокарты (GPU) в HUD");
    }

    private void HkRamBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleRam();
    }

    private void HkRamBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("ram", LocalizationService.IsRussian ? "Оперативная память (RAM) в HUD" : "RAM usage in HUD");
    }

    private void HkGlowBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleGlow();
    }

    private void HkGlowBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRebindDialog("glow", LocalizationService.IsRussian ? "Визуальный эффект в HUD" : "HUD visual effect");
    }

    private void OpenRebindDialog(string hotkeyId, string actionTitle)
    {
        var current = hotkeyId switch
        {
            "overlay" => _viewModel.OverlayBinding,
            "lock" => _viewModel.LockBinding,
            "sparkline" => _viewModel.SparklineBinding,
            "fps" => _viewModel.FpsBinding,
            "cpu" => _viewModel.CpuBinding,
            "gpu" => _viewModel.GpuBinding,
            "ram" => _viewModel.RamBinding,
            "glow" => _viewModel.GlowBinding,
            _ => _viewModel.OverlayBinding
        };

        var defaultBinding = MainViewModel.GetDefaultBinding(hotkeyId);

        _viewModel.SetKeyboardHookPaused(true);
        try
        {
            var dlg = new HotkeyRebindDialog(actionTitle, current, defaultBinding)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true)
            {
                _viewModel.UpdateHotkey(hotkeyId, dlg.ResultBinding);
            }
        }
        finally
        {
            _viewModel.SetKeyboardHookPaused(false);
        }
    }

    private void OnRequestOpenMtr(string targetHost, string targetName)
    {
        var mtrWin = new MtrDiagnosticsWindow(targetHost, targetName, _viewModel.Database)
        {
            Owner = this
        };
        mtrWin.ShowDialog();
    }

    private void OnRequestHistoryAutoScroll()
    {
        _historyScrollViewer ??= GetScrollViewer(HistoryList);
        double offset = _historyScrollViewer?.VerticalOffset ?? 0.0;
        if (_historyScrollViewer != null && offset > 0.5)
        {
            _historyScrollViewer.ScrollToVerticalOffset(offset + 1);
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv)
            return sv;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private void OnThemeChanged(ThemeDefinition theme)
    {
        if (TopGlowBar != null)
        {
            TopGlowBar.Visibility = ThemeService.IsOptimizedMode ? Visibility.Collapsed : Visibility.Visible;
        }

        if (AmbientGlowRect != null)
        {
            AmbientGlowRect.Visibility = ThemeService.IsOptimizedMode ? Visibility.Collapsed : Visibility.Visible;
        }

        if (TopGlowStop1 != null && TopGlowStop2 != null && TopGlowStop3 != null && TopGlowStop4 != null)
        {
            try
            {
                if (ThemeService.IsOptimizedMode)
                {
                    TopGlowStop1.Color = Colors.Transparent;
                    TopGlowStop2.Color = Colors.Transparent;
                    TopGlowStop3.Color = Colors.Transparent;
                    TopGlowStop4.Color = Colors.Transparent;
                }
                else
                {
                    var cAccent = (Color)ColorConverter.ConvertFromString(theme.AccentColor);
                    var cSecondaryHex = !string.IsNullOrEmpty(theme.PreviewColorSecondary) ? theme.PreviewColorSecondary : theme.AccentColor;
                    var cSecondary = (Color)ColorConverter.ConvertFromString(cSecondaryHex);
                    TopGlowStop1.Color = Color.FromArgb(0, cAccent.R, cAccent.G, cAccent.B);
                    TopGlowStop2.Color = cAccent;
                    TopGlowStop3.Color = cSecondary;
                    TopGlowStop4.Color = Color.FromArgb(0, cSecondary.R, cSecondary.G, cSecondary.B);
                }
            }
            catch { }
        }

        try
        {
            if (PingText != null)
            {
                PingText.FontFamily = ThemeService.IsOptimizedMode
                    ? new FontFamily("Consolas, Segoe UI, sans-serif")
                    : new FontFamily("Segoe UI, Inter, sans-serif");
            }

            if (StatusBadgeBorder != null)
            {
                StatusBadgeBorder.CornerRadius = ThemeService.IsOptimizedMode
                    ? new CornerRadius(3)
                    : new CornerRadius(12);
            }

            UpdateStatusDotAnimation();
        }
        catch { }

        if (_lastRecords.Count > 0)
        {
            DrawGraph(_lastRecords);
        }
    }

    private void UpdateStatusDotAnimation()
    {
        if (StatusDot == null) return;

        try
        {
            StatusDot.BeginAnimation(UIElement.OpacityProperty, null);
            StatusDot.Opacity = 1.0;
        }
        catch { }
    }

    private void PulseStatusDot()
    {
        if (StatusDot == null || ThemeService.IsOptimizedMode || WindowState == WindowState.Minimized || !IsVisible) return;
        try
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(250));
            StatusDot.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        catch { }
    }

    // =========================================================
    // GRAPH DRAWING (WPF CANVAS RENDERING)
    // =========================================================

    private void OnGraphDataUpdated(List<PingRecord> records)
    {
        _allSessionRecords.Clear();
        _allSessionRecords.AddRange(records);

        if (records.Count > 0)
        {
            var last = records[^1];
            string pingText = last.IsTimeout ? "Таймаут" : $"{last.PingMs} ms";
            _trayService?.UpdateTooltip($"Ping Monitoring — {pingText}");
        }

        PulseStatusDot();
        UpdateChartDisplay();
    }

    private void UpdateChartDisplay()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || _viewModel.IsTaskManagerView)
        {
            return;
        }

        int total = _allSessionRecords.Count;
        int windowSize = CurrentWindowSize;

        if (total == 0)
        {
            _isUpdatingScrollBar = true;
            ChartScrollBar.Minimum = 0;
            ChartScrollBar.Maximum = 0;
            ChartScrollBar.Value = 0;
            ChartScrollBar.IsEnabled = false;
            _isUpdatingScrollBar = false;
            LiveReturnBadge.Visibility = Visibility.Collapsed;
            DrawGraph(new List<PingRecord>());
            return;
        }

        int maxScroll = Math.Max(0, total - windowSize);

        _isUpdatingScrollBar = true;
        ChartScrollBar.Minimum = 0;
        ChartScrollBar.Maximum = maxScroll;
        ChartScrollBar.ViewportSize = windowSize;
        ChartScrollBar.SmallChange = Math.Max(1, Math.Min(5, windowSize / 10));
        ChartScrollBar.LargeChange = Math.Max(5, Math.Min(30, windowSize / 2));
        ChartScrollBar.IsEnabled = maxScroll > 0;

        List<PingRecord> recordsToShow;

        if (_isChartLive || maxScroll == 0)
        {
            _isChartLive = true;
            ChartScrollBar.Value = maxScroll;
            _isUpdatingScrollBar = false;

            int startIndex = Math.Max(0, total - windowSize);
            recordsToShow = _allSessionRecords.Skip(startIndex).Take(windowSize).ToList();

            UpdateLiveHeaderAndBadge();
        }
        else
        {
            if (ChartScrollBar.Value > maxScroll)
                ChartScrollBar.Value = maxScroll;

            _isUpdatingScrollBar = false;

            int startIndex = (int)Math.Round(ChartScrollBar.Value);
            startIndex = Math.Clamp(startIndex, 0, maxScroll);
            int count = Math.Min(windowSize, total - startIndex);
            recordsToShow = _allSessionRecords.Skip(startIndex).Take(count).ToList();

            UpdateHistoryHeaderAndBadge(recordsToShow);
        }

        DrawGraph(recordsToShow);
    }

    private void DrawGraph(List<PingRecord> records)
    {
        if (!IsVisible || WindowState == WindowState.Minimized || _viewModel.IsTaskManagerView)
        {
            _lastRecords = records;
            return;
        }

        _lastRecords = records;
        _graphPoints.Clear();

        PingLine.Points.Clear();
        PingFill.Points.Clear();

        // Удаляем динамические элементы графика, оставляя линию и заливку
        var toRemoveChart = PingChart.Children
            .Cast<UIElement>()
            .Where(e => e != PingLine && e != PingFill)
            .ToList();

        foreach (var element in toRemoveChart)
            PingChart.Children.Remove(element);

        // Очищаем шкалу оси Y
        YAxisCanvas.Children.Clear();

        if (records.Count == 0)
        {
            HideHoverIndicator();
            return;
        }

        double width = PingChart.ActualWidth;
        if (width <= 0) width = 800;

        double height = PingChart.ActualHeight;
        if (height <= 0) height = 115;

        var validRecords = records
            .Where(x => !x.IsTimeout && x.PingMs > 0)
            .ToList();

        double maxPing = validRecords.Count > 0
            ? validRecords.Max(x => x.PingMs)
            : 100;

        maxPing = Math.Max(maxPing, 100);
        maxPing = Math.Ceiling(maxPing / 50) * 50;

        // 1. Горизонтальные линии сетки и подписи оси Y (в оптимизации снижено количество линий)
        int guideLineCount = ThemeService.IsOptimizedMode ? 2 : 4;
        for (int i = 0; i <= guideLineCount; i++)
        {
            double pingValue = i * maxPing / guideLineCount;
            double y = height - (pingValue / maxPing) * height;

            var guideLine = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeService.Current.BorderSubtle)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            PingChart.Children.Insert(0, guideLine);

            var label = new TextBlock
            {
                Text = $"{pingValue:F0}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeService.Current.TextMuted)),
                FontSize = 10,
                FontWeight = FontWeights.Medium
            };

            Canvas.SetRight(label, 4);
            Canvas.SetTop(label, y - 7);
            YAxisCanvas.Children.Add(label);
        }

        // 2. Линия и точки графика ping
        double stepX = records.Count > 1 ? width / (records.Count - 1) : width;

        if (ThemeService.IsOptimizedMode)
        {
            RenderOptions.SetEdgeMode(PingLine, EdgeMode.Aliased);
            PingLine.StrokeThickness = 1.0;
            PingLine.StrokeLineJoin = PenLineJoin.Miter;
            PingFill.Visibility = Visibility.Collapsed;
        }
        else
        {
            RenderOptions.SetEdgeMode(PingLine, EdgeMode.Unspecified);
            PingLine.StrokeThickness = 2.0;
            PingLine.StrokeLineJoin = PenLineJoin.Round;
            PingFill.Visibility = Visibility.Visible;
        }

        if (records.Count == 1)
        {
            var single = records[0];
            bool isTimeout = single.IsTimeout || single.PingMs <= 0;
            double y = isTimeout ? 4.0 : height - (Math.Clamp((double)single.PingMs / maxPing, 0, 1) * (height - 8)) - 4;

            var pt1 = new Point(0, y);
            var pt2 = new Point(width, y);
            PingLine.Points.Add(pt1);
            PingLine.Points.Add(pt2);
            _graphPoints.Add((new Point(width / 2.0, y), single));
        }
        else
        {
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                double x = i * width / (records.Count - 1);
                double y;

                if (record.IsTimeout || record.PingMs <= 0)
                {
                    y = 4.0;
                }
                else
                {
                    double normalized = Math.Clamp((double)record.PingMs / maxPing, 0, 1);
                    y = height - (normalized * (height - 8)) - 4;
                }

                var pt = new Point(x, y);

                if (ThemeService.IsOptimizedMode)
                {
                    // Режим оптимизации: цифровой ступенчатый график телеметрии (PerfMon / Task Manager step line)
                    if (i > 0)
                    {
                        var prevPt = _graphPoints[i - 1].Pos;
                        PingLine.Points.Add(new Point(pt.X, prevPt.Y));
                    }
                    PingLine.Points.Add(pt);
                }
                else
                {
                    if (i > 0)
                    {
                        var prevRecord = records[i - 1];
                        bool prevIsTimeout = prevRecord.IsTimeout || prevRecord.PingMs <= 0;
                        bool currIsTimeout = record.IsTimeout || record.PingMs <= 0;

                        if ((!prevIsTimeout && currIsTimeout) || (prevIsTimeout && !currIsTimeout))
                        {
                            var prevPt = _graphPoints[i - 1].Pos;
                            var curvePts = GenerateBezierPoints(prevPt, pt, 12);
                            for (int k = 1; k < curvePts.Count - 1; k++)
                            {
                                PingLine.Points.Add(curvePts[k]);
                            }
                        }
                    }

                    PingLine.Points.Add(pt);
                }

                _graphPoints.Add((pt, record));
            }
        }

        // 3. Замкнутый полигон градиентной заливки
        if (!ThemeService.IsOptimizedMode && PingLine.Points.Count >= 2)
        {
            PingFill.Points.Add(new Point(0, height));
            foreach (var pt in PingLine.Points)
            {
                PingFill.Points.Add(pt);
            }
            PingFill.Points.Add(new Point(width, height));
        }

        if (records.Count > 0)
        {
            var theme = ThemeService.Current;

            if (ThemeService.IsOptimizedMode)
            {
                // Режим оптимизации: чистая сплошная линия без тяжелых многопроходных градиентов и заливок
                PingFill.Points.Clear();
                PingFill.Fill = Brushes.Transparent;
                PingLine.Effect = null;

                var latestRecord = records.LastOrDefault();
                long lastPing = (latestRecord == null || latestRecord.IsTimeout) ? -1 : latestRecord.PingMs;
                var (colorHex, _) = ThemeService.GetPingColor(lastPing);
                var solidBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                solidBrush.Freeze();
                PingLine.Stroke = solidBrush;
            }
            else
            {
                var greenColor = (Color)ColorConverter.ConvertFromString(theme.AccentGreen);
                var yellowColor = (Color)ColorConverter.ConvertFromString(theme.AccentYellow);
                var redColor = (Color)ColorConverter.ConvertFromString(theme.AccentRed);

            // Точное вычисление относительного вертикального смещения для заданного значения пинга
            double GetOffsetForPing(double pingMs)
            {
                double clampedPing = Math.Clamp(pingMs, 0, maxPing);
                double y = height - ((clampedPing / maxPing) * (height - 8)) - 4;
                return Math.Clamp(1.0 - (y / height), 0.0, 1.0);
            }

            var lineStops = new GradientStopCollection();
            var fillStops = new GradientStopCollection();

            lineStops.Add(new GradientStop(greenColor, 0.0));
            fillStops.Add(new GradientStop(Color.FromArgb(0x00, greenColor.R, greenColor.G, greenColor.B), 0.0));

            double offGreen = GetOffsetForPing(75.0);
            lineStops.Add(new GradientStop(greenColor, offGreen));
            fillStops.Add(new GradientStop(Color.FromArgb(0x18, greenColor.R, greenColor.G, greenColor.B), offGreen));

            if (maxPing > 75.0)
            {
                double offYellowStart = GetOffsetForPing(Math.Min(maxPing, 100.0));
                lineStops.Add(new GradientStop(yellowColor, offYellowStart));
                fillStops.Add(new GradientStop(Color.FromArgb(0x22, yellowColor.R, yellowColor.G, yellowColor.B), offYellowStart));

                if (maxPing > 100.0)
                {
                    double offYellowEnd = GetOffsetForPing(Math.Min(maxPing, 160.0));
                    lineStops.Add(new GradientStop(yellowColor, offYellowEnd));
                    fillStops.Add(new GradientStop(Color.FromArgb(0x25, yellowColor.R, yellowColor.G, yellowColor.B), offYellowEnd));

                    if (maxPing > 160.0)
                    {
                        double offRedStart = GetOffsetForPing(Math.Min(maxPing, 200.0));
                        lineStops.Add(new GradientStop(redColor, offRedStart));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x2A, redColor.R, redColor.G, redColor.B), offRedStart));

                        lineStops.Add(new GradientStop(redColor, 1.0));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x32, redColor.R, redColor.G, redColor.B), 1.0));
                    }
                    else
                    {
                        lineStops.Add(new GradientStop(yellowColor, 1.0));
                        fillStops.Add(new GradientStop(Color.FromArgb(0x25, yellowColor.R, yellowColor.G, yellowColor.B), 1.0));
                    }
                }
                else
                {
                    lineStops.Add(new GradientStop(yellowColor, 1.0));
                    fillStops.Add(new GradientStop(Color.FromArgb(0x22, yellowColor.R, yellowColor.G, yellowColor.B), 1.0));
                }
            }
            else
            {
                lineStops.Add(new GradientStop(greenColor, 1.0));
                fillStops.Add(new GradientStop(Color.FromArgb(0x18, greenColor.R, greenColor.G, greenColor.B), 1.0));
            }

            var lineBrush = new LinearGradientBrush(lineStops, new Point(0, height), new Point(0, 0))
            {
                MappingMode = BrushMappingMode.Absolute
            };
            PingLine.Stroke = lineBrush;

            var fillBrush = new LinearGradientBrush(fillStops, new Point(0, height), new Point(0, 0))
            {
                MappingMode = BrushMappingMode.Absolute
            };
            PingFill.Fill = fillBrush;

            if (!ThemeService.IsOptimizedMode)
            {
                try
                {
                    var glowColor = (Color)ColorConverter.ConvertFromString(theme.AccentColor);
                    if (PingLine.Effect is not System.Windows.Media.Effects.DropShadowEffect dse || dse.Color != glowColor)
                    {
                        var glowEffect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = 12,
                            ShadowDepth = 0,
                            Color = glowColor,
                            Opacity = 0.45
                        };
                        glowEffect.Freeze();
                        PingLine.Effect = glowEffect;
                    }
                }
                catch
                {
                    PingLine.Effect = null;
                }
            }
            else
            {
                PingLine.Effect = null;
            }
            }
        }

        // 4. Отрисовка зон потерь, полос таймаутов и бейджей
        RenderTimeoutVisuals(records, width, height, stepX, validRecords.Count == 0);

        if (_currentMousePosition.HasValue)
        {
            UpdateHoverIndicator();
        }
        else
        {
            UpdateLiveIndicator();
        }
    }

    private static List<Point> GenerateBezierPoints(Point p0, Point p3, int count = 12)
    {
        var points = new List<Point>(count + 1);
        double dx = p3.X - p0.X;
        if (dx <= 0.001)
        {
            points.Add(p0);
            points.Add(p3);
            return points;
        }

        var p1 = new Point(p0.X + 0.45 * dx, p0.Y);
        var p2 = new Point(p3.X - 0.45 * dx, p3.Y);

        for (int k = 0; k <= count; k++)
        {
            double t = (double)k / count;
            double u = 1.0 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            double x = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
            double y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private void RenderTimeoutVisuals(
        List<PingRecord> records,
        double width,
        double height,
        double stepX,
        bool allTimeouts)
    {
        if (records.Count == 0) return;

        int i = 0;
        double lastBadgeRight = -100;
        int fillIndex = PingChart.Children.IndexOf(PingFill);
        if (fillIndex < 0) fillIndex = 0;
        else fillIndex++; // Вставляем подсветку поверх основной заливки графика, но под линиями

        while (i < records.Count)
        {
            if (records[i].IsTimeout || records[i].PingMs <= 0)
            {
                int startIdx = i;
                while (i < records.Count && (records[i].IsTimeout || records[i].PingMs <= 0))
                {
                    i++;
                }
                int endIdx = i - 1;

                bool isLatest = (endIdx == records.Count - 1);

                // Координаты горизонтальной линии таймаута на потолке графика (y = 4.0)
                double capX1 = startIdx == 0 ? 0 : _graphPoints[startIdx].Pos.X;
                double capX2 = isLatest ? width : _graphPoints[endIdx].Pos.X;

                if (ThemeService.IsOptimizedMode)
                {
                    // РЕЖИМ ОПТИМИЗАЦИИ: плоская четкая колонка таймаута без кривых Безье, градиентов и тяжелых OpacityMask
                    double colWidth = Math.Max(2.0, capX2 - capX1);
                    var optRect = new Rectangle
                    {
                        Width = colWidth,
                        Height = height,
                        Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x61, 0x61)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(optRect, capX1);
                    Canvas.SetTop(optRect, 0);
                    PingChart.Children.Insert(fillIndex, optRect);
                    fillIndex++;

                    var optLine = new Line
                    {
                        X1 = capX1,
                        Y1 = 4.0,
                        X2 = Math.Max(capX2, capX1 + 2.0),
                        Y2 = 4.0,
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                        StrokeThickness = 2.0,
                        IsHitTestVisible = false
                    };
                    PingChart.Children.Add(optLine);
                }
                else
                {
                    // ОБЫЧНЫЙ РЕЖИМ: плавные кривые Безье, градиентный фон и размытая маска затухания
                    List<Point>? entranceCurve = null;
                    if (startIdx > 0 && startIdx - 1 < _graphPoints.Count && startIdx < _graphPoints.Count)
                    {
                        var prevPt = _graphPoints[startIdx - 1].Pos;
                        var currPt = _graphPoints[startIdx].Pos;
                        entranceCurve = GenerateBezierPoints(prevPt, currPt, 12);
                    }

                    List<Point>? exitCurve = null;
                    if (!isLatest && endIdx + 1 < _graphPoints.Count && endIdx < _graphPoints.Count)
                    {
                        var currPt = _graphPoints[endIdx].Pos;
                        var nextPt = _graphPoints[endIdx + 1].Pos;
                        exitCurve = GenerateBezierPoints(currPt, nextPt, 12);
                    }

                    // 1. Мягкая фоновая полупрозрачная подсветка без жестких вертикальных рамок
                    var timeoutBgPolygon = new Polygon
                    {
                        StrokeThickness = 0,
                        IsHitTestVisible = false,
                        Fill = new LinearGradientBrush(
                            new GradientStopCollection
                            {
                                new GradientStop((Color)ColorConverter.ConvertFromString("#28FF6161"), 0),
                                new GradientStop((Color)ColorConverter.ConvertFromString("#10FF6161"), 0.5),
                                new GradientStop((Color)ColorConverter.ConvertFromString("#00FF6161"), 1.0)
                            },
                            new Point(0, 0),
                            new Point(0, 1)
                        )
                    };

                    // Нижний левый угол полигона подсветки
                    double bottomStartX = (entranceCurve != null && entranceCurve.Count > 0)
                        ? entranceCurve[0].X
                        : capX1;
                    timeoutBgPolygon.Points.Add(new Point(bottomStartX, height));

                    if (entranceCurve != null)
                    {
                        foreach (var pt in entranceCurve)
                        {
                            timeoutBgPolygon.Points.Add(pt);
                        }
                    }
                    else
                    {
                        timeoutBgPolygon.Points.Add(new Point(capX1, 4.0));
                    }

                    if (capX2 > capX1)
                    {
                        timeoutBgPolygon.Points.Add(new Point(capX2, 4.0));
                    }

                    if (exitCurve != null)
                    {
                        foreach (var pt in exitCurve)
                        {
                            timeoutBgPolygon.Points.Add(pt);
                        }
                        timeoutBgPolygon.Points.Add(new Point(exitCurve.Last().X, height));
                    }
                    else
                    {
                        timeoutBgPolygon.Points.Add(new Point(capX2, height));
                    }

                    // Плавное затухание по краям подсветки
                    var maskStops = new GradientStopCollection();
                    if (startIdx > 0)
                    {
                        maskStops.Add(new GradientStop(Colors.Transparent, 0.0));
                        maskStops.Add(new GradientStop(Colors.White, 0.25));
                    }
                    else
                    {
                        maskStops.Add(new GradientStop(Colors.White, 0.0));
                    }

                    if (!isLatest)
                    {
                        maskStops.Add(new GradientStop(Colors.White, 0.75));
                        maskStops.Add(new GradientStop(Colors.Transparent, 1.0));
                    }
                    else
                    {
                        maskStops.Add(new GradientStop(Colors.White, 1.0));
                    }

                    timeoutBgPolygon.OpacityMask = new LinearGradientBrush(maskStops, new Point(0, 0), new Point(1, 0));

                    PingChart.Children.Insert(fillIndex, timeoutBgPolygon);
                    fillIndex++;

                    // 2. Единая непрерывная плавная красная линия таймаута (вход, потолок, выход)
                    var timeoutPath = new Polyline
                    {
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                        StrokeThickness = 2.5,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false
                    };

                    if (entranceCurve != null)
                    {
                        foreach (var pt in entranceCurve)
                        {
                            timeoutPath.Points.Add(pt);
                        }
                    }
                    else
                    {
                        timeoutPath.Points.Add(new Point(capX1, 4.0));
                    }

                    if (capX2 > capX1)
                    {
                        timeoutPath.Points.Add(new Point(capX2, 4.0));
                    }

                    if (exitCurve != null)
                    {
                        foreach (var pt in exitCurve)
                        {
                            timeoutPath.Points.Add(pt);
                        }
                    }

                    if (timeoutPath.Points.Count == 1)
                    {
                        var apexDot = new Ellipse
                        {
                            Width = 5,
                            Height = 5,
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(apexDot, capX1 - 2.5);
                        Canvas.SetTop(apexDot, 1.5);
                        PingChart.Children.Add(apexDot);
                    }
                    else
                    {
                        PingChart.Children.Add(timeoutPath);
                    }
                }

                // 5. Красный бейдж "-- ms"
                double centerX = (capX1 + capX2) / 2.0;
                double zoneWidth = capX2 - capX1;

                if (!allTimeouts && (zoneWidth >= 16 || isLatest || (centerX - 22 > lastBadgeRight)))
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D1013")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#90FF6161")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        IsHitTestVisible = false
                    };

                    var badgeStack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    if (isLatest)
                    {
                        badgeStack.Children.Add(new Ellipse
                        {
                            Width = 5,
                            Height = 5,
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                            Margin = new Thickness(0, 0, 4, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    badgeStack.Children.Add(new TextBlock
                    {
                        Text = "-- ms",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Consolas, Segoe UI"),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    badge.Child = badgeStack;
                    badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    double badgeW = badge.DesiredSize.Width > 0 ? badge.DesiredSize.Width : 38;
                    double badgeX = centerX - badgeW / 2.0;
                    badgeX = Math.Clamp(badgeX, 2, width - badgeW - 2);

                    Canvas.SetLeft(badge, badgeX);
                    Canvas.SetTop(badge, 6);
                    PingChart.Children.Add(badge);

                    lastBadgeRight = badgeX + badgeW + 6;
                }
            }
            else
            {
                i++;
            }
        }

        // Если все замеры в интервале — сплошные таймауты
        if (allTimeouts)
        {
            var emptyAlert = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6150C0E")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FF6161")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                IsHitTestVisible = false
            };

            var emptyStack = new StackPanel { Orientation = Orientation.Horizontal };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "✕",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text = LocalizationService.IsRussian ? "Нет ответа от сервера (-- ms) • Потери 100%" : "No response from server (-- ms) • 100% loss",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6161")),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            emptyAlert.Child = emptyStack;
            emptyAlert.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(emptyAlert, Math.Max(10, (width - emptyAlert.DesiredSize.Width) / 2.0));
            Canvas.SetTop(emptyAlert, Math.Max(10, (height - emptyAlert.DesiredSize.Height) / 2.0));
            PingChart.Children.Add(emptyAlert);
        }
    }

    // =========================================================
    // СЕКЦИЯ СКРОЛЛА ИСТОРИИ ГРАФИКА
    // =========================================================

    private void ChartScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingScrollBar) return;
        if (_allSessionRecords.Count == 0) return;

        double max = ChartScrollBar.Maximum;
        double currentVal = e.NewValue;

        if (max > 0 && currentVal >= max - 0.5)
        {
            _isChartLive = true;
        }
        else
        {
            _isChartLive = false;
        }

        int total = _allSessionRecords.Count;
        int windowSize = CurrentWindowSize;
        int maxScroll = Math.Max(0, total - windowSize);
        int startIndex = (int)Math.Round(Math.Clamp(currentVal, 0, maxScroll));
        int count = Math.Min(windowSize, total - startIndex);

        var recordsToShow = _allSessionRecords.Skip(startIndex).Take(count).ToList();

        if (_isChartLive)
        {
            UpdateLiveHeaderAndBadge();
        }
        else
        {
            UpdateHistoryHeaderAndBadge(recordsToShow);
        }

        DrawGraph(recordsToShow);
    }

    private void PingChart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ChartScrollBar.IsEnabled || ChartScrollBar.Maximum <= 0) return;

        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 15 : 5;
        double delta = (e.Delta > 0 ? -1 : 1) * step;

        double targetValue = Math.Clamp(ChartScrollBar.Value + delta, 0, ChartScrollBar.Maximum);
        ChartScrollBar.Value = targetValue;
        e.Handled = true;
    }

    private void LiveReturnBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ReturnToLive();
    }

    private void ReturnToLive()
    {
        _isChartLive = true;
        if (ChartScrollBar.Maximum > 0)
        {
            ChartScrollBar.Value = ChartScrollBar.Maximum;
        }
        UpdateChartDisplay();
    }

    private void UpdateLiveHeaderAndBadge()
    {
        LiveReturnBadge.Visibility = Visibility.Collapsed;
        ChartSubtitleText.Text = _viewModel.ChartSubtitleText;
    }

    private void UpdateHistoryHeaderAndBadge(List<PingRecord> recordsToShow)
    {
        LiveReturnBadge.Visibility = Visibility.Visible;

        if (recordsToShow.Count > 0)
        {
            var firstTime = recordsToShow.First().Timestamp;
            var lastTime = recordsToShow.Last().Timestamp;
            var diff = DateTime.Now - lastTime;
            string ago = FormatAgo(diff);
            ChartSubtitleText.Text = $"{firstTime:HH:mm:ss} — {lastTime:HH:mm:ss} ({ago})";
        }
    }

    private static string FormatAgo(TimeSpan diff)
    {
        if (LocalizationService.IsRussian)
        {
            if (diff.TotalSeconds < 5) return "сейчас";
            if (diff.TotalMinutes < 1) return $"{(int)Math.Round(diff.TotalSeconds)}с назад";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}м {diff.Seconds}с назад";
            return $"{(int)diff.TotalHours}ч {diff.Minutes}м назад";
        }
        else
        {
            if (diff.TotalSeconds < 5) return "now";
            if (diff.TotalMinutes < 1) return $"{(int)Math.Round(diff.TotalSeconds)}s ago";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m {diff.Seconds}s ago";
            return $"{(int)diff.TotalHours}h {diff.Minutes}m ago";
        }
    }

    // =========================================================
    // HOVER ИНТЕРАКТИВНОСТЬ ГРАФИКА
    // =========================================================

    private void PingChart_MouseMove(object sender, MouseEventArgs e)
    {
        _currentMousePosition = e.GetPosition(PingChart);
        _viewModel.UpdateChartLiveStatus(isHovering: true);
        UpdateHoverIndicator();
    }

    private void PingChart_MouseEnter(object sender, MouseEventArgs e)
    {
        _currentMousePosition = e.GetPosition(PingChart);
        _viewModel.UpdateChartLiveStatus(isHovering: true);
        UpdateHoverIndicator();
    }

    private void PingChart_MouseLeave(object sender, MouseEventArgs e)
    {
        _currentMousePosition = null;
        HideHoverIndicator();
        _viewModel.UpdateChartLiveStatus(isHovering: false);
    }

    private void PingChart_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lastRecords.Count > 0)
        {
            DrawGraph(_lastRecords);
        }
    }

    private PingRecord? _currentHoveredRecord;

    private void HideHoverIndicator()
    {
        HoverLine.Visibility = Visibility.Collapsed;
        HoverDotGlow.Visibility = Visibility.Collapsed;
        HoverDot.Visibility = Visibility.Collapsed;
        HoverHeaderBadge.Visibility = Visibility.Collapsed;
        UpdateLiveIndicator();
    }

    private void UpdateLiveIndicator()
    {
        var record = _lastRecords.LastOrDefault();
        _currentHoveredRecord = record;

        if (_isChartLive)
        {
            ChartInfoModeText.Text = LocalizationService.IsRussian ? "В ЭФИРЕ" : "LIVE";
            ChartInfoModeText.Foreground = (Brush)FindResource("TextMuted");
        }
        else
        {
            ChartInfoModeText.Text = LocalizationService.IsRussian ? "ИСТОРИЯ" : "HISTORY";
            ChartInfoModeText.Foreground = (Brush)FindResource("AccentBlue");
        }

        if (record == null)
        {
            ChartInfoTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
            ChartInfoAgoText.Text = _isChartLive ? "(live)" : "";
            ChartInfoPingText.Text = "-- ms";
            ChartInfoPingText.Foreground = (Brush)FindResource("TextMuted");
            ChartInfoPingDot.Fill = (Brush)FindResource("TextMuted");
            ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Ожидание" : "Waiting");
            ChartInfoStatusText.Foreground = (Brush)FindResource("TextMuted");
            ChartInfoRouterText.Text = "—";
            ChartInfoRouterText.Foreground = (Brush)FindResource("TextMuted");
            ChartInfoIspText.Text = "—";
            ChartInfoIspText.Foreground = (Brush)FindResource("TextMuted");
            return;
        }

        ChartInfoTimeText.Text = record.Timestamp.ToString("HH:mm:ss");
        if (_isChartLive)
        {
            ChartInfoAgoText.Text = "(live)";
        }
        else
        {
            var diff = DateTime.Now - record.Timestamp;
            ChartInfoAgoText.Text = $"({FormatAgo(diff)})";
        }

        var (colorHex, _) = ThemeService.GetPingColor(record.IsTimeout ? -1 : record.PingMs);
        var mainBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

        if (record.IsTimeout || record.PingMs <= 0)
        {
            ChartInfoPingText.Text = "-- ms";
            ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Таймаут" : "Timeout");
            ChartInfoStatusText.Foreground = mainBrush;
        }
        else
        {
            ChartInfoPingText.Text = $"{record.PingMs} ms";
            if (record.PingMs > 200)
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Высокий" : "High");
                ChartInfoStatusText.Foreground = mainBrush;
            }
            else if (record.PingMs > 100 || record.IsSpike)
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Скачок" : "Spike");
                ChartInfoStatusText.Foreground = mainBrush;
            }
            else
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Стабильно" : "Stable");
                ChartInfoStatusText.Foreground = (Brush)FindResource("TextSecondary");
            }
        }

        ChartInfoPingText.Foreground = mainBrush;
        ChartInfoPingDot.Fill = mainBrush;

        UpdateSegmentationInfo(record);
    }

    private void UpdateHoverIndicator()
    {
        if (!_currentMousePosition.HasValue || _graphPoints.Count == 0)
        {
            HideHoverIndicator();
            return;
        }

        double mouseX = _currentMousePosition.Value.X;
        double mouseY = _currentMousePosition.Value.Y;
        double chartWidth = PingChart.ActualWidth;
        double chartHeight = PingChart.ActualHeight;

        if (chartWidth <= 0 || chartHeight <= 0)
        {
            HideHoverIndicator();
            return;
        }

        if (mouseX < 0 || mouseX > chartWidth || mouseY < 0 || mouseY > chartHeight)
        {
            HideHoverIndicator();
            return;
        }

        var closest = _graphPoints
            .OrderBy(p => Math.Abs(p.Pos.X - mouseX))
            .First();

        var pt = closest.Pos;
        var record = closest.Record;

        // 1. Вертикальная пунктирная линия шкалы времени
        HoverLine.X1 = pt.X;
        HoverLine.Y1 = 0;
        HoverLine.X2 = pt.X;
        HoverLine.Y2 = chartHeight;
        HoverLine.Stroke = (record.IsTimeout || record.PingMs <= 0)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FF6161"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353C46"));
        HoverLine.Visibility = Visibility.Visible;

        // 2. Цвета точки и ореола в соответствии со статусом задержки
        var (colorHex, softColorHex) = ThemeService.GetPingColor(record.IsTimeout ? -1 : record.PingMs);
        var mainBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        var softBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(softColorHex));

        HoverDot.Fill = mainBrush;
        Canvas.SetLeft(HoverDot, pt.X - HoverDot.Width / 2);
        Canvas.SetTop(HoverDot, pt.Y - HoverDot.Height / 2);
        HoverDot.Visibility = Visibility.Visible;

        if (ThemeService.IsOptimizedMode)
        {
            HoverDotGlow.Visibility = Visibility.Collapsed;
        }
        else
        {
            HoverDotGlow.Fill = softBrush;
            HoverDotGlow.Stroke = mainBrush;
            Canvas.SetLeft(HoverDotGlow, pt.X - HoverDotGlow.Width / 2);
            Canvas.SetTop(HoverDotGlow, pt.Y - HoverDotGlow.Height / 2);
            HoverDotGlow.Visibility = Visibility.Visible;
        }

        // 3. Обновление информационной панели слева (график не перекрывается!)
        UpdateHoveredRecordInfo(record);

        // 4. Бейдж в заголовке графика
        HoverHeaderText.Text = (record.IsTimeout || record.PingMs <= 0)
            ? $"{record.Timestamp:HH:mm:ss} — -- ms ({(LocalizationService.IsRussian ? "Таймаут" : "Timeout")})"
            : $"{record.Timestamp:HH:mm:ss} — {record.PingMs} ms";
        HoverHeaderBadge.Visibility = Visibility.Visible;
    }

    private void UpdateHoveredRecordInfo(PingRecord record)
    {
        _currentHoveredRecord = record;

        ChartInfoModeText.Text = LocalizationService.IsRussian ? "ТОЧКА" : "POINT";
        ChartInfoModeText.Foreground = (Brush)FindResource("AccentBlue");

        ChartInfoTimeText.Text = record.Timestamp.ToString("HH:mm:ss");

        var diff = DateTime.Now - record.Timestamp;
        if (diff.TotalSeconds < 3)
            ChartInfoAgoText.Text = LocalizationService.IsRussian ? "(сейчас)" : "(now)";
        else if (diff.TotalMinutes < 1)
            ChartInfoAgoText.Text = $"({(int)Math.Round(diff.TotalSeconds)}{(LocalizationService.IsRussian ? "с назад" : "s ago")})";
        else if (diff.TotalHours < 1)
            ChartInfoAgoText.Text = diff.Seconds > 0
                ? $"({(int)diff.TotalMinutes}{(LocalizationService.IsRussian ? "м" : "m")} {diff.Seconds}{(LocalizationService.IsRussian ? "с" : "s")})"
                : $"({(int)diff.TotalMinutes}{(LocalizationService.IsRussian ? "м" : "m")})";
        else
            ChartInfoAgoText.Text = $"({(int)diff.TotalHours}{(LocalizationService.IsRussian ? "ч" : "h")} {diff.Minutes}{(LocalizationService.IsRussian ? "м" : "m")})";

        var (colorHex, _) = ThemeService.GetPingColor(record.IsTimeout ? -1 : record.PingMs);
        var mainBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

        if (record.IsTimeout || record.PingMs <= 0)
        {
            ChartInfoPingText.Text = "-- ms";
            ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Таймаут" : "Timeout");
            ChartInfoStatusText.Foreground = mainBrush;
        }
        else
        {
            ChartInfoPingText.Text = $"{record.PingMs} ms";
            if (record.PingMs > 200)
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Высокий" : "High");
                ChartInfoStatusText.Foreground = mainBrush;
            }
            else if (record.PingMs > 100 || record.IsSpike)
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Скачок" : "Spike");
                ChartInfoStatusText.Foreground = mainBrush;
            }
            else
            {
                ChartInfoStatusText.Text = "• " + (LocalizationService.IsRussian ? "Стабильно" : "Stable");
                ChartInfoStatusText.Foreground = (Brush)FindResource("TextSecondary");
            }
        }

        ChartInfoPingText.Foreground = mainBrush;
        ChartInfoPingDot.Fill = mainBrush;

        UpdateSegmentationInfo(record);
    }

    private void UpdateSegmentationInfo(PingRecord record)
    {
        if (record.RouterPingMs >= 0)
        {
            ChartInfoRouterText.Text = $"{record.RouterPingMs} ms";
            if (record.RouterPingMs > 200)
                ChartInfoRouterText.Foreground = (Brush)FindResource("AccentRed");
            else if (record.RouterPingMs > 100)
                ChartInfoRouterText.Foreground = (Brush)FindResource("AccentYellow");
            else
                ChartInfoRouterText.Foreground = (Brush)FindResource("AccentGreen");
        }
        else
        {
            ChartInfoRouterText.Text = "—";
            ChartInfoRouterText.Foreground = (Brush)FindResource("TextMuted");
        }

        if (record.IspPingMs >= 0)
        {
            ChartInfoIspText.Text = $"{record.IspPingMs} ms";
            if (record.IspPingMs > 200)
                ChartInfoIspText.Foreground = (Brush)FindResource("AccentRed");
            else if (record.IspPingMs > 100)
                ChartInfoIspText.Foreground = (Brush)FindResource("AccentYellow");
            else
                ChartInfoIspText.Foreground = (Brush)FindResource("AccentGreen");
        }
        else
        {
            ChartInfoIspText.Text = record.RouterPingMs >= 0 
                ? (LocalizationService.IsRussian ? "норм" : "OK") 
                : "—";
            ChartInfoIspText.Foreground = record.RouterPingMs >= 0
                ? (Brush)FindResource("AccentGreen")
                : (Brush)FindResource("TextMuted");
        }

    }

    private void OpenDiagnosticsForRecord(PingRecord record)
    {
        string targetIp = _viewModel.SelectedPingTarget?.Ip ?? "8.8.8.8";
        string targetName = _viewModel.SelectedPingTarget?.DisplayName ?? _viewModel.SelectedPingTarget?.Name ?? "Google DNS (8.8.8.8)";
        var incidentWin = new IncidentDiagnosticsWindow(
            record,
            targetIp,
            targetName,
            _viewModel.NetworkDiagnostic)
        {
            Owner = this
        };
        incidentWin.ShowDialog();
    }

    private void ChartInfoDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        var record = _currentHoveredRecord ?? _lastRecords.LastOrDefault();
        if (record != null)
        {
            OpenDiagnosticsForRecord(record);
        }
        else
        {
            _viewModel.OpenMtrDiagnostics();
        }
    }

    private void ChartInfoPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var record = _currentHoveredRecord ?? _lastRecords.LastOrDefault();
        if (record != null)
        {
            OpenDiagnosticsForRecord(record);
        }
        else
        {
            _viewModel.OpenMtrDiagnostics();
        }
    }

    private void PingChart_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_graphPoints.Count == 0) return;

        var pos = e.GetPosition(PingChart);
        var closest = _graphPoints
            .OrderBy(p => Math.Abs(p.Pos.X - pos.X))
            .FirstOrDefault();

        if (closest.Record != null)
        {
            OpenDiagnosticsForRecord(closest.Record);
        }
    }
}