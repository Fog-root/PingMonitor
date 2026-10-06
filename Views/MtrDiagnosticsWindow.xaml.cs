using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;
using DotaPingMonitor.ViewModels;

namespace DotaPingMonitor.Views;

public partial class MtrDiagnosticsWindow : Window
{
    private readonly string _targetHost;
    private readonly string _targetName;
    private readonly MtrService _mtrService = new();
    private readonly ObservableCollection<MtrHopResult> _hops = new();
    private MtrReport? _lastReport;
    private double _lastElapsedSeconds;
    private CancellationTokenSource? _cts;

    public MtrDiagnosticsWindow(string targetHost, string targetName)
    {
        InitializeComponent();
        _targetHost = targetHost;
        _targetName = targetName;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Closed += (s, e) =>
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            _cts?.Cancel();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        HopsListView.ItemsSource = _hops;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        await StartDiagnosticsAsync();
    }

    private void OnLanguageChanged(string lang)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();

            foreach (var hop in _hops)
            {
                MtrService.RefreshHopLocalization(hop, _targetName);
            }

            if (_lastReport != null)
            {
                MtrService.ComputeVerdict(_lastReport);
                DisplayVerdict(_lastReport, _lastElapsedSeconds);
            }
        });
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.IsRussian
            ? "Диагностика сетевых узлов (MTR)"
            : "Network Node Diagnostics (MTR)";

        MtrHeaderTitleText.Text = LocalizationService.IsRussian
            ? "ДИАГНОСТИКА СЕТЕВЫХ УЗЛОВ И МАРШРУТА"
            : "NETWORK ROUTE & NODE DIAGNOSTICS";

        string localizedTarget = PingTargetItemViewModel.GetLocalizedName(_targetName);
        TargetSubtitleText.Text = LocalizationService.IsRussian
            ? $"Трассировка до {localizedTarget} ({_targetHost})"
            : $"Tracing to {localizedTarget} ({_targetHost})";

        CloseButton.ToolTip = LocalizationService.IsRussian ? "Закрыть (Esc)" : "Close (Esc)";

        ColHopText.Text = LocalizationService.IsRussian ? "ХОП" : "HOP";
        ColNodeText.Text = LocalizationService.IsRussian ? "УЗЕЛ СЕТИ" : "NETWORK NODE";
        ColIpText.Text = LocalizationService.IsRussian ? "IP АДРЕС / PTR ИМЯ" : "IP ADDRESS / PTR NAME";
        ColPingText.Text = LocalizationService.IsRussian ? "ОТКЛИК" : "PING";
        ColLossText.Text = LocalizationService.IsRussian ? "ПОТЕРИ" : "LOSS";
        ColStatusText.Text = LocalizationService.IsRussian ? "СОСТОЯНИЕ" : "STATUS";

        CopyReportBtnText.Text = LocalizationService.IsRussian ? "Скопировать отчёт" : "Copy report";
        CopyReportButton.ToolTip = LocalizationService.IsRussian
            ? "Скопировать детальный MTR-отчёт для техподдержки"
            : "Copy detailed MTR report for tech support";

        RestartBtnText.Text = LocalizationService.IsRussian ? "Повторить" : "Restart";
        CloseBtnText.Text = LocalizationService.IsRussian ? "Закрыть" : "Close";
    }

    private async Task StartDiagnosticsAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _hops.Clear();
        _lastReport = null;
        MtrProgressBar.Visibility = Visibility.Visible;
        MtrProgressBar.IsIndeterminate = true;
        RestartButton.IsEnabled = false;

        VerdictTitleText.Text = LocalizationService.IsRussian
            ? "Опрашиваем сетевые узлы..."
            : "Polling network nodes...";
        VerdictDescText.Text = LocalizationService.IsRussian
            ? "Замеряем задержку и потери пакетов на каждом промежуточном шлюзе..."
            : "Measuring latency and packet loss on each intermediate gateway...";
        VerdictIconText.Text = "⏳";
        SetVerdictStyle("#57C1FF", "#1857C1FF", "#3357C1FF");

        StatusFooterText.Text = LocalizationService.IsRussian
            ? "Идёт опрос промежуточных узлов..."
            : "Polling intermediate nodes...";
        var sw = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<MtrHopResult>(hop =>
            {
                _hops.Add(hop);
                StatusFooterText.Text = LocalizationService.IsRussian
                    ? $"Проверено узлов: {_hops.Count}... (Хоп #{hop.HopIndex}: {hop.CategoryName})"
                    : $"Hops checked: {_hops.Count}... (Hop #{hop.HopIndex}: {hop.CategoryName})";
                if (HopsListView.Items.Count > 0)
                {
                    HopsListView.ScrollIntoView(HopsListView.Items[^1]);
                }
            });

            _lastReport = await _mtrService.RunDiagnosticsAsync(
                _targetHost,
                _targetName,
                progress,
                _cts.Token);

            sw.Stop();
            _lastElapsedSeconds = sw.Elapsed.TotalSeconds;
            DisplayVerdict(_lastReport, _lastElapsedSeconds);
        }
        catch (OperationCanceledException)
        {
            StatusFooterText.Text = LocalizationService.IsRussian
                ? "Диагностика остановлена пользователем"
                : "Diagnostics canceled by user";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = LocalizationService.IsRussian
                ? $"Ошибка: {ex.Message}"
                : $"Error: {ex.Message}";
            VerdictTitleText.Text = LocalizationService.IsRussian
                ? "✕ Ошибка выполнения MTR"
                : "✕ MTR Execution Error";
            VerdictDescText.Text = ex.Message;
            VerdictIconText.Text = "✕";
            SetVerdictStyle("#FF6161", "#18FF6161", "#33FF6161");
        }
        finally
        {
            MtrProgressBar.Visibility = Visibility.Collapsed;
            RestartButton.IsEnabled = true;
        }
    }

    private void DisplayVerdict(MtrReport report, double elapsedSeconds)
    {
        VerdictTitleText.Text = report.VerdictTitle;
        VerdictDescText.Text = report.VerdictDescription;
        VerdictIconText.Text = report.VerdictIcon;

        string borderHex = report.VerdictColor;
        string bgHex = report.VerdictBg;
        SetVerdictStyle(borderHex, bgHex, borderHex);

        StatusFooterText.Text = LocalizationService.IsRussian
            ? $"Диагностика завершена за {elapsedSeconds:F1} сек. Всего узлов: {_hops.Count}."
            : $"Diagnostics completed in {elapsedSeconds:F1} sec. Total hops: {_hops.Count}.";
    }

    private void SetVerdictStyle(string colorHex, string bgHex, string borderHex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var bg = (Color)ColorConverter.ConvertFromString(bgHex);
            var border = (Color)ColorConverter.ConvertFromString(borderHex);

            VerdictBorder.Background = new SolidColorBrush(bg);
            VerdictBorder.BorderBrush = new SolidColorBrush(border);

            VerdictIconContainer.Background = new SolidColorBrush(bg);
            VerdictIconContainer.BorderBrush = new SolidColorBrush(border);
            VerdictIconText.Foreground = new SolidColorBrush(color);
        }
        catch
        {
            // fallback
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartDiagnosticsAsync();
    }

    private async void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport == null && _hops.Count == 0)
        {
            return;
        }

        var report = _lastReport ?? new MtrReport
        {
            TargetName = _targetName,
            TargetIp = _targetHost,
            Hops = _hops.ToList()
        };

        string text = MtrService.FormatReportText(report);
        Clipboard.SetText(text);

        CopyReportBtnText.Text = LocalizationService.IsRussian ? "✓ Скопировано в буфер!" : "✓ Copied to clipboard!";
        CopyReportButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#59D499"));

        await Task.Delay(1500);

        CopyReportBtnText.Text = LocalizationService.IsRussian ? "Скопировать отчёт" : "Copy report";
        CopyReportButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A8B4"));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
