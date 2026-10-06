using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Views;

public partial class IncidentDiagnosticsWindow : Window
{
    private readonly PingRecord _record;
    private readonly string _targetHost;
    private readonly string _targetName;
    private readonly NetworkDiagnosticService _diagService;

    public IncidentDiagnosticsWindow(
        PingRecord record,
        string targetHost,
        string targetName,
        NetworkDiagnosticService diagService)
    {
        InitializeComponent();

        _record = record;
        _targetHost = targetHost;
        _targetName = targetName;
        _diagService = diagService;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        ApplyStaticLocalization();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= OnLanguageChanged;

        Loaded += async (_, _) => await LoadDiagnosticsAsync();
    }

    private void OnLanguageChanged(string _)
    {
        Dispatcher.Invoke(async () =>
        {
            ApplyStaticLocalization();
            await LoadDiagnosticsAsync();
        });
    }

    private void ApplyStaticLocalization()
    {
        bool isRu = LocalizationService.IsRussian;
        Title = isRu ? "Анализ точки на графике" : "Network Incident Analysis";
        HeaderTitleText.Text = isRu ? "АНАЛИЗ ИНЦИДЕНТА ЗАДЕРЖКИ СЕТИ" : "NETWORK LATENCY INCIDENT ANALYSIS";
        CloseButton.ToolTip = isRu ? "Закрыть (Esc)" : "Close (Esc)";
        SampleBadgeLabel.Text = isRu ? "ЗАМЕР" : "SAMPLE";
        SectionBreakdownTitle.Text = isRu ? "РАЗБИВКА СЕТЕВОГО МАРШРУТА ПО СЕГМЕНТАМ" : "NETWORK ROUTE BREAKDOWN BY SEGMENTS";
        Seg1Title.Text = isRu ? "1. ДОМАШНИЙ WI-FI / РОУТЕР" : "1. HOME WI-FI / ROUTER";
        Seg2Title.Text = isRu ? "2. ГОРОДСКОЙ ИНТЕРНЕТ-ПРОВАЙДЕР" : "2. INTERNET SERVICE PROVIDER (ISP)";
        Seg3Title.Text = isRu ? "3. МАГИСТРАЛЬНЫЙ ТРАНЗИТ / СЕРВЕР" : "3. TRANSIT BACKBONE / SERVER";
        RunMtrBtnText.Text = isRu ? "Подробная MTR трассировка всех узлов" : "Detailed MTR trace of all nodes";
        RunMtrButton.ToolTip = isRu ? "Запустить пошаговый опрос каждого узла на пути следования пакетов" : "Run step-by-step trace of every node along the packet route";
        DismissBtnText.Text = isRu ? "Закрыть" : "Close";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RunMtrButton_Click(object sender, RoutedEventArgs e)
    {
        var mtrWin = new MtrDiagnosticsWindow(_targetHost, _targetName)
        {
            Owner = this.Owner ?? this
        };
        Close();
        mtrWin.ShowDialog();
    }

    private async Task LoadDiagnosticsAsync()
    {
        ApplyStaticLocalization();
        bool isRu = LocalizationService.IsRussian;
        string displayTargetName = _targetName;
        if (!isRu && displayTargetName.StartsWith("Россия", StringComparison.OrdinalIgnoreCase))
        {
            displayTargetName = "Russia" + displayTargetName.Substring(6);
        }

        TimestampSubtitle.Text = isRu
            ? $"Замер от {_record.Timestamp:HH:mm:ss} • Цель: {displayTargetName} ({_targetHost})"
            : $"Sample at {_record.Timestamp:HH:mm:ss} • Target: {displayTargetName} ({_targetHost})";

        string pingStr = _record.IsTimeout || _record.PingMs <= 0 ? "-- ms" : $"{_record.PingMs} ms";
        PingBadgeText.Text = pingStr;

        NetworkDiagnosticResult res;

        // Если в записи уже зафиксированы данные сегментации за этот момент
        if (_record.RouterPingMs >= 0)
        {
            res = _diagService.AnalyzeIncident(
                _record.PingMs,
                _record.RouterPingMs,
                _record.IspPingMs,
                _record.IsTimeout,
                _record.IsSpike);

            res.RouterIp = !string.IsNullOrEmpty(_record.RouterIp) ? _record.RouterIp : _diagService.RouterIp;
            res.IspIp = !string.IsNullOrEmpty(_record.IspIp) ? _record.IspIp : _diagService.IspIp;
        }
        else
        {
            // Выполняем экспресс-тест маршрута для локализации
            res = await _diagService.PerformLiveProbeAsync(_targetHost, _record.PingMs, _record.IsTimeout);
        }

        ApplyDiagnosisToUi(res);
    }

    private void ApplyDiagnosisToUi(NetworkDiagnosticResult res)
    {
        bool isRu = LocalizationService.IsRussian;
        string displayTargetName = _targetName;
        if (!isRu && displayTargetName.StartsWith("Россия", StringComparison.OrdinalIgnoreCase))
        {
            displayTargetName = "Russia" + displayTargetName.Substring(6);
        }

        // 1. Карточка вердикта
        VerdictTitleText.Text = res.VerdictTitle;
        VerdictDescriptionText.Text = res.VerdictDescription;
        VerdictIconText.Text = res.VerdictIcon;

        var vColor = (Color)ColorConverter.ConvertFromString(res.VerdictColor);
        var vBg = (Color)ColorConverter.ConvertFromString(res.VerdictBg);

        VerdictIconText.Foreground = new SolidColorBrush(vColor);
        VerdictIconContainer.Background = new SolidColorBrush(vBg);
        VerdictIconContainer.BorderBrush = new SolidColorBrush(vColor);
        VerdictCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, vColor.R, vColor.G, vColor.B));

        PingBadgeText.Foreground = new SolidColorBrush(vColor);

        // 2. Сегмент 1: Роутер
        RouterIpText.Text = isRu ? $"Шлюз домашней сети: {res.RouterIp}" : $"Home network gateway: {res.RouterIp}";
        if (res.RouterPingMs >= 0)
        {
            RouterPingText.Text = $"{res.RouterPingMs} ms";
            if (res.RouterPingMs > 200)
            {
                SetSegmentAlert(RouterRowBorder, RouterStatusBadge, RouterStatusBadgeText, RouterPingText,
                    isRu ? "⚠ Высокий пинг" : "⚠ High ping", "#FF6161", "#18FF6161");
            }
            else if (res.RouterPingMs > 100)
            {
                SetSegmentAlert(RouterRowBorder, RouterStatusBadge, RouterStatusBadgeText, RouterPingText,
                    isRu ? "⚠ Повышенный пинг" : "⚠ Elevated ping", "#FFC533", "#18FFC533");
            }
            else
            {
                SetSegmentNormal(RouterRowBorder, RouterStatusBadge, RouterStatusBadgeText, RouterPingText,
                    isRu ? "✓ Норма" : "✓ OK", "#59D499", "#1859D499");
            }
        }
        else
        {
            RouterPingText.Text = isRu ? "* (таймаут)" : "* (timeout)";
            SetSegmentAlert(RouterRowBorder, RouterStatusBadge, RouterStatusBadgeText, RouterPingText,
                isRu ? "✕ Нет ответа" : "✕ No response", "#FF6161", "#18FF6161");
        }

        // 3. Сегмент 2: Провайдер
        string ispLabel = res.IspIp;
        if (!isRu && (ispLabel == "Шлюз провайдера" || string.IsNullOrEmpty(ispLabel)))
        {
            ispLabel = "ISP gateway";
        }
        IspIpText.Text = isRu ? $"Шлюз оператора: {ispLabel}" : $"ISP gateway: {ispLabel}";
        if (res.IspPingMs >= 0)
        {
            IspPingText.Text = $"{res.IspPingMs} ms";
            if (res.IspPingMs > 200)
            {
                SetSegmentAlert(IspRowBorder, IspStatusBadge, IspStatusBadgeText, IspPingText,
                    isRu ? "⚠ Высокий пинг" : "⚠ High ping", "#FF6161", "#18FF6161");
            }
            else if (res.IspPingMs > 100)
            {
                SetSegmentAlert(IspRowBorder, IspStatusBadge, IspStatusBadgeText, IspPingText,
                    isRu ? "⚠ Повышенный пинг" : "⚠ Elevated ping", "#FFC533", "#18FFC533");
            }
            else
            {
                SetSegmentNormal(IspRowBorder, IspStatusBadge, IspStatusBadgeText, IspPingText,
                    isRu ? "✓ Норма" : "✓ OK", "#59D499", "#1859D499");
            }
        }
        else
        {
            IspPingText.Text = res.Category == "Isp" 
                ? (isRu ? "* (таймаут)" : "* (timeout)") 
                : (isRu ? "В норме" : "OK");
            if (res.Category == "Isp")
            {
                SetSegmentAlert(IspRowBorder, IspStatusBadge, IspStatusBadgeText, IspPingText,
                    isRu ? "⚠ Потери у провайдера" : "⚠ Packet loss at ISP", "#FF6161", "#18FF6161");
            }
            else
            {
                SetSegmentNormal(IspRowBorder, IspStatusBadge, IspStatusBadgeText, IspPingText,
                    isRu ? "✓ Шлюз в норме" : "✓ Gateway OK", "#59D499", "#1859D499");
            }
        }

        // 4. Сегмент 3: Магистраль / Сервер
        TargetIpText.Text = isRu 
            ? $"Целевой игровой узел: {displayTargetName} ({_targetHost})"
            : $"Target game node: {displayTargetName} ({_targetHost})";
        if (_record.IsTimeout || _record.PingMs <= 0)
        {
            TargetPingText.Text = "-- ms";
            SetSegmentAlert(BackboneRowBorder, BackboneStatusBadge, BackboneStatusBadgeText, TargetPingText,
                isRu ? "✕ Таймаут пакета" : "✕ Packet timeout", "#FF6161", "#18FF6161");
        }
        else
        {
            TargetPingText.Text = $"{_record.PingMs} ms";
            if (_record.PingMs > 200)
            {
                SetSegmentAlert(BackboneRowBorder, BackboneStatusBadge, BackboneStatusBadgeText, TargetPingText,
                    isRu ? "⚠ Высокий пинг" : "⚠ High ping", "#FF6161", "#18FF6161");
            }
            else if (_record.PingMs > 100 || _record.IsSpike)
            {
                SetSegmentAlert(BackboneRowBorder, BackboneStatusBadge, BackboneStatusBadgeText, TargetPingText,
                    isRu ? "⚠ Скачок задержки" : "⚠ Latency spike", "#FFC533", "#18FFC533");
            }
            else
            {
                SetSegmentNormal(BackboneRowBorder, BackboneStatusBadge, BackboneStatusBadgeText, TargetPingText,
                    isRu ? "✓ Стабильно" : "✓ Stable", "#59D499", "#1859D499");
            }
        }

        // 5. Рекомендация
        RecommendationText.Text = res.Recommendation;
    }

    private static void SetSegmentAlert(
        Border rowBorder,
        Border badgeBorder,
        TextBlock badgeText,
        TextBlock pingText,
        string status,
        string colorHex,
        string bgHex)
    {
        var col = (Color)ColorConverter.ConvertFromString(colorHex);
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var brush = new SolidColorBrush(col);
        var bgBrush = new SolidColorBrush(bg);

        rowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, col.R, col.G, col.B));
        rowBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, col.R, col.G, col.B));
        badgeBorder.Background = bgBrush;
        badgeBorder.BorderBrush = brush;
        badgeText.Text = status;
        badgeText.Foreground = brush;
        pingText.Foreground = brush;
    }

    private static void SetSegmentNormal(
        Border rowBorder,
        Border badgeBorder,
        TextBlock badgeText,
        TextBlock pingText,
        string status,
        string colorHex,
        string bgHex)
    {
        var col = (Color)ColorConverter.ConvertFromString(colorHex);
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var brush = new SolidColorBrush(col);
        var bgBrush = new SolidColorBrush(bg);

        rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22262C"));
        rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121418"));
        badgeBorder.Background = bgBrush;
        badgeBorder.BorderBrush = brush;
        badgeText.Text = status;
        badgeText.Foreground = brush;
        pingText.Foreground = brush;
    }
}
