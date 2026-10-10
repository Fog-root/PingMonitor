using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotaPingMonitor.Data;
using DotaPingMonitor.Models;
using DotaPingMonitor.Services;
using DotaPingMonitor.Views;

namespace DotaPingMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _database = new();
    public DatabaseService Database => _database;
    private readonly DotaSdrPingService _dotaSdr = new();
    private readonly OverlayConfigService _overlayConfigService = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly FpsService _fpsService = new();
    private readonly CpuService _cpuService = new();
    private readonly NetworkDiagnosticService _networkDiagnosticService = new();
    private readonly TaskManagerService _taskManagerService = new();
    private readonly RamService _ramService = new();
    private readonly DotaMatchTrackerService _dotaMatchTrackerService = new();
    private readonly GameBoostService _gameBoostService = new();
    private readonly NetworkTweaksService _networkTweaksService = new();
    private readonly GameService _gameService;

    public GameService GameService => _gameService;
    public DotaMatchTrackerService DotaMatchTracker => _dotaMatchTrackerService;
    public GameBoostService GameBoost => _gameBoostService;
    public NetworkTweaksService NetworkTweaks => _networkTweaksService;

    private readonly Dictionary<string, List<PingRecord>> _gameSessionRecords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<HistoryItemViewModel>> _gameHistoryItems = new(StringComparer.OrdinalIgnoreCase);

    public NetworkDiagnosticService NetworkDiagnostic => _networkDiagnosticService;

    private readonly DispatcherTimer _pingTimer;
    private readonly DispatcherTimer _sdrUiTimer;
    private readonly DispatcherTimer _allTargetsPingTimer;
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly DispatcherTimer _matchDurationTimer;
    private int _refreshTargetsRunId;

    private HudOverlayWindow? _overlayWindow;
    private int _sdrCountdown = 15;
    private bool _isPingCheckRunning;
    private double _currentLossPercent;
    private double _matchRollingLoss;
    private readonly Queue<bool> _matchRecentProbes = new(30);
    private PingTargetItemViewModel? _userSavedPingTarget;
    private PingTargetItemViewModel? _matchPingTarget;
    private DateTime _matchStartTime = DateTime.MinValue;
    private double _currentJitterMs;
    private long _lastRecordedPingMs = -1;
    private bool _isLastPingTimeout;
    private List<PingRecord>? _lastAllRecords;
    private List<PingRecord>? _lastValidRecords;
    private List<SdrPingResult>? _lastSdrResults;
    private bool _isThemeChangingInternal;
    private bool _isGameChangingInternal;
    private bool _isInitialized;
    private Task? _initDbTask;

    // View notifications
    public List<PingRecord> SessionRecords { get; } = new();
    public event Action<List<PingRecord>>? GraphDataUpdated;
    public event Action<string, string>? RequestOpenMtr;
    public event Action? RequestHistoryAutoScroll;

    private static readonly (string Name, string Ip)[] TargetDefinitions =
    {
        ("Google DNS (8.8.8.8)", "8.8.8.8"),
        ("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
        ("Россия — Stockholm (Valve)", "162.254.198.41"),
        ("Россия — Stockholm 2 (Valve)", "185.25.182.1"),
        ("EU West — Frankfurt (Valve)", "155.133.226.68"),
        ("EU East — Vienna (Valve)", "146.66.155.1"),
        ("Poland — Warsaw (Valve)", "155.133.230.98"),
        ("US East — Virginia (Valve)", "208.78.164.1"),
        ("US West — Seattle (Valve)", "205.196.6.135"),
        ("SE Asia — Singapore", "103.28.54.1"),
    };

    private static readonly Dictionary<string, string> RegionNames = new()
    {
        ["waw"] = "Warsaw",
        ["sto"] = "Stockholm",
        ["sto2"] = "Stockholm 2",
        ["fra"] = "Frankfurt",
        ["dfra"] = "Frankfurt 2",
        ["dvie"] = "Frankfurt 3",
        ["vie"] = "Vienna",
        ["ams"] = "Amsterdam",
        ["lhr"] = "London",
        ["mad"] = "Madrid",
        ["par"] = "Paris",
        ["hel"] = "Helsinki",
        ["fsn"] = "Falkenstein",
        ["iad"] = "Washington",
        ["atl"] = "Atlanta",
        ["ord"] = "Chicago",
        ["dfw"] = "Dallas",
        ["dxb"] = "Dubai",
        ["sgp"] = "Singapore",
        ["hkg"] = "Hong Kong",
        ["gru"] = "São Paulo",
        ["eze"] = "Buenos Aires",
        ["scl"] = "Santiago",
        ["lim"] = "Lima",
        ["jnb"] = "Johannesburg",
        ["sea"] = "Seattle",
        ["lax"] = "Los Angeles",
        ["tyo"] = "Tokyo",
        ["syd"] = "Sydney",
        ["lux"] = "Luxembourg",
        ["eat"] = "Washington",
        ["eu-north-1"] = "Stockholm",
        ["eu-central-1"] = "Frankfurt",
        ["eu-west-1"] = "Ireland",
        ["eu-west-2"] = "London",
        ["eu-south-1"] = "Milan",
        ["me-south-1"] = "Bahrain",
        ["me-central-1"] = "UAE",
        ["us-east-1"] = "N. Virginia",
        ["us-east-2"] = "Ohio",
        ["us-west-2"] = "Oregon",
        ["ru1"] = "Москва RU1",
        ["ru2"] = "Москва RU2",
        ["ru4"] = "Красноярск RU4",
        ["ru6"] = "Москва RU6",
        ["ru8"] = "Екатеринбург RU8",
        ["ru9"] = "Хабаровск RU9",
        ["ru12"] = "Москва RU12",
        ["eu1"] = "Frankfurt EU1",
        ["eu2"] = "Frankfurt EU2",
        ["eu3"] = "Warsaw EU3",
        ["eu4"] = "Warsaw EU4",
        ["na_c"] = "US Central",
        ["na_e"] = "US East",
        ["fra1"] = "Frankfurt 1",
        ["fra2"] = "Frankfurt 2",
        ["bel"] = "Belgium",
        ["bhr"] = "Bahrain",
        ["pdx"] = "Oregon",
        ["dsm"] = "Iowa",
        ["ist"] = "Istanbul",
        ["irl"] = "Ireland",
        ["icn"] = "Seoul"
    };

    // =========================================================
    // OBSERVABLE PROPERTIES
    // =========================================================

    // Collections
    public ObservableCollection<GameItemViewModel> AvailableGames { get; } = new();
    public ObservableCollection<PingTargetItemViewModel> PingTargets { get; } = new();
    public ObservableCollection<ThemeItemViewModel> Themes { get; } = new();
    public ObservableCollection<DotaSdrItemViewModel> SdrItems { get; } = new();
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = new();

    // Selected items
    [ObservableProperty]
    private GameItemViewModel? _selectedGame;

    [ObservableProperty]
    private PingTargetItemViewModel? _selectedPingTarget;

    public bool IsCustomTargetSelected => SelectedPingTarget?.IsCustom == true;

    [ObservableProperty]
    private ThemeItemViewModel? _selectedTheme;

    // Current Ping Hero Card
    [ObservableProperty]
    private string _currentPingText = "-- ms";

    [ObservableProperty]
    private Brush _currentPingForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _statusText = "Проверяем соединение...";

    [ObservableProperty]
    private Brush _statusBadgeBackground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _statusBadgeBorderBrush = Brushes.Transparent;

    // Metrics Cards
    [ObservableProperty]
    private string _averagePingText = "-- ms";

    [ObservableProperty]
    private Brush _averagePingForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _averagePeriodText = " (60 сек)";

    [ObservableProperty]
    private string _minPingText = "-- ms";

    [ObservableProperty]
    private Brush _minPingForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _maxPingText = "-- ms";

    [ObservableProperty]
    private Brush _maxPingForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _packetLossText = "0.0%";

    [ObservableProperty]
    private string _packetLossSubText = " (0 из 0)";

    [ObservableProperty]
    private Brush _packetLossForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _spikesCountText = "0";

    [ObservableProperty]
    private Brush _spikesCountForeground = Brushes.Transparent;

    // Chart
    [ObservableProperty]
    private string _chartSubtitleText = "(последняя минута)";

    [ObservableProperty]
    private int _chartIntervalMinutes = 1;

    public bool IsInterval1m
    {
        get => ChartIntervalMinutes == 1;
        set { if (value) SetChartInterval(1); }
    }

    public bool IsInterval5m
    {
        get => ChartIntervalMinutes == 5;
        set { if (value) SetChartInterval(5); }
    }

    public bool IsInterval10m
    {
        get => ChartIntervalMinutes == 10;
        set { if (value) SetChartInterval(10); }
    }

    public bool IsInterval60m
    {
        get => ChartIntervalMinutes == 60;
        set { if (value) SetChartInterval(60); }
    }

    [ObservableProperty]
    private Visibility _chartLiveStatusVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _chartLiveStatusText = "-- ms (Нет ответа)";

    [ObservableProperty]
    private Brush _chartLiveStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _chartLiveStatusBackground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _chartLiveStatusBorderBrush = Brushes.Transparent;

    // =========================================================
    // DOTA 2 ACTIVE MATCH MONITORING
    // =========================================================
    [ObservableProperty]
    private bool _isInDotaMatch;

    [ObservableProperty]
    private Visibility _dotaMatchBannerVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _dotaMatchBadgeVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _dotaMatchBadgeText = "";

    [ObservableProperty]
    private string _dotaMatchServerText = "";

    [ObservableProperty]
    private string _dotaMatchRelayText = "";

    [ObservableProperty]
    private string _dotaMatchLossText = "0.0%";

    [ObservableProperty]
    private Brush _dotaMatchLossForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _dotaMatchDurationText = "00:00";

    [ObservableProperty]
    private string _dotaMatchTooltip = "";

    [ObservableProperty]
    private Visibility _dotaLogWarningVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private bool _isAutoDetectDotaMatchEnabled = true;

    [ObservableProperty]
    private string _autoMatchButtonText = "🎯 Авто-катка: ВКЛ";

    [ObservableProperty]
    private Brush _autoMatchButtonBackground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _autoMatchButtonBorderBrush = Brushes.Transparent;

    [ObservableProperty]
    private Brush _autoMatchButtonForeground = Brushes.Transparent;

    public string LocDotaAutoMatchTooltip => LocalizationService.Get("DotaAutoMatchTooltip");
    public string LocDotaInMatch => LocalizationService.Get("DotaInMatchBadge");
    public string LocDotaMatchServer => LocalizationService.Get("DotaMatchServerLabel");
    public string LocDotaMatchRelay => LocalizationService.Get("DotaMatchRelayLabel");
    public string LocDotaMatchLoss => LocalizationService.Get("DotaMatchLossLabel");
    public string LocDotaMatchDuration => LocalizationService.Get("DotaMatchDurationLabel");

    [ObservableProperty]
    private string _copyConLogStatusText = "";

    public string LocDotaLogWarningTitle => LocalizationService.Get("DotaLogWarningTitle");
    public string LocDotaLogWarningText => LocalizationService.Get("DotaLogWarningText");
    public string LocDotaLogWarningTip => LocalizationService.Get("DotaLogWarningTip");
    public string LocDotaCopyCommand => LocalizationService.Get("DotaCopyCommand");
    public string LocDotaCopyCondebug => LocalizationService.Get("DotaCopyCondebug");

    // SDR Card
    [ObservableProperty]
    private string _sdrStatusText = "Ожидаем данные от Dota 2...";

    [ObservableProperty]
    private Brush _sdrStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _sdrTimerText = "Обновление: --";

    [ObservableProperty]
    private Brush _sdrTimerForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _bestSdrPingText = "-- ms";

    [ObservableProperty]
    private Brush _bestSdrForeground = Brushes.Transparent;

    [ObservableProperty]
    private bool _canRefreshSdr = true;

    // History Card
    [ObservableProperty]
    private string _historyCountText = "Сессия: 0 замеров";

    // Overlay & Hotkeys state
    [ObservableProperty]
    private string _overlayButtonText = "🎮 Оверлей";

    [ObservableProperty]
    private string _overlayButtonToolTip = "Игровой HUD-оверлей поверх экрана (Хоткей: Ctrl+Shift+O, закрепить: Ctrl+Shift+L)";

    [ObservableProperty]
    private Brush _overlayStatusDotBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _hkOverlayStatusText = "выкл";

    [ObservableProperty]
    private Brush _hkOverlayBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkOverlayBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkOverlayForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkOverlayStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _hkLockStatusText = "клик";

    [ObservableProperty]
    private Brush _hkLockBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkLockBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkLockForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkLockStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _hkSparklineStatusText = "граф";

    [ObservableProperty]
    private Brush _hkSparklineBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkSparklineBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkSparklineForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkSparklineStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuSparklineHeader = "✓ Кардиограмма сети (Ctrl+Shift+G)";

    [ObservableProperty]
    private string _hkFpsStatusText = "фпс";

    [ObservableProperty]
    private Brush _hkFpsBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkFpsBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkFpsForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkFpsStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuFpsHeader = "✓ Кадры в секунду (Ctrl+Shift+F)";

    [ObservableProperty]
    private string _hkCpuStatusText = "цп";

    [ObservableProperty]
    private Brush _hkCpuBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkCpuBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkCpuForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkCpuStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuCpuHeader = "✓ Нагрузка ЦП (Ctrl+Shift+C)";

    [ObservableProperty]
    private string _hkGpuStatusText = "гп";

    [ObservableProperty]
    private Brush _hkGpuBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGpuBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGpuForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGpuStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuGpuHeader = "✓ Нагрузка ГП (Ctrl+Shift+U)";

    [ObservableProperty]
    private string _hkRamStatusText = "озу";

    [ObservableProperty]
    private Brush _hkRamBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkRamBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkRamForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkRamStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuRamHeader = "✓ Оперативная память (Ctrl+Shift+R)";

    [ObservableProperty]
    private string _hkGlowStatusText = "свеч";

    [ObservableProperty]
    private Brush _hkGlowBg = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGlowBorder = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGlowForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush _hkGlowStatusForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _menuGlowHeader = "✓ Свечение контура (Ctrl+Shift+B)";

    [ObservableProperty]
    private string _hkOverlayText = "Ctrl+Shift+O";

    [ObservableProperty]
    private string _hkLockText = "Ctrl+Shift+L";

    [ObservableProperty]
    private string _hkSparklineText = "Ctrl+Shift+G";

    [ObservableProperty]
    private string _hkFpsText = "Ctrl+Shift+F";

    [ObservableProperty]
    private string _hkCpuText = "Ctrl+Shift+C";

    [ObservableProperty]
    private string _hkGpuText = "Ctrl+Shift+U";

    [ObservableProperty]
    private string _hkRamText = "Ctrl+Shift+R";

    [ObservableProperty]
    private string _hkGlowText = "Ctrl+Shift+B";

    public HotkeyBinding OverlayBinding => _keyboardHook.OverlayBinding;
    public HotkeyBinding LockBinding => _keyboardHook.LockBinding;
    public HotkeyBinding SparklineBinding => _keyboardHook.SparklineBinding;
    public HotkeyBinding FpsBinding => _keyboardHook.FpsBinding;
    public HotkeyBinding CpuBinding => _keyboardHook.CpuBinding;
    public HotkeyBinding GpuBinding => _keyboardHook.GpuBinding;
    public HotkeyBinding RamBinding => _keyboardHook.RamBinding;
    public HotkeyBinding GlowBinding => _keyboardHook.GlowBinding;

    public void SetKeyboardHookPaused(bool isPaused)
    {
        _keyboardHook.IsPaused = isPaused;
    }

    public static HotkeyBinding GetDefaultBinding(string hotkeyId)
    {
        return hotkeyId.ToLowerInvariant() switch
        {
            "overlay" => new HotkeyBinding(true, true, false, false, 0x4F, "Ctrl+Shift+O"),
            "lock" => new HotkeyBinding(true, true, false, false, 0x4C, "Ctrl+Shift+L"),
            "sparkline" => new HotkeyBinding(true, true, false, false, 0x47, "Ctrl+Shift+G"),
            "fps" => new HotkeyBinding(true, true, false, false, 0x46, "Ctrl+Shift+F"),
            "cpu" => new HotkeyBinding(true, true, false, false, 0x43, "Ctrl+Shift+C"),
            "gpu" => new HotkeyBinding(true, true, false, false, 0x55, "Ctrl+Shift+U"),
            "ram" => new HotkeyBinding(true, true, false, false, 0x52, "Ctrl+Shift+R"),
            "glow" => new HotkeyBinding(true, true, false, false, 0x42, "Ctrl+Shift+B"),
            _ => new HotkeyBinding(true, true, false, false, 0x4F, "Ctrl+Shift+O")
        };
    }

    public void UpdateHotkey(string hotkeyId, HotkeyBinding newBinding)
    {
        var cfg = _overlayConfigService.Load();

        switch (hotkeyId.ToLowerInvariant())
        {
            case "overlay":
                _keyboardHook.OverlayBinding = newBinding;
                HkOverlayText = newBinding.DisplayText;
                cfg.HotkeyOverlay = newBinding;
                break;
            case "lock":
                _keyboardHook.LockBinding = newBinding;
                HkLockText = newBinding.DisplayText;
                cfg.HotkeyLock = newBinding;
                break;
            case "sparkline":
                _keyboardHook.SparklineBinding = newBinding;
                HkSparklineText = newBinding.DisplayText;
                cfg.HotkeySparkline = newBinding;
                MenuSparklineHeader = $"{(cfg.ShowSparkline ? "✓" : " ")} Кардиограмма сети ({newBinding.DisplayText})";
                break;
            case "fps":
                _keyboardHook.FpsBinding = newBinding;
                HkFpsText = newBinding.DisplayText;
                cfg.HotkeyFps = newBinding;
                MenuFpsHeader = $"{(cfg.ShowFps ? "✓" : " ")} Кадры в секунду ({newBinding.DisplayText})";
                break;
            case "cpu":
                _keyboardHook.CpuBinding = newBinding;
                HkCpuText = newBinding.DisplayText;
                cfg.HotkeyCpu = newBinding;
                MenuCpuHeader = $"{(cfg.ShowCpu ? "✓" : " ")} Нагрузка ЦП ({newBinding.DisplayText})";
                break;
            case "gpu":
                _keyboardHook.GpuBinding = newBinding;
                HkGpuText = newBinding.DisplayText;
                cfg.HotkeyGpu = newBinding;
                MenuGpuHeader = $"{(cfg.ShowGpu ? "✓" : " ")} Нагрузка ГП ({newBinding.DisplayText})";
                break;
            case "ram":
                _keyboardHook.RamBinding = newBinding;
                HkRamText = newBinding.DisplayText;
                cfg.HotkeyRam = newBinding;
                MenuRamHeader = $"{(cfg.ShowRam ? "✓" : " ")} {LocalizationService.Get("RamMenu")} ({newBinding.DisplayText})";
                break;
            case "glow":
                _keyboardHook.GlowBinding = newBinding;
                HkGlowText = newBinding.DisplayText;
                cfg.HotkeyGlow = newBinding;
                MenuGlowHeader = $"{(cfg.ShowGlowEffect ? "✓" : " ")} {LocalizationService.Get("GlowMenu")} ({newBinding.DisplayText})";
                break;
        }

        // Синхронизируем все активные бинды в конфигурацию
        cfg.HotkeyOverlay = _keyboardHook.OverlayBinding;
        cfg.HotkeyLock = _keyboardHook.LockBinding;
        cfg.HotkeySparkline = _keyboardHook.SparklineBinding;
        cfg.HotkeyFps = _keyboardHook.FpsBinding;
        cfg.HotkeyCpu = _keyboardHook.CpuBinding;
        cfg.HotkeyGpu = _keyboardHook.GpuBinding;
        cfg.HotkeyRam = _keyboardHook.RamBinding;
        cfg.HotkeyGlow = _keyboardHook.GlowBinding;

        _overlayConfigService.Save(cfg);
        _overlayWindow?.SyncConfig(cfg);
        UpdateOverlayButtonState();
    }

    public bool IsOverlayActive => _overlayWindow != null && _overlayWindow.IsVisible;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public MainViewModel()
    {
        var initialCfg = _overlayConfigService.Load();
        string initialLang = string.IsNullOrWhiteSpace(initialCfg.Language)
            ? LocalizationService.DetectDefaultLanguage()
            : initialCfg.Language;
        LocalizationService.SetLanguage(initialLang);
        LocalizationService.LanguageChanged += OnLanguageChanged;

        _gameService = new GameService(string.IsNullOrWhiteSpace(initialCfg.SelectedGame) ? "dota2" : initialCfg.SelectedGame);

        // 1. Инициализируем переключатель доступных игр
        foreach (var g in _gameService.Games)
        {
            AvailableGames.Add(new GameItemViewModel
            {
                Id = g.Id,
                DisplayName = g.DisplayName,
                ShortName = g.ShortName,
                IconGlyph = g.IconGlyph,
                IconColorHex = g.LogoColorHex,
                SteamAppId = g.SteamAppId,
                IsSelected = g.Id.Equals(_gameService.CurrentGame.Id, StringComparison.OrdinalIgnoreCase)
            });
        }
        _isGameChangingInternal = true;
        try
        {
            SelectedGame = AvailableGames.FirstOrDefault(g => g.IsSelected) ?? AvailableGames.FirstOrDefault();
        }
        finally
        {
            _isGameChangingInternal = false;
        }

        // 2. Заполняем цели пинга для активной дисциплины
        foreach (var target in _gameService.CurrentGame.Targets)
        {
            PingTargets.Add(target);
        }

        string preferredServer = initialCfg.GetSelectedServerForGame(_gameService.CurrentGame.Id);

        if (string.IsNullOrWhiteSpace(preferredServer))
        {
            preferredServer = initialCfg.SelectedServer;
        }

        if (!string.IsNullOrWhiteSpace(preferredServer))
        {
            var savedTarget = PingTargets.FirstOrDefault(t =>
                t.Name.Equals(preferredServer, StringComparison.OrdinalIgnoreCase) ||
                t.Ip.Equals(preferredServer, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(preferredServer, StringComparison.OrdinalIgnoreCase) ||
                preferredServer.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
            SelectedPingTarget = savedTarget ?? PingTargets.FirstOrDefault(t => t.Name.Equals(_gameService.CurrentGame.DefaultServerName, StringComparison.OrdinalIgnoreCase)) ?? PingTargets.FirstOrDefault();
        }
        else
        {
            SelectedPingTarget = PingTargets.FirstOrDefault(t => t.Name.Equals(_gameService.CurrentGame.DefaultServerName, StringComparison.OrdinalIgnoreCase)) ?? PingTargets.FirstOrDefault();
        }

        // Если активная игра не Dota 2, сразу переключаем SDR на соответствующий AppID
        if (_gameService.CurrentGame.SteamAppId != 570)
        {
            _ = _dotaSdr.SwitchGameAsync(_gameService.CurrentGame.SteamAppId, _gameService.CurrentGame.DefaultPops, _gameService.CurrentGame.DefaultRouteName);
        }

        // Загружаем сохраненные хоткеи
        if (initialCfg.HotkeyOverlay != null)
        {
            _keyboardHook.OverlayBinding = initialCfg.HotkeyOverlay;
            HkOverlayText = initialCfg.HotkeyOverlay.DisplayText;
        }
        if (initialCfg.HotkeyLock != null)
        {
            _keyboardHook.LockBinding = initialCfg.HotkeyLock;
            HkLockText = initialCfg.HotkeyLock.DisplayText;
        }
        if (initialCfg.HotkeySparkline != null)
        {
            _keyboardHook.SparklineBinding = initialCfg.HotkeySparkline;
            HkSparklineText = initialCfg.HotkeySparkline.DisplayText;
            MenuSparklineHeader = $"{(initialCfg.ShowSparkline ? "✓" : " ")} Кардиограмма сети ({initialCfg.HotkeySparkline.DisplayText})";
        }
        if (initialCfg.HotkeyFps != null)
        {
            _keyboardHook.FpsBinding = initialCfg.HotkeyFps;
            HkFpsText = initialCfg.HotkeyFps.DisplayText;
            MenuFpsHeader = $"{(initialCfg.ShowFps ? "✓" : " ")} Кадры в секунду ({initialCfg.HotkeyFps.DisplayText})";
        }
        if (initialCfg.HotkeyCpu != null)
        {
            _keyboardHook.CpuBinding = initialCfg.HotkeyCpu;
            HkCpuText = initialCfg.HotkeyCpu.DisplayText;
            MenuCpuHeader = $"{(initialCfg.ShowCpu ? "✓" : " ")} Нагрузка ЦП ({initialCfg.HotkeyCpu.DisplayText})";
        }
        if (initialCfg.HotkeyGpu != null)
        {
            _keyboardHook.GpuBinding = initialCfg.HotkeyGpu;
            HkGpuText = initialCfg.HotkeyGpu.DisplayText;
            MenuGpuHeader = $"{(initialCfg.ShowGpu ? "✓" : " ")} Нагрузка ГП ({initialCfg.HotkeyGpu.DisplayText})";
        }
        if (initialCfg.HotkeyRam != null)
        {
            _keyboardHook.RamBinding = initialCfg.HotkeyRam;
            HkRamText = initialCfg.HotkeyRam.DisplayText;
            MenuRamHeader = $"{(initialCfg.ShowRam ? "✓" : " ")} {LocalizationService.Get("RamMenu")} ({initialCfg.HotkeyRam.DisplayText})";
        }
        if (initialCfg.HotkeyGlow != null)
        {
            _keyboardHook.GlowBinding = initialCfg.HotkeyGlow;
            HkGlowText = initialCfg.HotkeyGlow.DisplayText;
            MenuGlowHeader = $"{(initialCfg.ShowGlowEffect ? "✓" : " ")} {LocalizationService.Get("GlowMenu")} ({initialCfg.HotkeyGlow.DisplayText})";
        }

        // 2. Заполняем темы
        foreach (var theme in ThemeService.Themes)
        {
            var previewBrush = ThemeService.CreateThemePreviewBrush(theme);
            string themeName = theme.Id == "Linear"
                ? (LocalizationService.IsRussian ? "Linear Dark (По умолчанию)" : "Linear Dark (Default)")
                : theme.Name;

            Themes.Add(new ThemeItemViewModel
            {
                Id = theme.Id,
                Name = themeName,
                Description = theme.Description,
                PreviewBrush = previewBrush
            });
        }

        // 3. Подписка на изменение темы
        ThemeService.ThemeChanged += OnThemeChanged;

        // 3a. Восстановление режима оптимизации из конфига
        if (initialCfg.IsOptimizedMode)
        {
            ThemeService.SetOptimizationMode(true);
            _taskManagerService.RefreshIntervalMs = 3000;
            _taskManagerService.Gpu.RefreshIntervalMs = 3000;
        }
        ThemeService.OptimizationModeChanged += isOpt =>
        {
            _taskManagerService.RefreshIntervalMs = isOpt ? 3000 : 1000;
            _taskManagerService.Gpu.RefreshIntervalMs = isOpt ? 3000 : 1000;

            OnPropertyChanged(nameof(IsOptimizedMode));
            OnPropertyChanged(nameof(OptimizationButtonText));
            OnPropertyChanged(nameof(OptimizationButtonToolTip));
            OnPropertyChanged(nameof(OptimizationStatusDotBrush));
            OnPropertyChanged(nameof(OptimizationButtonBackground));
            OnPropertyChanged(nameof(OptimizationButtonBorderBrush));
            OnPropertyChanged(nameof(OptimizationButtonForeground));
            OnPropertyChanged(nameof(CardCornerRadius));
            OnPropertyChanged(nameof(CardPadding));
            OnPropertyChanged(nameof(ContainerCornerRadius));
            OnPropertyChanged(nameof(ContainerPadding));
            OnPropertyChanged(nameof(TableHeaderCornerRadius));
            OnPropertyChanged(nameof(TableHeaderPadding));
            OnPropertyChanged(nameof(TelemetryBarHeight));

            foreach (var proc in Processes)
            {
                if (isOpt)
                {
                    proc.Icon = null;
                }
                proc.RefreshOptimizationMode();
            }

            RefreshTaskManager();
        };

        // 4. Глобальные хоткеи
        _keyboardHook.ToggleOverlayRequested += () => Dispatch(() => ToggleOverlay());
        _keyboardHook.ToggleLockRequested += () => Dispatch(() => ToggleLock());
        _keyboardHook.ToggleSparklineRequested += () => Dispatch(() => ToggleSparkline());
        _keyboardHook.ToggleFpsRequested += () => Dispatch(() => ToggleFps());
        _keyboardHook.ToggleCpuRequested += () => Dispatch(() => ToggleCpu());
        _keyboardHook.ToggleGpuRequested += () => Dispatch(() => ToggleGpu());
        _keyboardHook.ToggleRamRequested += () => Dispatch(() => ToggleRam());
        _keyboardHook.ToggleGlowRequested += () => Dispatch(() => ToggleGlow());

        // 5. FPS, CPU, GPU и RAM сервисы
        _fpsService.FpsUpdated += fps =>
        {
            _overlayWindow?.UpdateFps(fps);
        };

        _taskManagerService.CpuPercentProvider = () => _cpuService.CurrentCpu;

        _cpuService.CpuUpdated += cpu =>
        {
            _overlayWindow?.UpdateCpu(cpu);
            if (IsTaskManagerView)
            {
                TotalCpuText = $"{cpu}%";
                TotalCpuPercent = cpu;
            }
        };

        _taskManagerService.GpuUpdated += gpu =>
        {
            _overlayWindow?.UpdateGpu(gpu);
            if (IsTaskManagerView)
            {
                TotalGpuText = $"{gpu}%";
                TotalGpuPercent = gpu;
            }
        };

        _ramService.RamUpdated += (ram, usedGb, totalGb) =>
        {
            _overlayWindow?.UpdateRam(ram, usedGb, totalGb);
        };

        // 6. Таймеры: умеренный интервал для предотвращения паразитной нагрузки на стек NDIS
        _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _pingTimer.Tick += async (_, _) => await CheckPingAsync();

        _sdrUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sdrUiTimer.Tick += (_, _) => UpdateSdrCountdown();

        _allTargetsPingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _allTargetsPingTimer.Tick += async (_, _) => await RefreshAllTargetsPingAsync();

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            ApplyProcessFilterAndSort();
        };

        _dotaSdr.PingUpdated += OnDotaSdrPingUpdated;
        _taskManagerService.SnapshotUpdated += OnTaskManagerSnapshotUpdated;

        // 7. Автоопределение активного матча Dota 2
        IsAutoDetectDotaMatchEnabled = initialCfg.AutoDetectDotaMatch;
        _dotaMatchTrackerService.IsEnabled = IsAutoDetectDotaMatchEnabled;
        _dotaMatchTrackerService.SetDestinationResolver((ingress, backhaul) => _dotaSdr.FindDestinationCluster(ingress, backhaul));
        _dotaMatchTrackerService.MatchConnected += OnDotaMatchConnected;
        _dotaMatchTrackerService.MatchDisconnected += OnDotaMatchDisconnected;
        _dotaMatchTrackerService.DotaLoggingStatusChanged += isStale =>
        {
            Dispatch(() =>
            {
                DotaLogWarningVisibility = (IsAutoDetectDotaMatchEnabled && !IsInDotaMatch && isStale)
                    ? Visibility.Visible : Visibility.Collapsed;
            });
        };

        _matchDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _matchDurationTimer.Tick += (_, _) =>
        {
            if (IsInDotaMatch && _matchStartTime > DateTime.MinValue)
            {
                var elapsed = DateTime.Now - _matchStartTime;
                DotaMatchDurationText = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                    : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
        };

        ApplyCurrentThemeBrushes();
        UpdateOverlayButtonState();
        UpdateAutoMatchButtonState();
    }

    // =========================================================
    // INITIALIZATION & CLEANUP
    // =========================================================

    public async Task InitializeAsync()
    {
        // 1. Фоновая неблокирующая инициализация и очистка локальной базы данных
        _initDbTask = Task.Run(async () =>
        {
            await _database.InitializeAsync().ConfigureAwait(false);
            await _database.ClearAllAsync().ConfigureAwait(false);
            await LoadCustomTargetsForActiveGameAsync().ConfigureAwait(false);
        });

        SessionRecords.Clear();
        HistoryItems.Clear();
        ResetStatistics();
        UpdateChartLiveStatus();
        GraphDataUpdated?.Invoke(new List<PingRecord>());
        _isInitialized = true;

        // 2. Моментальный старт мониторинга SDR и таймеров
        StartDotaSdrMonitoring();
        StartSdrCountdown();

        _pingTimer.Start();
        _sdrUiTimer.Start();
        _allTargetsPingTimer.Start();
        _dotaMatchTrackerService.Start();

        // 3. Быстрое применение сохраненной темы и оверлея
        var overlayCfg = _overlayConfigService.Load();
        if (!string.IsNullOrWhiteSpace(overlayCfg.Language))
        {
            LocalizationService.SetLanguage(overlayCfg.Language);
        }
        ThemeService.SetOptimizationMode(overlayCfg.IsOptimizedMode);
        string themeToApply = string.IsNullOrWhiteSpace(overlayCfg.Theme) ? "Linear" : overlayCfg.Theme;
        ThemeService.ApplyTheme(themeToApply);
        SelectThemeInList(ThemeService.Current.Id);

        if (overlayCfg.IsEnabled)
        {
            EnsureOverlayCreated();
            _overlayWindow?.Show();
            if (_overlayWindow != null)
            {
                _overlayWindow.Topmost = true;
            }
        }
        UpdateOverlayButtonState();

        // 4. Запускаем первый замер пинга в фоне без задержки отображения окна
        _ = CheckPingAsync();
        _ = RefreshAllTargetsPingAsync();
        await Task.CompletedTask;
    }

    public void Cleanup()
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        ThemeService.ThemeChanged -= OnThemeChanged;
        _pingTimer.Stop();
        _sdrUiTimer.Stop();
        _allTargetsPingTimer.Stop();
        _searchDebounceTimer.Stop();
        _matchDurationTimer.Stop();
        _fpsService.Dispose();
        _cpuService.Dispose();
        _ramService.Dispose();
        _taskManagerService.Dispose();
        _dotaMatchTrackerService.Dispose();

        var cfg = _overlayConfigService.Load();
        cfg.AutoDetectDotaMatch = IsAutoDetectDotaMatchEnabled;
        if (SelectedPingTarget != null)
        {
            cfg.SelectedServer = SelectedPingTarget.Name;
        }

        // Гарантированно фиксируем актуальные хоткеи из хука
        cfg.HotkeyOverlay = _keyboardHook.OverlayBinding;
        cfg.HotkeyLock = _keyboardHook.LockBinding;
        cfg.HotkeySparkline = _keyboardHook.SparklineBinding;
        cfg.HotkeyFps = _keyboardHook.FpsBinding;
        cfg.HotkeyCpu = _keyboardHook.CpuBinding;
        cfg.HotkeyGpu = _keyboardHook.GpuBinding;
        cfg.HotkeyRam = _keyboardHook.RamBinding;

        if (_overlayWindow != null)
        {
            bool wasOverlayVisible = _overlayWindow.IsVisible;
            cfg.IsEnabled = wasOverlayVisible;
            cfg.Left = _overlayWindow.Left;
            cfg.Top = _overlayWindow.Top;
            cfg.Scale = Math.Round(_overlayWindow.CurrentScale, 2);
            cfg.IsLocked = _overlayWindow.IsLocked;

            _overlayWindow.SyncConfig(cfg);
            _overlayWindow.Close();
            _overlayWindow = null;
        }

        _keyboardHook.Dispose();

        _overlayConfigService.Save(cfg);

        try
        {
            _database.ClearAll();
        }
        catch
        {
            // Ignore on shutdown
        }

        HistoryItems.Clear();
        SessionRecords.Clear();
    }

    // =========================================================
    // SELECTION CHANGES
    // =========================================================

    partial void OnSelectedGameChanged(GameItemViewModel? value)
    {
        if (_isGameChangingInternal || value == null) return;
        SelectGame(value.Id);
    }

    partial void OnSelectedPingTargetChanged(PingTargetItemViewModel? value)
    {
        OnPropertyChanged(nameof(IsCustomTargetSelected));
        if (value == null) return;
        var cfg = _overlayConfigService.Load();
        cfg.SetSelectedServerForGame(_gameService.CurrentGame.Id, value.Name);
        _overlayConfigService.Save(cfg);

        _overlayWindow?.UpdateServerCode(GetServerRegionCode(value));

        if (!_isInitialized) return;

        SessionRecords.Clear();
        HistoryItems.Clear();
        _ = _database.ClearAllAsync();
        _ = CheckPingAsync();
    }

    partial void OnSelectedThemeChanged(ThemeItemViewModel? value)
    {
        if (_isThemeChangingInternal || value == null) return;

        ThemeService.ApplyTheme(value.Id);
        var cfg = _overlayConfigService.Load();
        cfg.Theme = value.Id;
        _overlayConfigService.Save(cfg);
    }

    private void SelectThemeInList(string themeId)
    {
        _isThemeChangingInternal = true;
        try
        {
            SelectedTheme = Themes.FirstOrDefault(t => t.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isThemeChangingInternal = false;
        }
    }

    // =========================================================
    // OPTIMIZATION MODE
    // =========================================================

    public bool IsOptimizedMode => ThemeService.IsOptimizedMode;

    public string OptimizationButtonText => ThemeService.IsOptimizedMode
        ? LocalizationService.Get("OptimizationActive")
        : LocalizationService.Get("OptimizationInactive");

    public string OptimizationButtonToolTip => ThemeService.IsOptimizedMode
        ? LocalizationService.Get("OptimizationActiveTooltip")
        : LocalizationService.Get("OptimizationInactiveTooltip");

    // =========================================================
    // STARTUP / RUN WITH WINDOWS
    // =========================================================

    [ObservableProperty]
    private bool _isStartupEnabled = StartupService.IsStartupEnabled();

    partial void OnIsStartupEnabledChanged(bool value)
    {
        StartupService.SetStartupEnabled(value);
    }

    // =========================================================
    // LOCALIZATION (i18n)
    // =========================================================

    public string CurrentLanguage => LocalizationService.CurrentLanguage;
    public bool IsRussian => LocalizationService.IsRussian;

    public Brush LangRuBg => LocalizationService.IsRussian
        ? CreateBrush(ThemeService.Current.AccentBlueSoft)
        : Brushes.Transparent;

    public Brush LangRuFg => LocalizationService.IsRussian
        ? CreateBrush(ThemeService.Current.AccentBlue)
        : CreateBrush(ThemeService.Current.TextMuted);

    public FontWeight LangRuFontWeight => LocalizationService.IsRussian ? FontWeights.Bold : FontWeights.Normal;

    public Brush LangEnBg => !LocalizationService.IsRussian
        ? CreateBrush(ThemeService.Current.AccentBlueSoft)
        : Brushes.Transparent;

    public Brush LangEnFg => !LocalizationService.IsRussian
        ? CreateBrush(ThemeService.Current.AccentBlue)
        : CreateBrush(ThemeService.Current.TextMuted);

    public FontWeight LangEnFontWeight => !LocalizationService.IsRussian ? FontWeights.Bold : FontWeights.Normal;

    public string LocGame => LocalizationService.Get("Game");
    public string LocServer => LocalizationService.Get("Server");
    public string LocTheme => LocalizationService.Get("Theme");
    public string LocLanguage => LocalizationService.Get("Language");
    public string LocLanguageTooltip => LocalizationService.Get("LanguageTooltip");

    public string LocMtr => LocalizationService.Get("MtrDiagnostics");
    public string LocMtrTooltip => LocalizationService.Get("MtrDiagnosticsTooltip");
    public string LocIspTicketButton => LocalizationService.Get("IspTicketButton");
    public string LocIspTicketTooltip => LocalizationService.Get("IspTicketTooltip");
    public string LocNavIspTicketTitle => LocalizationService.Get("NavIspTicketTitle");
    public string LocNavIspTicketDesc => LocalizationService.Get("NavIspTicketDesc");

    public string LocCurrentPing => LocalizationService.Get("CurrentPing");
    public string LocAvgPing => LocalizationService.Get("AveragePing");
    public string LocMinPing => LocalizationService.Get("Minimum");
    public string LocMinBestSuffix => LocalizationService.Get("BestSuffix");
    public string LocMaxPing => LocalizationService.Get("Maximum");
    public string LocMaxPeakSuffix => LocalizationService.Get("PeakSuffix");
    public string LocPacketLoss => LocalizationService.Get("PacketLoss");
    public string LocSpikes => LocalizationService.Get("Spikes");
    public string LocSpikesCountSuffix => LocalizationService.Get("SpikesCountSuffix");

    public string LocGraphTitle => LocalizationService.Get("NetworkLatencyGraph");
    public string LocLiveBadge => LocalizationService.Get("Live");
    public string LocReturnToLive => LocalizationService.Get("ReturnToLive");
    public string LocReturnToLiveTooltip => LocalizationService.Get("ReturnToLiveTooltip");
    public string LocChartBackTooltip => LocalizationService.Get("ChartBackTooltip");
    public string LocChartForwardTooltip => LocalizationService.Get("ChartForwardTooltip");
    public string LocRouter => LocalizationService.Get("Router");
    public string LocIsp => LocalizationService.Get("Isp");
    public string LocAnalyze => LocalizationService.Get("AnalyzeMtr");
    public string LocIcmpPing => LocalizationService.Get("IcmpPingLabel");

    public string LocInterval1m => LocalizationService.Get("Interval1m");
    public string LocInterval5m => LocalizationService.Get("Interval5m");
    public string LocInterval10m => LocalizationService.Get("Interval10m");
    public string LocInterval60m => LocalizationService.Get("Interval60m");

    public string LocColRegion => LocalizationService.Get("ColRegion");
    public string LocColCode => LocalizationService.Get("ColCode");
    public string LocColPing => LocalizationService.Get("ColPing");
    public string LocColRoute => LocalizationService.Get("ColRoute");
    public string LocBest => LocalizationService.Get("Best");
    public string LocRefresh => LocalizationService.Get("Refresh");

    public string LocHistoryTitle => LocalizationService.Get("MeasurementHistory");
    public string LocColTime => LocalizationService.Get("ColTime");
    public string LocColLoss => LocalizationService.Get("ColLoss");
    public string LocColStatus => LocalizationService.Get("ColStatus");
    public string LocColServer => LocalizationService.Get("ColServer");
    public string LocHotkeyHint => LocalizationService.Get("HkHint");

    public string LocNavSections => LocalizationService.Get("NavSections");
    public string LocNavPingDesc => LocalizationService.Get("NavPingDesc");
    public string LocNavTmTitle => LocalizationService.Get("NavTmTitle");
    public string LocNavTmDesc => LocalizationService.Get("NavTmDesc");
    public string LocNavSpeedtestTitle => LocalizationService.Get("NavSpeedtestTitle");
    public string LocNavSpeedtestDesc => LocalizationService.Get("NavSpeedtestDesc");
    public string LocNavActive => LocalizationService.Get("NavActive");
    public string LocNavMenuTooltip => LocalizationService.Get("NavMenuTooltip");
    public string LocGameComboTooltip => LocalizationService.Get("GameComboTooltip");

    public string LocNetworkTweaksButton => LocalizationService.IsRussian ? "Твики сети" : "Net Tweaks";
    public string LocNetworkTweaksTooltip => LocalizationService.IsRussian ? "Оптимизация сети и игровой режим (Game Boost, Nagle, Flush DNS)" : "Network latency optimization and Game Boost Mode";
    public string LocAddCustomServerTooltip => LocalizationService.IsRussian ? "Добавить свой сервер (IP / Домен)" : "Add custom target (IP / Domain)";
    public string LocDeleteCustomServerTooltip => LocalizationService.IsRussian ? "Удалить этот сервер" : "Delete this custom server";

    public string LocGlowAmbient => LocalizationService.Get("GlowMenuAmbient");
    public string LocGlowDota => LocalizationService.Get("GlowMenuDota");
    public string LocGlowOff => LocalizationService.Get("GlowMenuOff");
    public string LocHkGlowTooltip => LocalizationService.Get("HkGlowTooltip");

    public string LocSpeedtestStart => LocalizationService.Get("SpeedtestStart");
    public string LocSpeedtestCancel => LocalizationService.Get("SpeedtestCancel");
    public string LocSpeedtestTesting => LocalizationService.Get("SpeedtestTesting");
    public string LocSpeedtestIdle => LocalizationService.Get("SpeedtestIdle");
    public string LocSpeedtestDownload => LocalizationService.Get("SpeedtestDownload");
    public string LocSpeedtestUpload => LocalizationService.Get("SpeedtestUpload");
    public string LocSpeedtestPing => LocalizationService.Get("SpeedtestPing");
    public string LocSpeedtestJitter => LocalizationService.Get("SpeedtestJitter");
    public string LocSpeedtestIsp => LocalizationService.Get("SpeedtestIsp");
    public string LocSpeedtestServer => LocalizationService.Get("SpeedtestServer");
    public string LocSpeedtestClientIp => LocalizationService.Get("SpeedtestClientIp");
    public string LocSpeedtestPeak => LocalizationService.Get("SpeedtestPeak");
    public string LocSpeedtestAverage => LocalizationService.Get("SpeedtestAverage");
    public string LocSpeedtestWaveGraph => LocalizationService.Get("SpeedtestWaveGraph");
    public string LocSpeedtestHistoryTitle => LocalizationService.Get("SpeedtestHistoryTitle");
    public string LocSpeedtestClearHistory => LocalizationService.Get("SpeedtestClearHistory");
    public string LocSpeedtestNoHistory => LocalizationService.Get("SpeedtestNoHistory");
    public string LocSpeedtestMbps => LocalizationService.Get("SpeedtestMbps");
    public string LocSpeedtestColDownload => LocalizationService.Get("SpeedtestColDownload");
    public string LocSpeedtestColPeak => LocalizationService.Get("SpeedtestColPeak");
    public string LocSpeedtestColUpload => LocalizationService.Get("SpeedtestColUpload");
    public string LocSpeedtestColPing => LocalizationService.Get("SpeedtestColPing");
    public string LocSpeedtestColJitter => LocalizationService.Get("SpeedtestColJitter");
    public string LocSpeedtestColServer => LocalizationService.Get("SpeedtestColServer");

    public string LocSearchPlaceholder => LocalizationService.Get("SearchPlaceholder");
    public string LocEndTask => LocalizationService.Get("EndTask");
    public string LocEndTaskTooltip => LocalizationService.Get("EndTaskTooltip");
    public string LocFolder => LocalizationService.Get("Folder");
    public string LocFolderTooltip => LocalizationService.Get("FolderTooltip");
    public string LocRefreshTmTooltip => LocalizationService.Get("RefreshTmTooltip");
    public string LocProcessorCpu => LocalizationService.Get("ProcessorCpu");
    public string LocGraphicsGpu => LocalizationService.Get("GraphicsGpu");
    public string LocMemoryRam => LocalizationService.Get("MemoryRam");
    public string LocDiskActivity => LocalizationService.Get("DiskActivity");
    public string LocNetwork => LocalizationService.Get("Network");
    public string LocActiveProcesses => LocalizationService.Get("ActiveProcesses");
    public string LocTotalCpuLoad => LocalizationService.Get("TotalCpuLoad");
    public string LocTotalGpuLoad => LocalizationService.Get("TotalGpuLoad");
    public string LocTotalDiskIo => LocalizationService.Get("TotalDiskIo");
    public string LocSystem => LocalizationService.Get("System");

    public string LocColName => LocalizationService.Get("ColName");
    public string LocColPid => LocalizationService.Get("ColPid");
    public string LocColCpuSort => LocalizationService.Get("ColCpuSort");
    public string LocColGpuSort => LocalizationService.Get("ColGpuSort");
    public string LocColRamSort => LocalizationService.Get("ColRamSort");
    public string LocColDiskSort => LocalizationService.Get("ColDiskSort");
    public string LocColNetSort => LocalizationService.Get("ColNetSort");
    public string LocColThreadsSort => LocalizationService.Get("ColThreadsSort");
    public string LocColStateSort => LocalizationService.Get("ColStateSort");

    [RelayCommand]
    public void SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return;
        lang = LocalizationService.NormalizeLanguage(lang);
        LocalizationService.SetLanguage(lang);

        var cfg = _overlayConfigService.Load();
        cfg.Language = lang;
        _overlayConfigService.Save(cfg);
    }

    private void OnLanguageChanged(string lang)
    {
        Dispatch(() =>
        {
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged(nameof(IsRussian));
            OnPropertyChanged(nameof(LangRuBg));
            OnPropertyChanged(nameof(LangRuFg));
            OnPropertyChanged(nameof(LangRuFontWeight));
            OnPropertyChanged(nameof(LangEnBg));
            OnPropertyChanged(nameof(LangEnFg));
            OnPropertyChanged(nameof(LangEnFontWeight));

            OnPropertyChanged(nameof(LocGame));
            OnPropertyChanged(nameof(LocServer));
            OnPropertyChanged(nameof(LocTheme));
            OnPropertyChanged(nameof(LocLanguage));
            OnPropertyChanged(nameof(LocLanguageTooltip));
            OnPropertyChanged(nameof(LocMtr));
            OnPropertyChanged(nameof(LocMtrTooltip));
            OnPropertyChanged(nameof(LocIspTicketButton));
            OnPropertyChanged(nameof(LocIspTicketTooltip));
            OnPropertyChanged(nameof(LocNavIspTicketTitle));
            OnPropertyChanged(nameof(LocNavIspTicketDesc));
            OnPropertyChanged(nameof(LocCurrentPing));
            OnPropertyChanged(nameof(LocAvgPing));
            OnPropertyChanged(nameof(LocMinPing));
            OnPropertyChanged(nameof(LocMinBestSuffix));
            OnPropertyChanged(nameof(LocMaxPing));
            OnPropertyChanged(nameof(LocMaxPeakSuffix));
            OnPropertyChanged(nameof(LocPacketLoss));
            OnPropertyChanged(nameof(LocSpikes));
            OnPropertyChanged(nameof(LocSpikesCountSuffix));
            OnPropertyChanged(nameof(LocGraphTitle));
            OnPropertyChanged(nameof(LocLiveBadge));
            OnPropertyChanged(nameof(LocReturnToLive));
            OnPropertyChanged(nameof(LocReturnToLiveTooltip));
            OnPropertyChanged(nameof(LocChartBackTooltip));
            OnPropertyChanged(nameof(LocChartForwardTooltip));
            OnPropertyChanged(nameof(LocRouter));
            OnPropertyChanged(nameof(LocIsp));
            OnPropertyChanged(nameof(LocAnalyze));
            OnPropertyChanged(nameof(LocIcmpPing));
            OnPropertyChanged(nameof(LocInterval1m));
            OnPropertyChanged(nameof(LocInterval5m));
            OnPropertyChanged(nameof(LocInterval10m));
            OnPropertyChanged(nameof(LocInterval60m));
            OnPropertyChanged(nameof(LocColRegion));
            OnPropertyChanged(nameof(LocColCode));
            OnPropertyChanged(nameof(LocColPing));
            OnPropertyChanged(nameof(LocColRoute));
            OnPropertyChanged(nameof(LocBest));
            OnPropertyChanged(nameof(LocRefresh));
            OnPropertyChanged(nameof(LocHistoryTitle));
            OnPropertyChanged(nameof(LocColTime));
            OnPropertyChanged(nameof(LocColLoss));
            OnPropertyChanged(nameof(LocColStatus));
            OnPropertyChanged(nameof(LocColServer));
            OnPropertyChanged(nameof(LocHotkeyHint));
            OnPropertyChanged(nameof(LocGlowAmbient));
            OnPropertyChanged(nameof(LocGlowDota));
            OnPropertyChanged(nameof(LocGlowOff));
            OnPropertyChanged(nameof(LocHkGlowTooltip));
            OnPropertyChanged(nameof(LocNavSections));
            OnPropertyChanged(nameof(LocNavPingDesc));
            OnPropertyChanged(nameof(LocNavTmTitle));
            OnPropertyChanged(nameof(LocNavTmDesc));
            OnPropertyChanged(nameof(LocNavSpeedtestTitle));
            OnPropertyChanged(nameof(LocNavSpeedtestDesc));
            OnPropertyChanged(nameof(LocNavActive));
            OnPropertyChanged(nameof(LocNavMenuTooltip));
            OnPropertyChanged(nameof(LocGameComboTooltip));

            OnPropertyChanged(nameof(LocSpeedtestStart));
            OnPropertyChanged(nameof(LocSpeedtestCancel));
            OnPropertyChanged(nameof(LocSpeedtestTesting));
            OnPropertyChanged(nameof(LocSpeedtestIdle));
            OnPropertyChanged(nameof(LocSpeedtestDownload));
            OnPropertyChanged(nameof(LocSpeedtestUpload));
            OnPropertyChanged(nameof(LocSpeedtestPing));
            OnPropertyChanged(nameof(LocSpeedtestJitter));
            OnPropertyChanged(nameof(LocSpeedtestIsp));
            OnPropertyChanged(nameof(LocSpeedtestServer));
            OnPropertyChanged(nameof(LocSpeedtestClientIp));
            OnPropertyChanged(nameof(LocSpeedtestPeak));
            OnPropertyChanged(nameof(LocSpeedtestAverage));
            OnPropertyChanged(nameof(LocSpeedtestWaveGraph));
            OnPropertyChanged(nameof(LocSpeedtestHistoryTitle));
            OnPropertyChanged(nameof(LocSpeedtestClearHistory));
            OnPropertyChanged(nameof(LocSpeedtestNoHistory));
            OnPropertyChanged(nameof(LocSpeedtestMbps));
            OnPropertyChanged(nameof(LocSpeedtestColDownload));
            OnPropertyChanged(nameof(LocSpeedtestColPeak));
            OnPropertyChanged(nameof(LocSpeedtestColUpload));
            OnPropertyChanged(nameof(LocSpeedtestColPing));
            OnPropertyChanged(nameof(LocSpeedtestColJitter));
            OnPropertyChanged(nameof(LocSpeedtestColServer));
            UpdateSpeedtestLocalization();
            OnPropertyChanged(nameof(LocSearchPlaceholder));
            OnPropertyChanged(nameof(LocEndTask));
            OnPropertyChanged(nameof(LocEndTaskTooltip));
            OnPropertyChanged(nameof(LocFolder));
            OnPropertyChanged(nameof(LocFolderTooltip));
            OnPropertyChanged(nameof(LocRefreshTmTooltip));
            OnPropertyChanged(nameof(LocProcessorCpu));
            OnPropertyChanged(nameof(LocGraphicsGpu));
            OnPropertyChanged(nameof(LocMemoryRam));
            OnPropertyChanged(nameof(LocDiskActivity));
            OnPropertyChanged(nameof(LocNetwork));
            OnPropertyChanged(nameof(LocActiveProcesses));
            OnPropertyChanged(nameof(LocTotalCpuLoad));
            OnPropertyChanged(nameof(LocTotalGpuLoad));
            OnPropertyChanged(nameof(LocTotalDiskIo));
            OnPropertyChanged(nameof(LocSystem));
            OnPropertyChanged(nameof(LocColName));
            OnPropertyChanged(nameof(LocColPid));
            OnPropertyChanged(nameof(LocColCpuSort));
            OnPropertyChanged(nameof(LocColGpuSort));
            OnPropertyChanged(nameof(LocColRamSort));
            OnPropertyChanged(nameof(LocColDiskSort));
            OnPropertyChanged(nameof(LocColNetSort));
            OnPropertyChanged(nameof(LocColThreadsSort));
            OnPropertyChanged(nameof(LocColStateSort));

            OnPropertyChanged(nameof(LocDotaAutoMatchTooltip));
            OnPropertyChanged(nameof(LocDotaInMatch));
            OnPropertyChanged(nameof(LocDotaMatchServer));
            OnPropertyChanged(nameof(LocDotaMatchRelay));
            OnPropertyChanged(nameof(LocDotaMatchLoss));
            OnPropertyChanged(nameof(LocDotaMatchDuration));
            OnPropertyChanged(nameof(LocDotaLogWarningTitle));
            OnPropertyChanged(nameof(LocDotaLogWarningText));
            OnPropertyChanged(nameof(LocDotaLogWarningTip));
            OnPropertyChanged(nameof(LocDotaCopyCommand));
            OnPropertyChanged(nameof(LocDotaCopyCondebug));
            UpdateAutoMatchButtonState();

            OnPropertyChanged(nameof(OptimizationButtonText));
            OnPropertyChanged(nameof(OptimizationButtonToolTip));
            OnPropertyChanged(nameof(HeaderTitleText));
            OnPropertyChanged(nameof(HeaderSubtitleText));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(NavGamePingTitle));
            OnPropertyChanged(nameof(LocNetworkTweaksButton));
            OnPropertyChanged(nameof(LocNetworkTweaksTooltip));
            OnPropertyChanged(nameof(LocAddCustomServerTooltip));
            OnPropertyChanged(nameof(LocDeleteCustomServerTooltip));

            OnPropertyChanged(nameof(MenuLayoutHeader));
            OnPropertyChanged(nameof(MenuOpacityHeader));
            OnPropertyChanged(nameof(MenuPresetClassicBarHeader));
            OnPropertyChanged(nameof(MenuPresetMinimalHeader));
            OnPropertyChanged(nameof(MenuPresetVerticalStackHeader));
            OnPropertyChanged(nameof(MenuPresetNetGraphHeader));
            OnPropertyChanged(nameof(MenuPresetCyberHudHeader));
            OnPropertyChanged(nameof(MenuOpacity100Header));
            OnPropertyChanged(nameof(MenuOpacity85Header));
            OnPropertyChanged(nameof(MenuOpacity75Header));
            OnPropertyChanged(nameof(MenuOpacity50Header));
            OnPropertyChanged(nameof(MenuOpacity25Header));

            ChartSubtitleText = GetLocalizedChartSubtitle(ChartIntervalMinutes);
            AveragePeriodText = GetLocalizedAveragePeriod(ChartIntervalMinutes);
            HistoryCountText = GetRecordCountText(HistoryItems.Count);

            if (_lastRecordedPingMs > 0)
            {
                UpdatePingStatus(_lastRecordedPingMs);
            }
            else if (_isLastPingTimeout)
            {
                ApplyTimeoutState();
            }

            foreach (var item in HistoryItems)
            {
                if (item.RawIsTimeout || item.RawPingMs <= 0)
                {
                    item.Status = LocalizationService.Get("HistoryStatusTimeout");
                }
                else if (item.RawPingMs > 200)
                {
                    item.Status = LocalizationService.Get("HistoryStatusHigh");
                }
                else if (item.RawPingMs > 100 || item.RawIsSpike)
                {
                    item.Status = LocalizationService.Get("HistoryStatusSpike");
                }
                else
                {
                    item.Status = LocalizationService.Get("HistoryStatusStable");
                }
                item.RefreshLocalization();
            }

            // Обновляем локализованное название Linear Dark (По умолчанию / Default)
            foreach (var th in Themes)
            {
                if (th.Id == "Linear")
                {
                    th.Name = LocalizationService.IsRussian ? "Linear Dark (По умолчанию)" : "Linear Dark (Default)";
                }
                else if (th.Id == "MonochromeCyber")
                {
                    th.Description = LocalizationService.IsRussian
                        ? "Строгий неоновый нуар-киберпанк: глубокий матовый черный, графитовые подложки и неоновые белые контуры"
                        : "High-contrast monochrome cyberpunk: deep matte black, graphite surfaces and pure neon white accents";
                }
            }

            // Обновляем отображаемые названия серверов и подсказки (Россия -> Russia)
            foreach (var target in PingTargets)
            {
                target.RefreshLocalization();
            }

            // Обновляем телеметрию Диспетчера задач (КБ/с -> KB/s, процессов -> processes, потоков -> threads)
            if (_latestSnapshot != null)
            {
                OnTaskManagerSnapshotUpdated(_latestSnapshot);
            }
            else
            {
                TotalNetworkSubText = LocalizationService.IsRussian ? "Прием и передача" : "Send & receive";
                ProcessCountText = LocalizationService.IsRussian ? "0 процессов" : "0 processes";
                ThreadCountText = LocalizationService.IsRussian ? "0 потоков" : "0 threads";
            }

            foreach (var p in Processes)
            {
                p.RefreshLocalization();
            }

            // Обновляем SDR статус, таймер и локализованные маршруты (Прямой -> Direct, через -> via)
            if (_lastSdrResults != null && _lastSdrResults.Count > 0)
            {
                int validCount = _lastSdrResults.Count(x => x.PingMs > 0);
                UpdateSdrStatusDisplay(validCount > 0 ? validCount : _lastSdrResults.Count);
                UpdateSdrTimerDisplay();
            }
            foreach (var sdr in SdrItems)
            {
                sdr.RefreshLocalization();
            }

            UpdateOverlayButtonState();
            RefreshOverlayMenuHeaders();
        });
    }

    public Brush OptimizationStatusDotBrush => ThemeService.IsOptimizedMode
        ? CreateBrush(ThemeService.Current.AccentGreen)
        : CreateBrush(ThemeService.Current.TextMuted);

    public Brush OptimizationButtonBackground => ThemeService.IsOptimizedMode
        ? CreateBrush(ThemeService.Current.AccentGreenSoft)
        : (Brush)Application.Current.FindResource("BgCard");

    public Brush OptimizationButtonBorderBrush => ThemeService.IsOptimizedMode
        ? CreateBrush(ThemeService.Current.AccentGreen)
        : (Brush)Application.Current.FindResource("BorderColor");

    public Brush OptimizationButtonForeground => ThemeService.IsOptimizedMode
        ? CreateBrush(ThemeService.Current.AccentGreen)
        : (Brush)Application.Current.FindResource("TextSecondary");

    public CornerRadius CardCornerRadius => ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(8);
    public Thickness CardPadding => ThemeService.IsOptimizedMode ? new Thickness(8, 6, 8, 6) : new Thickness(12, 10, 12, 10);
    public CornerRadius ContainerCornerRadius => ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(10);
    public Thickness ContainerPadding => ThemeService.IsOptimizedMode ? new Thickness(6, 4, 6, 4) : new Thickness(12, 10, 12, 10);
    public CornerRadius TableHeaderCornerRadius => ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(6);
    public Thickness TableHeaderPadding => ThemeService.IsOptimizedMode ? new Thickness(8, 4, 8, 4) : new Thickness(10, 7, 10, 7);
    public double TelemetryBarHeight => ThemeService.IsOptimizedMode ? 2.0 : 4.0;

    public void SetWindowFocusState(bool isFocused)
    {
        _taskManagerService.SetWindowFocusState(isFocused);
    }

    [RelayCommand]
    public void ToggleOptimizationMode()
    {
        bool newMode = !ThemeService.IsOptimizedMode;
        ThemeService.SetOptimizationMode(newMode);
        var cfg = _overlayConfigService.Load();
        cfg.IsOptimizedMode = newMode;
        _overlayConfigService.Save(cfg);

        // Re-apply current theme to trigger visual changes in MainWindow
        OnThemeChanged(ThemeService.Current);
        if (SessionRecords.Count > 0)
        {
            GraphDataUpdated?.Invoke(SessionRecords.ToList());
        }
    }

    // =========================================================
    // ICMP PING LOGIC
    // =========================================================

    private async Task CheckPingAsync()
    {
        if (_isPingCheckRunning) return;
        _isPingCheckRunning = true;

        if (_initDbTask != null && !_initDbTask.IsCompleted)
        {
            try
            {
                await _initDbTask.ConfigureAwait(false);
            }
            catch { }
        }

        string targetIp = SelectedPingTarget?.Ip ?? "8.8.8.8";
        string targetName = SelectedPingTarget?.Name ?? "Google DNS (8.8.8.8)";

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(targetIp, 1000);

            // Если первичный адрес не ответил, выполняем попытку самовосстановления через резервный релей
            if (reply.Status != IPStatus.Success && SelectedPingTarget != null)
            {
                var altRelays = _dotaSdr.GetAlternativeRelays(SelectedPingTarget.Name, targetIp);
                foreach (var altIp in altRelays)
                {
                    try
                    {
                        using var altPing = new Ping();
                        var altReply = await altPing.SendPingAsync(altIp, 800);
                        if (altReply.Status == IPStatus.Success)
                        {
                            SelectedPingTarget.Ip = altIp;
                            targetIp = altIp;
                            reply = altReply;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (reply.Status == IPStatus.Success)
            {
                long rawPing = Math.Max(1, reply.RoundtripTime);
                long pingMs = rawPing;

                if (IsInDotaMatch && _dotaMatchTrackerService.CurrentMatch != null)
                {
                    int backhaul = _dotaMatchTrackerService.CurrentMatch.BackhaulLatencyMs;
                    if (backhaul > 0)
                    {
                        pingMs = rawPing + backhaul;
                    }
                    else if (_dotaMatchTrackerService.CurrentMatch.LastReportedTotalPingMs > 0 && rawPing < 15)
                    {
                        pingMs = _dotaMatchTrackerService.CurrentMatch.LastReportedTotalPingMs;
                    }
                }

                if (_lastRecordedPingMs > 0 && pingMs > 0)
                {
                    double diff = Math.Abs(pingMs - _lastRecordedPingMs);
                    _currentJitterMs = _currentJitterMs <= 0 ? diff : (_currentJitterMs * 0.85 + diff * 0.15);
                }
                _lastRecordedPingMs = pingMs;
                _isLastPingTimeout = false;
                SelectedPingTarget?.UpdatePing(pingMs);

                CurrentPingText = $"{pingMs} ms";
                bool isSpike = pingMs > 100;
                UpdatePingStatus(pingMs);

                if (IsInDotaMatch)
                {
                    RecordMatchProbe(isTimeout: false);
                }

                var (rPing, ispPing, incCategory, incDesc) = await _networkDiagnosticService.DiagnoseTickAsync(
                    targetIp, pingMs, false, isSpike);

                var record = await _database.SavePingAsync(
                    pingMs, isSpike, false, targetName,
                    routerPingMs: rPing,
                    ispPingMs: ispPing,
                    routerIp: _networkDiagnosticService.RouterIp,
                    ispIp: _networkDiagnosticService.IspIp,
                    incidentCategory: incCategory,
                    incidentDescription: incDesc);

                SessionRecords.Add(record);
                AddHistoryRecord(record, targetName);

                double displayLoss = IsInDotaMatch ? _matchRollingLoss : _currentLossPercent;
                _overlayWindow?.UpdateLivePing(pingMs, false, isSpike, displayLoss, _currentJitterMs);
            }
            else
            {
                // Если мы в активном матче Dota 2, но данный конкретный релей блокирует входящие ICMP эхо-запросы:
                if (IsInDotaMatch && _dotaMatchTrackerService.CurrentMatch?.LastReportedTotalPingMs > 0)
                {
                    long fallbackPing = _dotaMatchTrackerService.CurrentMatch.LastReportedTotalPingMs;
                    _lastRecordedPingMs = fallbackPing;
                    _isLastPingTimeout = false;
                    SelectedPingTarget?.UpdatePing(fallbackPing);
                    CurrentPingText = $"{fallbackPing} ms";
                    bool isSpike = fallbackPing > 100;
                    UpdatePingStatus(fallbackPing);
                    RecordMatchProbe(isTimeout: false);
                    double displayLoss = _matchRollingLoss;
                    _overlayWindow?.UpdateLivePing(fallbackPing, false, isSpike, displayLoss, _currentJitterMs);
                }
                else
                {
                    _lastRecordedPingMs = -1;
                    _isLastPingTimeout = true;
                    SelectedPingTarget?.UpdatePing(-1);
                    ApplyTimeoutState();

                    if (IsInDotaMatch)
                    {
                        RecordMatchProbe(isTimeout: true);
                    }

                    var (rPing, ispPing, incCategory, incDesc) = await _networkDiagnosticService.DiagnoseTickAsync(
                        targetIp, -1, true, false);

                    var record = await _database.SavePingAsync(
                        -1, false, true, targetName,
                        routerPingMs: rPing,
                        ispPingMs: ispPing,
                        routerIp: _networkDiagnosticService.RouterIp,
                        ispIp: _networkDiagnosticService.IspIp,
                        incidentCategory: incCategory,
                        incidentDescription: incDesc);

                    SessionRecords.Add(record);
                    AddHistoryRecord(record, targetName);

                    double displayLoss = IsInDotaMatch ? _matchRollingLoss : _currentLossPercent;
                    _overlayWindow?.UpdateLivePing(-1, true, false, displayLoss, _currentJitterMs);
                }
            }

            UpdateStatisticsFromSession();
        }
        catch
        {
            _lastRecordedPingMs = -1;
            _isLastPingTimeout = true;
            SelectedPingTarget?.UpdatePing(-1);
            ApplyTimeoutState("Ошибка соединения");

            if (IsInDotaMatch)
            {
                RecordMatchProbe(isTimeout: true);
            }

            var (rPing, ispPing, incCategory, incDesc) = await _networkDiagnosticService.DiagnoseTickAsync(
                targetIp, -1, true, false);

            var record = await _database.SavePingAsync(
                -1, false, true, targetName,
                routerPingMs: rPing,
                ispPingMs: ispPing,
                routerIp: _networkDiagnosticService.RouterIp,
                ispIp: _networkDiagnosticService.IspIp,
                incidentCategory: incCategory,
                incidentDescription: incDesc);

            SessionRecords.Add(record);
            AddHistoryRecord(record, targetName);

            double displayLoss = IsInDotaMatch ? _matchRollingLoss : _currentLossPercent;
            _overlayWindow?.UpdateLivePing(-1, true, false, displayLoss, _currentJitterMs);

            UpdateStatisticsFromSession();
        }
        finally
        {
            _isPingCheckRunning = false;
        }
    }

    private void RecordMatchProbe(bool isTimeout)
    {
        _matchRecentProbes.Enqueue(isTimeout);
        while (_matchRecentProbes.Count > 30)
        {
            _matchRecentProbes.Dequeue();
        }

        int dropped = _matchRecentProbes.Count(x => x);
        _matchRollingLoss = _matchRecentProbes.Count > 0 ? ((double)dropped / _matchRecentProbes.Count * 100.0) : 0.0;
        DotaMatchLossText = $"{_matchRollingLoss:F1}%";

        string lossColor = _matchRollingLoss == 0
            ? ThemeService.Current.AccentGreen
            : (_matchRollingLoss <= 2.0 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed);
        DotaMatchLossForeground = CreateBrush(lossColor);
    }

    [RelayCommand]
    public void ToggleAutoDetectDotaMatch()
    {
        IsAutoDetectDotaMatchEnabled = !IsAutoDetectDotaMatchEnabled;
        _dotaMatchTrackerService.IsEnabled = IsAutoDetectDotaMatchEnabled;
        UpdateAutoMatchButtonState();

        if (!IsAutoDetectDotaMatchEnabled && IsInDotaMatch)
        {
            OnDotaMatchDisconnected();
        }

        if (!IsAutoDetectDotaMatchEnabled)
        {
            DotaLogWarningVisibility = Visibility.Collapsed;
        }
        else if (_dotaMatchTrackerService.IsDotaRunningWithoutLog && !IsInDotaMatch)
        {
            DotaLogWarningVisibility = Visibility.Visible;
        }

        var cfg = _overlayConfigService.Load();
        cfg.AutoDetectDotaMatch = IsAutoDetectDotaMatchEnabled;
        _overlayConfigService.Save(cfg);
    }

    private void UpdateAutoMatchButtonState()
    {
        if (IsInDotaMatch)
        {
            string code = _dotaMatchTrackerService.CurrentMatch?.ClusterCode?.ToUpperInvariant() ?? "DOTA";
            AutoMatchButtonText = LocalizationService.IsRussian
                ? $"🟢 В катке: {code}"
                : $"🟢 In Match: {code}";
            AutoMatchButtonBackground = CreateBrush(ThemeService.Current.AccentGreenSoft);
            AutoMatchButtonBorderBrush = CreateBrush(ThemeService.Current.AccentGreen);
            AutoMatchButtonForeground = CreateBrush(ThemeService.Current.AccentGreen);
        }
        else if (IsAutoDetectDotaMatchEnabled)
        {
            AutoMatchButtonText = LocalizationService.Get("DotaAutoMatchActive");
            AutoMatchButtonBackground = CreateBrush(ThemeService.Current.BgCard);
            AutoMatchButtonBorderBrush = CreateBrush(ThemeService.Current.BorderHover);
            AutoMatchButtonForeground = CreateBrush(ThemeService.Current.AccentGreen);
        }
        else
        {
            AutoMatchButtonText = LocalizationService.Get("DotaAutoMatchInactive");
            AutoMatchButtonBackground = CreateBrush(ThemeService.Current.BgCard);
            AutoMatchButtonBorderBrush = CreateBrush(ThemeService.Current.BorderColor);
            AutoMatchButtonForeground = CreateBrush(ThemeService.Current.TextMuted);
        }
    }

    private void OnDotaMatchConnected(DotaMatchInfo info)
    {
        Dispatch(() =>
        {
            if (!IsAutoDetectDotaMatchEnabled) return;

            // Сохраняем текущий выбранный пользователем сервер, если еще не в матче
            if (!IsInDotaMatch && SelectedPingTarget != null && SelectedPingTarget != _matchPingTarget)
            {
                _userSavedPingTarget = SelectedPingTarget;
            }

            IsInDotaMatch = true;
            _matchStartTime = info.ConnectedAt;
            _matchRecentProbes.Clear();
            _matchRollingLoss = 0.0;

            string targetClusterCode = !string.IsNullOrWhiteSpace(info.TargetClusterCode) ? info.TargetClusterCode : info.ClusterCode;
            string targetClusterName = !string.IsNullOrWhiteSpace(info.TargetClusterDisplayName) ? info.TargetClusterDisplayName : info.ClusterDisplayName;
            string targetName = $"🎮 В матче: {targetClusterName} ({info.RelayIp})";

            if (_matchPingTarget == null)
            {
                _matchPingTarget = new PingTargetItemViewModel
                {
                    Name = targetName,
                    Ip = info.RelayIp
                };
            }
            else
            {
                _matchPingTarget.Name = targetName;
                _matchPingTarget.Ip = info.RelayIp;
            }

            if (!PingTargets.Contains(_matchPingTarget))
            {
                PingTargets.Insert(0, _matchPingTarget);
            }

            SelectedPingTarget = _matchPingTarget;

            DotaMatchBannerVisibility = Visibility.Visible;
            DotaMatchBadgeVisibility = Visibility.Visible;
            DotaLogWarningVisibility = Visibility.Collapsed;
            DotaMatchBadgeText = targetClusterCode.ToUpperInvariant();
            DotaMatchServerText = targetClusterName;
            DotaMatchRelayText = !string.IsNullOrWhiteSpace(info.RouteDescription) ? info.RouteDescription : $"{info.RelayIp}:{info.RelayPort}";
            DotaMatchLossText = "0.0%";
            DotaMatchLossForeground = CreateBrush(ThemeService.Current.AccentGreen);
            DotaMatchDurationText = "00:00";
            DotaMatchTooltip = $"{LocalizationService.Get("DotaMatchServerLabel")} {targetClusterName} ({info.RelayIp}:{info.RelayPort})";

            _matchDurationTimer.Start();
            UpdateAutoMatchButtonState();

            _overlayWindow?.UpdateMatchStatus(true, targetClusterCode, info.RelayIp);
            _overlayWindow?.UpdateServerCode(targetClusterCode);

            _ = CheckPingAsync();
        });
    }

    private void OnDotaMatchDisconnected()
    {
        Dispatch(() =>
        {
            if (!IsInDotaMatch) return;

            IsInDotaMatch = false;
            DotaMatchBannerVisibility = Visibility.Collapsed;
            DotaMatchBadgeVisibility = Visibility.Collapsed;
            _matchDurationTimer.Stop();

            if (_matchPingTarget != null && PingTargets.Contains(_matchPingTarget))
            {
                PingTargets.Remove(_matchPingTarget);
            }

            if (_userSavedPingTarget != null && PingTargets.Contains(_userSavedPingTarget))
            {
                SelectedPingTarget = _userSavedPingTarget;
            }
            else if (PingTargets.Count > 0)
            {
                SelectedPingTarget = PingTargets[0];
            }

            if (_dotaMatchTrackerService.IsDotaRunningWithoutLog && IsAutoDetectDotaMatchEnabled)
            {
                DotaLogWarningVisibility = Visibility.Visible;
            }

            UpdateAutoMatchButtonState();
            _overlayWindow?.UpdateMatchStatus(false, "", "");
            _overlayWindow?.UpdateServerCode(GetServerRegionCode(SelectedPingTarget));

            _ = CheckPingAsync();
        });
    }

    [RelayCommand]
    public async Task CopyConLogAsync()
    {
        try
        {
            Clipboard.SetText("con_logfile console.log");
            CopyConLogStatusText = LocalizationService.IsRussian ? "✓ Скопировано!" : "✓ Copied!";
            await Task.Delay(2500);
            CopyConLogStatusText = "";
        }
        catch { }
    }

    [RelayCommand]
    public async Task CopyConDebugAsync()
    {
        try
        {
            Clipboard.SetText("-condebug");
            CopyConLogStatusText = LocalizationService.IsRussian ? "✓ Скопировано -condebug!" : "✓ Copied -condebug!";
            await Task.Delay(2500);
            CopyConLogStatusText = "";
        }
        catch { }
    }

    [RelayCommand]
    public void DismissDotaLogWarning()
    {
        DotaLogWarningVisibility = Visibility.Collapsed;
    }

    private async Task<long> MeasureTargetWithFallbackAsync(PingTargetItemViewModel target)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Ip, 800).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                return Math.Max(1, reply.RoundtripTime);
            }
        }
        catch { }

        // Если основной IP не ответил, опрашиваем известные альтернативные релеи этого узла
        var altRelays = _dotaSdr.GetAlternativeRelays(target.Name, target.Ip);
        foreach (var altIp in altRelays)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(altIp, 600).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    target.Ip = altIp; // Самовосстановление: обновляем целевой IP на рабочий
                    return Math.Max(1, reply.RoundtripTime);
                }
            }
            catch { }
        }

        return -1;
    }

    /// <summary>
    /// Параллельный фоновый замер задержки для всех доступных серверов текущей игры
    /// </summary>
    [RelayCommand]
    public async Task RefreshAllTargetsPingAsync()
    {
        int runId = Interlocked.Increment(ref _refreshTargetsRunId);
        try
        {
            var targets = PingTargets.ToList();
            if (targets.Count == 0) return;

            const int batchSize = 3;
            for (int i = 0; i < targets.Count; i += batchSize)
            {
                if (runId != _refreshTargetsRunId) return;
                var batch = targets.Skip(i).Take(batchSize).ToList();
                var batchTasks = batch.Select(async target =>
                {
                    try
                    {
                        long ms = await MeasureTargetWithFallbackAsync(target).ConfigureAwait(false);
                        if (runId == _refreshTargetsRunId)
                        {
                            target.UpdatePing(ms);
                        }
                    }
                    catch
                    {
                        if (runId == _refreshTargetsRunId)
                        {
                            target.UpdatePing(-1);
                        }
                    }
                });

                await Task.WhenAll(batchTasks).ConfigureAwait(false);

                if (i + batchSize < targets.Count)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
        }
        catch { }
    }

    private void UpdatePingStatus(long pingMs)
    {
        var (hex, softHex) = ThemeService.GetPingColor(pingMs);
        var brush = CreateBrush(hex);
        var softBrush = CreateBrush(softHex);

        CurrentPingForeground = brush;
        StatusBadgeBackground = softBrush;
        StatusBadgeBorderBrush = brush;

        if (pingMs <= 100)
        {
            StatusText = LocalizationService.Get("StatusStable");
        }
        else if (pingMs <= 200)
        {
            StatusText = LocalizationService.Get("StatusModerate");
        }
        else
        {
            StatusText = LocalizationService.Get("StatusHigh");
        }
    }

    private void ApplyTimeoutState(string? message = null)
    {
        CurrentPingText = "-- ms";
        var redBrush = CreateBrush(ThemeService.Current.AccentRed);
        var redSoft = CreateBrush(ThemeService.Current.AccentRedSoft);

        CurrentPingForeground = redBrush;
        StatusText = message ?? LocalizationService.Get("StatusTimeout");
        StatusBadgeBackground = redSoft;
        StatusBadgeBorderBrush = redBrush;
    }

    // =========================================================
    // DATA & STATS
    // =========================================================

    private void UpdateStatisticsFromSession()
    {
        var records = SessionRecords;
        var validRecords = new List<PingRecord>(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (!r.IsTimeout && r.PingMs > 0)
            {
                validRecords.Add(r);
            }
        }

        var allList = records.ToList();
        _lastAllRecords = allList;
        _lastValidRecords = validRecords;

        if (allList.Count > 0)
        {
            UpdateStatistics(allList, validRecords);
        }
        else
        {
            ResetStatistics();
        }

        UpdateChartLiveStatus();
        GraphDataUpdated?.Invoke(allList);
    }

    public async Task LoadDataAsync()
    {
        var records = await _database.GetRecentByMinutesAsync(ChartIntervalMinutes);
        var validRecords = records
            .Where(x => !x.IsTimeout && x.PingMs > 0)
            .ToList();

        _lastAllRecords = records;
        _lastValidRecords = validRecords;

        if (records.Count > 0)
        {
            UpdateStatistics(records, validRecords);
        }
        else
        {
            ResetStatistics();
        }

        UpdateChartLiveStatus();
        GraphDataUpdated?.Invoke(SessionRecords.Count > 0 ? SessionRecords.ToList() : records);
    }


    private void UpdateStatistics(List<PingRecord> allRecords, List<PingRecord> validRecords)
    {
        int total = allRecords.Count;
        int timeouts = allRecords.Count(x => x.IsTimeout || x.PingMs <= 0);
        double lossPercent = total > 0 ? ((double)timeouts / total * 100.0) : 0.0;
        _currentLossPercent = lossPercent;

        string lossColorHex = lossPercent == 0
            ? ThemeService.Current.AccentGreen
            : (lossPercent <= 2.0 ? ThemeService.Current.AccentYellow : ThemeService.Current.AccentRed);

        PacketLossText = $"{lossPercent:F1}%";
        PacketLossSubText = LocalizationService.IsRussian
            ? $"({timeouts} из {total})"
            : $"({timeouts} of {total})";
        PacketLossForeground = CreateBrush(lossColorHex);

        if (validRecords.Count > 0)
        {
            double average = validRecords.Average(x => x.PingMs);
            long minimum = validRecords.Min(x => x.PingMs);
            long maximum = validRecords.Max(x => x.PingMs);
            int spikes = validRecords.Count(x => x.IsSpike);

            AveragePingText = $"{average:F0} ms";
            var (avgColor, _) = ThemeService.GetPingColor((long)Math.Round(average));
            AveragePingForeground = CreateBrush(avgColor);

            MinPingText = $"{minimum} ms";
            var (minColor, _) = ThemeService.GetPingColor(minimum);
            MinPingForeground = CreateBrush(minColor);

            MaxPingText = $"{maximum} ms";
            var (maxColor, _) = ThemeService.GetPingColor(maximum);
            MaxPingForeground = CreateBrush(maxColor);

            SpikesCountText = spikes.ToString();
            SpikesCountForeground = spikes > 0
                ? CreateBrush(ThemeService.Current.AccentYellow)
                : CreateBrush(ThemeService.Current.AccentGreen);
        }
        else
        {
            ResetPingMetrics();
        }
    }

    private void ResetStatistics()
    {
        _currentLossPercent = 0.0;
        PacketLossText = "0.0%";
        PacketLossSubText = LocalizationService.IsRussian ? "(0 из 0)" : "(0 of 0)";
        PacketLossForeground = CreateBrush(ThemeService.Current.AccentGreen);
        ResetPingMetrics();
    }

    private void ResetPingMetrics()
    {
        AveragePingText = "-- ms";
        AveragePingForeground = CreateBrush(ThemeService.Current.TextPrimary);
        MinPingText = "-- ms";
        MinPingForeground = CreateBrush(ThemeService.Current.AccentGreen);
        MaxPingText = "-- ms";
        MaxPingForeground = CreateBrush(ThemeService.Current.AccentRed);
        SpikesCountText = "0";
        SpikesCountForeground = CreateBrush(ThemeService.Current.AccentGreen);
    }

    public void UpdateChartLiveStatus(bool isHovering = false)
    {
        if (isHovering)
        {
            ChartLiveStatusVisibility = Visibility.Collapsed;
            return;
        }

        bool isTimeout = _isLastPingTimeout || _lastRecordedPingMs <= 0;
        if (isTimeout && _lastRecordedPingMs != 0)
        {
            var red = ThemeService.Current.AccentRed;
            var redSoft = ThemeService.Current.AccentRedSoft;
            ChartLiveStatusBackground = CreateBrush(redSoft);
            ChartLiveStatusBorderBrush = CreateBrush(red);
            ChartLiveStatusForeground = CreateBrush(red);
            ChartLiveStatusText = $"-- ms ({LocalizationService.Get("StatusTimeout")})";
            ChartLiveStatusVisibility = Visibility.Visible;
        }
        else
        {
            ChartLiveStatusVisibility = Visibility.Collapsed;
        }
    }

    // =========================================================
    // HISTORY
    // =========================================================

    private void AddHistoryRecord(PingRecord record, string? serverName = null)
    {
        string pingStr;
        string hexColor;
        string softHexColor;
        string statusStr;
        string lossStr;
        string lossColor;

        if (record.IsTimeout || record.PingMs <= 0)
        {
            pingStr = "-- ms";
            hexColor = ThemeService.Current.AccentRed;
            softHexColor = ThemeService.Current.AccentRedSoft;
            statusStr = LocalizationService.Get("HistoryStatusTimeout");
            lossStr = "100%";
            lossColor = ThemeService.Current.AccentRed;
        }
        else
        {
            pingStr = $"{record.PingMs} ms";
            var colors = ThemeService.GetPingColor(record.PingMs);
            hexColor = colors.HexColor;
            softHexColor = colors.SoftHexColor;

            if (record.PingMs > 200)
            {
                statusStr = LocalizationService.Get("HistoryStatusHigh");
            }
            else if (record.PingMs > 100 || record.IsSpike)
            {
                statusStr = LocalizationService.Get("HistoryStatusSpike");
            }
            else
            {
                statusStr = LocalizationService.Get("HistoryStatusStable");
            }

            lossStr = "0%";
            lossColor = ThemeService.Current.AccentGreen;
        }

        string finalServer = !string.IsNullOrEmpty(serverName)
            ? serverName
            : (!string.IsNullOrEmpty(record.Server) ? record.Server : (SelectedPingTarget?.Name ?? ""));

        var newItem = new HistoryItemViewModel
        {
            RawPingMs = record.PingMs,
            RawIsTimeout = record.IsTimeout || record.PingMs <= 0,
            RawIsSpike = record.IsSpike,
            Time = record.Timestamp.ToString("HH:mm:ss"),
            Ping = pingStr,
            PingColor = hexColor,
            PacketLoss = lossStr,
            PacketLossColor = lossColor,
            Status = statusStr,
            StatusColor = hexColor,
            StatusBg = softHexColor,
            Server = finalServer
        };

        RequestHistoryAutoScroll?.Invoke();
        HistoryItems.Insert(0, newItem);
        HistoryCountText = GetRecordCountText(HistoryItems.Count);
    }

    private static string GetRecordCountText(int count)
    {
        if (LocalizationService.IsRussian)
        {
            int mod100 = count % 100;
            int mod10 = count % 10;

            string word;
            if (mod100 >= 11 && mod100 <= 19)
            {
                word = "замеров";
            }
            else if (mod10 == 1)
            {
                word = "замер";
            }
            else if (mod10 >= 2 && mod10 <= 4)
            {
                word = "замера";
            }
            else
            {
                word = "замеров";
            }

            return $"Сессия: {count} {word}";
        }
        else
        {
            return count == 1 ? $"Session: {count} ping" : $"Session: {count} pings";
        }
    }

    // =========================================================
    // SDR MONITORING
    // =========================================================

    private void StartDotaSdrMonitoring()
    {
        string connMsg = _gameService.CurrentGame.SteamAppId > 0
            ? "Подключение к Valve SDR Network..."
            : "Подключение к AWS Global Backbone...";
        SetSdrSuccess(connMsg, "Синхронизация серверов...");

        var initialResults = _dotaSdr.GetLatestSdrPings();
        if (initialResults.Count > 0)
        {
            UpdateDotaSdrList(initialResults);
        }

        _ = Task.Run(async () =>
        {
            await _dotaSdr.StartAsync();
        });
    }

    private void StartSdrCountdown()
    {
        _sdrCountdown = 15;
        UpdateSdrTimerDisplay();
    }

    private async void UpdateSdrCountdown()
    {
        _sdrCountdown--;

        if (_sdrCountdown <= 0)
        {
            _sdrCountdown = 15;
            UpdateSdrTimerDisplay();
            await _dotaSdr.RefreshAsync();
            return;
        }

        UpdateSdrTimerDisplay();
    }

    private void UpdateSdrTimerDisplay()
    {
        if (_sdrCountdown <= 0)
        {
            SdrTimerText = LocalizationService.IsRussian ? "Замер SDR..." : "Measuring SDR...";
        }
        else
        {
            SdrTimerText = LocalizationService.IsRussian
                ? $"Следующее обновление: {_sdrCountdown} сек"
                : $"Next update: {_sdrCountdown} s";
        }
    }

    private void UpdateSdrStatusDisplay(int serverCount)
    {
        string apiLabel = _gameService.CurrentGame.SteamAppId > 0 ? "Valve SDR API" : _gameService.CurrentGame.SdrHeaderTitle;
        string sdrServersWord = LocalizationService.IsRussian ? "серверов" : "servers";
        SdrStatusForeground = CreateBrush("#9EA6B3");
        SdrStatusText = $"{apiLabel}: {serverCount} {sdrServersWord}";
        SdrTimerForeground = CreateBrush("#5C6470");
        BestSdrForeground = CreateBrush("#59D499");
    }

    private void OnDotaSdrPingUpdated(List<SdrPingResult> results)
    {
        Dispatch(() =>
        {
            if (results == null || results.Count == 0)
            {
                string errMsg = _gameService.CurrentGame.SteamAppId > 0
                    ? (LocalizationService.IsRussian ? "Нет доступных серверов SDR" : "No SDR servers available")
                    : (LocalizationService.IsRussian ? "Нет доступных регионов AWS" : "No AWS regions available");
                string hint = LocalizationService.IsRussian ? "Проверьте подключение к сети" : "Check network connection";
                SetSdrError(errMsg, hint);
                return;
            }

            UpdateDotaSdrList(results);
            _sdrCountdown = 15;
            UpdateSdrStatusDisplay(results.Count);
            UpdateSdrTimerDisplay();
        });
    }

    private void UpdateDotaSdrList(List<SdrPingResult> results)
    {
        _lastSdrResults = results;

        if (results == null || results.Count == 0)
        {
            SetSdrError("Нет доступных серверов SDR", "Проверьте сетевое соединение");
            return;
        }

        var validRows = results
            .Where(x => x.PingMs > 0)
            .OrderBy(x => x.PingMs)
            .Select(x =>
            {
                var (color, softColor) = ThemeService.GetPingColor(x.PingMs);
                string regionName = !string.IsNullOrEmpty(x.RegionName)
                    ? x.RegionName
                    : GetRegionName(x.Code);

                return new DotaSdrItemViewModel
                {
                    Name = regionName,
                    Code = x.Code.ToUpper(),
                    Ping = $"{x.PingMs} ms",
                    Route = x.Route,
                    PingColor = color,
                    PingBg = softColor,
                    RawPingMs = x.PingMs
                };
            })
            .ToList();

        if (validRows.Count == 0)
        {
            SetSdrError("Все серверы вернули нулевой пинг", "Проверьте интернет-соединение");
            return;
        }

        if (SdrItems.Count == validRows.Count)
        {
            for (int i = 0; i < validRows.Count; i++)
            {
                var src = validRows[i];
                var target = SdrItems[i];
                target.Name = src.Name;
                target.Code = src.Code;
                target.Ping = src.Ping;
                target.Route = src.Route;
                target.PingColor = src.PingColor;
                target.PingBg = src.PingBg;
                target.RawPingMs = src.RawPingMs;
            }
        }
        else
        {
            SdrItems.Clear();
            foreach (var row in validRows)
            {
                SdrItems.Add(row);
            }
        }

        var best = validRows.First();
        long bestPing = results.Where(x => x.PingMs > 0).Min(x => x.PingMs);
        var (bestColor, _) = ThemeService.GetPingColor(bestPing);
        BestSdrForeground = CreateBrush(bestColor);
        BestSdrPingText = best.Ping;

        _overlayWindow?.UpdateBestSdr(best.Code, best.Name, bestPing);
        UpdateSdrStatusDisplay(validRows.Count);
        UpdateSdrTimerDisplay();
    }

    private static string GetRegionName(string code)
    {
        if (RegionNames.TryGetValue(code.ToLower(), out string? name))
        {
            return name;
        }
        return code.ToUpper();
    }

    public string GetServerRegionCode(PingTargetItemViewModel? target)
    {
        if (target == null) return "FRA";

        if (IsInDotaMatch && target == _matchPingTarget && !string.IsNullOrWhiteSpace(DotaMatchBadgeText))
        {
            return DotaMatchBadgeText;
        }

        // 1. Попробуем сопоставить по IP в пуле Valve POP текущей дисциплины
        if (_gameService.CurrentGame?.DefaultPops != null)
        {
            var matchedPop = _gameService.CurrentGame.DefaultPops
                .FirstOrDefault(p => p.Relays.Contains(target.Ip, StringComparer.OrdinalIgnoreCase));
            if (matchedPop != null && !string.IsNullOrWhiteSpace(matchedPop.Code))
            {
                return matchedPop.Code.ToUpperInvariant();
            }
        }

        string name = target.Name ?? string.Empty;

        // 2. Специфические ключевые слова и распознавание городов
        if (name.Contains("Stockholm", StringComparison.OrdinalIgnoreCase) || name.Contains("Стокгольм", StringComparison.OrdinalIgnoreCase))
            return "STO";
        if (name.Contains("Frankfurt", StringComparison.OrdinalIgnoreCase) || name.Contains("Франкфурт", StringComparison.OrdinalIgnoreCase))
            return "FRA";
        if (name.Contains("Vienna", StringComparison.OrdinalIgnoreCase) || name.Contains("Вена", StringComparison.OrdinalIgnoreCase))
            return "VIE";
        if (name.Contains("Warsaw", StringComparison.OrdinalIgnoreCase) || name.Contains("Варшава", StringComparison.OrdinalIgnoreCase))
            return "WAW";
        if (name.Contains("Amsterdam", StringComparison.OrdinalIgnoreCase) || name.Contains("Амстердам", StringComparison.OrdinalIgnoreCase))
            return "AMS";
        if (name.Contains("London", StringComparison.OrdinalIgnoreCase) || name.Contains("Лондон", StringComparison.OrdinalIgnoreCase))
            return "LHR";
        if (name.Contains("Paris", StringComparison.OrdinalIgnoreCase) || name.Contains("Париж", StringComparison.OrdinalIgnoreCase))
            return "PAR";
        if (name.Contains("Madrid", StringComparison.OrdinalIgnoreCase) || name.Contains("Мадрид", StringComparison.OrdinalIgnoreCase))
            return "MAD";
        if (name.Contains("Helsinki", StringComparison.OrdinalIgnoreCase) || name.Contains("Хельсинки", StringComparison.OrdinalIgnoreCase))
            return "HEL";
        if (name.Contains("Dubai", StringComparison.OrdinalIgnoreCase) || name.Contains("Дубай", StringComparison.OrdinalIgnoreCase))
            return "DXB";
        if (name.Contains("Singapore", StringComparison.OrdinalIgnoreCase) || name.Contains("Сингапур", StringComparison.OrdinalIgnoreCase))
            return "SGP";
        if (name.Contains("Tokyo", StringComparison.OrdinalIgnoreCase) || name.Contains("Токио", StringComparison.OrdinalIgnoreCase))
            return "TYO";
        if (name.Contains("Seoul", StringComparison.OrdinalIgnoreCase) || name.Contains("Сеул", StringComparison.OrdinalIgnoreCase))
            return "SEO";
        if (name.Contains("Hong Kong", StringComparison.OrdinalIgnoreCase) || name.Contains("Гонконг", StringComparison.OrdinalIgnoreCase))
            return "HKG";
        if (name.Contains("Sydney", StringComparison.OrdinalIgnoreCase) || name.Contains("Сидней", StringComparison.OrdinalIgnoreCase))
            return "SYD";
        if (name.Contains("Virginia", StringComparison.OrdinalIgnoreCase) || name.Contains("Washington", StringComparison.OrdinalIgnoreCase))
            return "IAD";
        if (name.Contains("Chicago", StringComparison.OrdinalIgnoreCase) || name.Contains("Чикаго", StringComparison.OrdinalIgnoreCase))
            return "ORD";
        if (name.Contains("Los Angeles", StringComparison.OrdinalIgnoreCase) || name.Contains("Лос-Анджелес", StringComparison.OrdinalIgnoreCase))
            return "LAX";
        if (name.Contains("Seattle", StringComparison.OrdinalIgnoreCase) || name.Contains("Сиэтл", StringComparison.OrdinalIgnoreCase))
            return "SEA";
        if (name.Contains("Москва", StringComparison.OrdinalIgnoreCase) || name.Contains("Moscow", StringComparison.OrdinalIgnoreCase) || name.Contains("RU1", StringComparison.OrdinalIgnoreCase))
            return "MSK";
        if (name.Contains("Красноярск", StringComparison.OrdinalIgnoreCase))
            return "KJA";
        if (name.Contains("Екатеринбург", StringComparison.OrdinalIgnoreCase))
            return "SVX";
        if (name.Contains("Хабаровск", StringComparison.OrdinalIgnoreCase))
            return "KHV";
        if (name.Contains("Google", StringComparison.OrdinalIgnoreCase) || target.Ip == "8.8.8.8")
            return "DNS";
        if (name.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase) || target.Ip == "1.1.1.1")
            return "CF";

        // 3. Сопоставим с известными названиями регионов из словаря
        foreach (var kvp in RegionNames)
        {
            if (name.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Key.StartsWith("eu-north", StringComparison.OrdinalIgnoreCase)) return "STO";
                if (kvp.Key.StartsWith("eu-central", StringComparison.OrdinalIgnoreCase)) return "FRA";
                if (kvp.Key.StartsWith("eu-west-1", StringComparison.OrdinalIgnoreCase)) return "IRL";
                if (kvp.Key.StartsWith("eu-west-2", StringComparison.OrdinalIgnoreCase)) return "LHR";
                if (kvp.Key.StartsWith("us-east", StringComparison.OrdinalIgnoreCase)) return "IAD";
                if (kvp.Key.StartsWith("us-west", StringComparison.OrdinalIgnoreCase)) return "PDX";
                return kvp.Key.ToUpperInvariant();
            }
        }

        return "FRA";
    }

    private void SetSdrError(string message, string hint = "")
    {
        var redBrush = CreateBrush("#FF6161");
        SdrStatusForeground = redBrush;
        SdrStatusText = $"⚠ Ошибка: {message}";

        if (!string.IsNullOrEmpty(hint))
        {
            SdrTimerForeground = redBrush;
            SdrTimerText = hint;
        }

        if (SdrItems.Count == 0)
        {
            BestSdrForeground = redBrush;
            BestSdrPingText = "Ошибка";
        }
    }

    private void SetSdrSuccess(string message, string timerInfo = "")
    {
        SdrStatusForeground = CreateBrush("#9EA6B3");
        SdrStatusText = message;

        SdrTimerForeground = CreateBrush("#5C6470");
        if (!string.IsNullOrEmpty(timerInfo))
        {
            SdrTimerText = timerInfo;
        }

        BestSdrForeground = CreateBrush("#59D499");
    }

    // =========================================================
    // COMMANDS
    // =========================================================

    [RelayCommand]
    public async Task RefreshSdrAsync()
    {
        CanRefreshSdr = false;
        string refreshMsg = _gameService.CurrentGame.SteamAppId > 0
            ? "Опрашиваем серверы Valve SDR напрямую..."
            : "Опрашиваем регионы AWS Cloud напрямую...";
        SetSdrSuccess(refreshMsg, "Замер задержки...");

        try
        {
            await _dotaSdr.RefreshAsync();
        }
        catch (Exception ex)
        {
            SetSdrError("Ошибка опроса SDR", ex.Message);
        }
        finally
        {
            await Task.Delay(400);
            CanRefreshSdr = true;
        }
    }

    [RelayCommand]
    public void ToggleOverlay()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleVisibility();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleLock()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleLock();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleSparkline()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleSparkline();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleFps()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleFps();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleCpu()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleCpu();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleGpu()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleGpu();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleRam()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleRam();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void ToggleGlow()
    {
        EnsureOverlayCreated();
        _overlayWindow?.ToggleGlow();
        UpdateOverlayButtonState();
    }

    [RelayCommand]
    public void SetScalePreset(object? parameter)
    {
        if (parameter is double scale || (parameter is string str && double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out scale)))
        {
            EnsureOverlayCreated();
            _overlayWindow?.SetScale(scale);
            var cfg = _overlayConfigService.Load();
            cfg.Scale = scale;
            _overlayConfigService.Save(cfg);
            RefreshOverlayMenuHeaders();
        }
    }

    [RelayCommand]
    public void SetOverlayPreset(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId)) return;
        EnsureOverlayCreated();
        _overlayWindow?.SetPreset(presetId);
        var cfg = _overlayConfigService.Load();
        cfg.LayoutPreset = presetId;
        _overlayConfigService.Save(cfg);
        RefreshOverlayMenuHeaders();
    }

    [RelayCommand]
    public void SetOverlayOpacity(string? opacityStr)
    {
        if (string.IsNullOrWhiteSpace(opacityStr)) return;
        if (double.TryParse(opacityStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double op))
        {
            EnsureOverlayCreated();
            _overlayWindow?.SetBaseOpacity(op);
            var cfg = _overlayConfigService.Load();
            cfg.BaseOpacity = op;
            cfg.Opacity = op;
            _overlayConfigService.Save(cfg);
            RefreshOverlayMenuHeaders();
        }
    }

    public void RefreshOverlayMenuHeaders()
    {
        OnPropertyChanged(nameof(CurrentOverlayPreset));
        OnPropertyChanged(nameof(CurrentOverlayOpacity));
        OnPropertyChanged(nameof(MenuPresetClassicBarHeader));
        OnPropertyChanged(nameof(MenuPresetMinimalHeader));
        OnPropertyChanged(nameof(MenuPresetVerticalStackHeader));
        OnPropertyChanged(nameof(MenuPresetNetGraphHeader));
        OnPropertyChanged(nameof(MenuPresetCyberHudHeader));
        OnPropertyChanged(nameof(MenuOpacity100Header));
        OnPropertyChanged(nameof(MenuOpacity85Header));
        OnPropertyChanged(nameof(MenuOpacity75Header));
        OnPropertyChanged(nameof(MenuOpacity50Header));
        OnPropertyChanged(nameof(MenuOpacity25Header));
        OnPropertyChanged(nameof(CurrentOverlayScale));
        OnPropertyChanged(nameof(MenuScaleHeader));
        OnPropertyChanged(nameof(MenuScale75Header));
        OnPropertyChanged(nameof(MenuScale90Header));
        OnPropertyChanged(nameof(MenuScale100Header));
        OnPropertyChanged(nameof(MenuScale115Header));
        OnPropertyChanged(nameof(MenuScale130Header));
        OnPropertyChanged(nameof(MenuScale150Header));
        OnPropertyChanged(nameof(MenuScale180Header));
    }

    public string CurrentOverlayPreset => _overlayConfigService.Load().LayoutPreset ?? "classic_bar";
    public double CurrentOverlayOpacity => _overlayConfigService.Load().BaseOpacity > 0 ? _overlayConfigService.Load().BaseOpacity : 0.85;
    public double CurrentOverlayScale => _overlayConfigService.Load().Scale > 0 ? _overlayConfigService.Load().Scale : 1.0;

    public string MenuLayoutHeader => LocalizationService.Get("OverlayLayout");
    public string MenuOpacityHeader => LocalizationService.Get("OverlayOpacity");
    public string MenuScaleHeader => LocalizationService.Get("OverlayScaleMenu");

    public string MenuPresetClassicBarHeader => (CurrentOverlayPreset.Equals("classic_bar", StringComparison.OrdinalIgnoreCase) ? "✓ " : "    ") + LocalizationService.Get("PresetClassicBar");
    public string MenuPresetMinimalHeader => (CurrentOverlayPreset.Equals("minimal", StringComparison.OrdinalIgnoreCase) ? "✓ " : "    ") + LocalizationService.Get("PresetMinimal");
    public string MenuPresetVerticalStackHeader => (CurrentOverlayPreset.Equals("vertical_stack", StringComparison.OrdinalIgnoreCase) ? "✓ " : "    ") + LocalizationService.Get("PresetVerticalStack");
    public string MenuPresetNetGraphHeader => (CurrentOverlayPreset.Equals("net_graph", StringComparison.OrdinalIgnoreCase) ? "✓ " : "    ") + LocalizationService.Get("PresetNetGraph");
    public string MenuPresetCyberHudHeader => (CurrentOverlayPreset.Equals("cyber_hud", StringComparison.OrdinalIgnoreCase) ? "✓ " : "    ") + LocalizationService.Get("PresetCyberHud");

    public string MenuOpacity100Header => (Math.Abs(CurrentOverlayOpacity - 1.00) < 0.03 ? "✓ " : "    ") + LocalizationService.Get("Opacity100");
    public string MenuOpacity85Header => (Math.Abs(CurrentOverlayOpacity - 0.85) < 0.03 ? "✓ " : "    ") + LocalizationService.Get("Opacity85");
    public string MenuOpacity75Header => (Math.Abs(CurrentOverlayOpacity - 0.75) < 0.03 ? "✓ " : "    ") + LocalizationService.Get("Opacity75");
    public string MenuOpacity50Header => (Math.Abs(CurrentOverlayOpacity - 0.50) < 0.03 ? "✓ " : "    ") + LocalizationService.Get("Opacity50");
    public string MenuOpacity25Header => (Math.Abs(CurrentOverlayOpacity - 0.25) < 0.03 ? "✓ " : "    ") + LocalizationService.Get("Opacity25");

    public string MenuScale75Header => (Math.Abs(CurrentOverlayScale - 0.75) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale75");
    public string MenuScale90Header => (Math.Abs(CurrentOverlayScale - 0.90) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale90");
    public string MenuScale100Header => (Math.Abs(CurrentOverlayScale - 1.00) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale100");
    public string MenuScale115Header => (Math.Abs(CurrentOverlayScale - 1.15) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale115");
    public string MenuScale130Header => (Math.Abs(CurrentOverlayScale - 1.30) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale130");
    public string MenuScale150Header => (Math.Abs(CurrentOverlayScale - 1.50) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale150");
    public string MenuScale180Header => (Math.Abs(CurrentOverlayScale - 1.80) < 0.05 ? "✓ " : "    ") + LocalizationService.Get("Scale180");

    [RelayCommand]
    public void OpenMtrDiagnostics()
    {
        string host = SelectedPingTarget?.Ip ?? "8.8.8.8";
        string name = SelectedPingTarget?.DisplayName ?? "Google DNS (8.8.8.8)";
        RequestOpenMtr?.Invoke(host, name);
    }

    [RelayCommand]
    public async Task OpenIspTicketExportAsync()
    {
        string host = SelectedPingTarget?.Ip ?? "8.8.8.8";
        string name = SelectedPingTarget?.DisplayName ?? "Google DNS (8.8.8.8)";
        List<PingRecord> records;
        lock (SessionRecords)
        {
            records = SessionRecords.ToList();
        }

        var data = await IspTicketService.GatherDiagnosticDataAsync(
            _database,
            records,
            name,
            host,
            null
        );

        var win = new IspTicketExportWindow(data)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    [RelayCommand]
    public void OpenNetworkTweaks()
    {
        var win = new NetworkTweaksWindow(_gameBoostService, _networkTweaksService)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    [RelayCommand]
    public async Task AddCustomServerAsync()
    {
        var dlg = new AddCustomTargetDialog
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultHost))
        {
            var savedTarget = await _database.AddCustomTargetAsync(dlg.ResultName, dlg.ResultHost, _gameService.CurrentGame.Id);
            var vm = new PingTargetItemViewModel
            {
                Name = savedTarget.Name,
                Ip = savedTarget.Host,
                IsCustom = true,
                CustomId = savedTarget.Id
            };
            PingTargets.Add(vm);
            SelectedPingTarget = vm;
            OnPropertyChanged(nameof(IsCustomTargetSelected));
            _ = CheckPingAsync();
        }
    }

    [RelayCommand]
    public async Task DeleteCustomServerAsync(PingTargetItemViewModel? target)
    {
        var targetToDelete = target ?? SelectedPingTarget;
        if (targetToDelete == null || !targetToDelete.IsCustom) return;

        // If CustomId is missing or 0, attempt to look it up in the database
        if (targetToDelete.CustomId <= 0)
        {
            try
            {
                var customs = await _database.GetCustomTargetsAsync();
                var found = customs.FirstOrDefault(c => c.Name.Equals(targetToDelete.Name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    targetToDelete.CustomId = found.Id;
                }
            }
            catch { }
        }

        string prompt = LocalizationService.IsRussian
            ? $"Удалить сервер «{targetToDelete.DisplayName}» ({targetToDelete.Ip})?"
            : $"Delete custom server \"{targetToDelete.DisplayName}\" ({targetToDelete.Ip})?";
        string title = LocalizationService.IsRussian ? "Удаление сервера" : "Delete Server";

        var owner = Application.Current?.MainWindow;
        bool confirm = (owner != null
            ? MessageBox.Show(owner, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;

        if (!confirm) return;

        if (targetToDelete.CustomId > 0)
        {
            await _database.DeleteCustomTargetAsync(targetToDelete.CustomId);
        }
        else
        {
            await _database.DeleteCustomTargetByNameAndGameAsync(targetToDelete.Name, _gameService.CurrentGame.Id);
        }

        PingTargets.Remove(targetToDelete);

        if (SelectedPingTarget == targetToDelete || SelectedPingTarget == null)
        {
            SelectedPingTarget = PingTargets.FirstOrDefault(t => !t.IsCustom && t.Name.Equals(_gameService.CurrentGame.DefaultServerName, StringComparison.OrdinalIgnoreCase))
                ?? PingTargets.FirstOrDefault(t => !t.IsCustom)
                ?? PingTargets.FirstOrDefault();
            _ = CheckPingAsync();
        }

        OnPropertyChanged(nameof(IsCustomTargetSelected));
    }

    public async Task LoadCustomTargetsForActiveGameAsync()
    {
        try
        {
            var customs = await _database.GetCustomTargetsAsync(_gameService.CurrentGame.Id);
            Dispatch(() =>
            {
                var existingCustoms = PingTargets.Where(t => t.IsCustom).ToList();
                foreach (var ec in existingCustoms)
                {
                    PingTargets.Remove(ec);
                }

                foreach (var c in customs)
                {
                    var vm = new PingTargetItemViewModel
                    {
                        Name = c.Name,
                        Ip = c.Host,
                        IsCustom = true,
                        CustomId = c.Id
                    };
                    PingTargets.Add(vm);
                }

                OnPropertyChanged(nameof(IsCustomTargetSelected));
            });
        }
        catch { }
    }

    [RelayCommand]
    public void SetChartInterval(int minutes)
    {
        ChartIntervalMinutes = minutes;
        OnPropertyChanged(nameof(IsInterval1m));
        OnPropertyChanged(nameof(IsInterval5m));
        OnPropertyChanged(nameof(IsInterval10m));
        OnPropertyChanged(nameof(IsInterval60m));

        ChartSubtitleText = GetLocalizedChartSubtitle(minutes);
        AveragePeriodText = GetLocalizedAveragePeriod(minutes);

        _ = LoadDataAsync();
    }

    private static string GetLocalizedChartSubtitle(int minutes) => minutes switch
    {
        5 => LocalizationService.IsRussian ? "(последние 5 минут)" : "(last 5 minutes)",
        10 => LocalizationService.IsRussian ? "(последние 10 минут)" : "(last 10 minutes)",
        60 => LocalizationService.IsRussian ? "(последний час)" : "(last hour)",
        _ => LocalizationService.IsRussian ? "(последняя минута)" : "(last minute)"
    };

    private static string GetLocalizedAveragePeriod(int minutes) => minutes switch
    {
        5 => LocalizationService.IsRussian ? " (5 мин)" : " (5 min)",
        10 => LocalizationService.IsRussian ? " (10 мин)" : " (10 min)",
        60 => LocalizationService.IsRussian ? " (60 мин)" : " (60 min)",
        _ => LocalizationService.IsRussian ? " (60 сек)" : " (60 sec)"
    };

    // =========================================================
    // OVERLAY MANAGEMENT & STATE SYNC
    // =========================================================

    public void EnsureOverlayCreated()
    {
        if (_overlayWindow == null)
        {
            _overlayWindow = new HudOverlayWindow(_overlayConfigService);
            var currentCfg = _overlayConfigService.Load();
            currentCfg.HotkeyOverlay = _keyboardHook.OverlayBinding;
            currentCfg.HotkeyLock = _keyboardHook.LockBinding;
            currentCfg.HotkeySparkline = _keyboardHook.SparklineBinding;
            currentCfg.HotkeyFps = _keyboardHook.FpsBinding;
            currentCfg.HotkeyCpu = _keyboardHook.CpuBinding;
            currentCfg.HotkeyGpu = _keyboardHook.GpuBinding;
            currentCfg.HotkeyRam = _keyboardHook.RamBinding;
            _overlayWindow.SyncConfig(currentCfg);

            string initialCode = IsInDotaMatch && !string.IsNullOrWhiteSpace(DotaMatchBadgeText)
                ? DotaMatchBadgeText
                : GetServerRegionCode(SelectedPingTarget);
            _overlayWindow.UpdateServerCode(initialCode);

            _fpsService.GameFocusChanged += (isFocused, gameName) =>
            {
                _overlayWindow?.OnGameFocusChanged(isFocused, gameName);
            };

            _overlayWindow.StateChangedNotification += _ => UpdateOverlayButtonState();
            _overlayWindow.Closed += (_, _) =>
            {
                _overlayWindow = null;
                UpdateOverlayButtonState();
            };
        }
    }

    public void UpdateOverlayButtonState()
    {
        var cfg = _overlayConfigService.Load();
        bool isVisible = _overlayWindow != null && _overlayWindow.IsVisible;
        bool isLocked = isVisible && _overlayWindow!.IsLocked;
        bool isSparklineOn = _overlayWindow != null
            ? _overlayWindow.IsSparklineVisible
            : cfg.ShowSparkline;
        bool isFpsOn = _overlayWindow != null
            ? _overlayWindow.IsFpsVisible
            : cfg.ShowFps;
        bool isCpuOn = _overlayWindow != null
            ? _overlayWindow.IsCpuVisible
            : cfg.ShowCpu;
        bool isGpuOn = _overlayWindow != null
            ? _overlayWindow.IsGpuVisible
            : cfg.ShowGpu;
        bool isRamOn = _overlayWindow != null
            ? _overlayWindow.IsRamVisible
            : cfg.ShowRam;
        bool isGlowOn = _overlayWindow != null
            ? _overlayWindow.IsGlowEnabled
            : cfg.ShowGlowEffect;

        var theme = ThemeService.Current;
        var greenBrush = CreateBrush(theme.AccentGreen);
        var greenSoftBg = CreateBrush(theme.AccentGreenSoft);
        var cyanBrush = CreateBrush(theme.AccentBlue ?? "#00D2FF");
        var cyanSoftBg = CreateBrush(theme.AccentBlueSoft ?? "#1800D2FF");
        var grayBrush = CreateBrush(theme.TextSecondary);
        var grayMuted = CreateBrush(theme.TextMuted);
        var grayBg = CreateBrush(theme.BgCardElevated);
        var grayBorder = CreateBrush(theme.BorderColor);

        // 1. Статус Ctrl+Shift+O (Вкл / Выкл)
        if (isVisible)
        {
            HkOverlayBg = greenSoftBg;
            HkOverlayBorder = greenSoftBg;
            HkOverlayForeground = greenBrush;
            HkOverlayStatusText = LocalizationService.Get("HkOverlayOn");
            HkOverlayStatusForeground = greenBrush;
        }
        else
        {
            HkOverlayBg = grayBg;
            HkOverlayBorder = grayBorder;
            HkOverlayForeground = grayBrush;
            HkOverlayStatusText = LocalizationService.Get("HkOverlayOff");
            HkOverlayStatusForeground = grayMuted;
        }

        // 2. Статус Ctrl+Shift+L (Фиксация / Сквозные клики)
        if (isLocked)
        {
            HkLockBg = greenSoftBg;
            HkLockBorder = greenSoftBg;
            HkLockForeground = greenBrush;
            HkLockStatusText = LocalizationService.Get("HkLockOn");
            HkLockStatusForeground = greenBrush;
        }
        else
        {
            HkLockBg = grayBg;
            HkLockBorder = grayBorder;
            HkLockForeground = grayBrush;
            HkLockStatusText = LocalizationService.Get("HkLockOff");
            HkLockStatusForeground = grayMuted;
        }

        // 3. Статус Мини-график / Кардиограмма сети
        string sparkHdr = isSparklineOn ? "✓" : " ";
        string sparkName = LocalizationService.Get("SparklineMenu");
        MenuSparklineHeader = HkSparklineText == "..."
            ? $"{sparkHdr} {sparkName}"
            : $"{sparkHdr} {sparkName} ({HkSparklineText})";

        if (isSparklineOn)
        {
            HkSparklineBg = greenSoftBg;
            HkSparklineBorder = greenSoftBg;
            HkSparklineForeground = greenBrush;
            HkSparklineStatusText = LocalizationService.Get("HkGraph");
            HkSparklineStatusForeground = greenBrush;
        }
        else
        {
            HkSparklineBg = grayBg;
            HkSparklineBorder = grayBorder;
            HkSparklineForeground = grayBrush;
            HkSparklineStatusText = LocalizationService.Get("HkOverlayOff");
            HkSparklineStatusForeground = grayMuted;
        }

        // 4. Статус Кадры в секунду FPS
        string fpsHdr = isFpsOn ? "✓" : " ";
        string fpsName = LocalizationService.Get("FpsMenu");
        MenuFpsHeader = HkFpsText == "..."
            ? $"{fpsHdr} {fpsName}"
            : $"{fpsHdr} {fpsName} ({HkFpsText})";

        if (isFpsOn)
        {
            HkFpsBg = greenSoftBg;
            HkFpsBorder = greenSoftBg;
            HkFpsForeground = greenBrush;
            HkFpsStatusText = LocalizationService.Get("HkFps");
            HkFpsStatusForeground = greenBrush;
        }
        else
        {
            HkFpsBg = grayBg;
            HkFpsBorder = grayBorder;
            HkFpsForeground = grayBrush;
            HkFpsStatusText = LocalizationService.Get("HkOverlayOff");
            HkFpsStatusForeground = grayMuted;
        }

        // 5. Статус Нагрузка ЦП CPU
        string cpuHdr = isCpuOn ? "✓" : " ";
        string cpuName = LocalizationService.Get("CpuMenu");
        MenuCpuHeader = HkCpuText == "..."
            ? $"{cpuHdr} {cpuName}"
            : $"{cpuHdr} {cpuName} ({HkCpuText})";

        if (isCpuOn)
        {
            HkCpuBg = greenSoftBg;
            HkCpuBorder = greenSoftBg;
            HkCpuForeground = greenBrush;
            HkCpuStatusText = LocalizationService.Get("HkCpu");
            HkCpuStatusForeground = greenBrush;
        }
        else
        {
            HkCpuBg = grayBg;
            HkCpuBorder = grayBorder;
            HkCpuForeground = grayBrush;
            HkCpuStatusText = LocalizationService.Get("HkOverlayOff");
            HkCpuStatusForeground = grayMuted;
        }

        // 6. Статус Нагрузка ГП GPU
        string gpuHdr = isGpuOn ? "✓" : " ";
        string gpuName = LocalizationService.Get("GpuMenu");
        MenuGpuHeader = HkGpuText == "..."
            ? $"{gpuHdr} {gpuName}"
            : $"{gpuHdr} {gpuName} ({HkGpuText})";

        if (isGpuOn)
        {
            HkGpuBg = greenSoftBg;
            HkGpuBorder = greenSoftBg;
            HkGpuForeground = greenBrush;
            HkGpuStatusText = LocalizationService.Get("HkGpu");
            HkGpuStatusForeground = greenBrush;
        }
        else
        {
            HkGpuBg = grayBg;
            HkGpuBorder = grayBorder;
            HkGpuForeground = grayBrush;
            HkGpuStatusText = LocalizationService.Get("HkOverlayOff");
            HkGpuStatusForeground = grayMuted;
        }

        // 7. Статус Оперативная память RAM
        string ramHdr = isRamOn ? "✓" : " ";
        string ramName = LocalizationService.Get("RamMenu");
        MenuRamHeader = HkRamText == "..."
            ? $"{ramHdr} {ramName}"
            : $"{ramHdr} {ramName} ({HkRamText})";

        if (isRamOn)
        {
            HkRamBg = greenSoftBg;
            HkRamBorder = greenSoftBg;
            HkRamForeground = greenBrush;
            HkRamStatusText = LocalizationService.Get("HkRam");
            HkRamStatusForeground = greenBrush;
        }
        else
        {
            HkRamBg = grayBg;
            HkRamBorder = grayBorder;
            HkRamForeground = grayBrush;
            HkRamStatusText = LocalizationService.Get("HkOverlayOff");
            HkRamStatusForeground = grayMuted;
        }

        // 8. Статус Свечение контура (Ambient Flow)
        if (isGlowOn)
        {
            HkGlowBg = cyanSoftBg;
            HkGlowBorder = cyanSoftBg;
            HkGlowForeground = cyanBrush;
            HkGlowStatusText = LocalizationService.Get("HkGlowAmbient");
            HkGlowStatusForeground = cyanBrush;
            MenuGlowHeader = $"✓ {LocalizationService.Get("GlowMenu")} ({HkGlowText})";
        }
        else
        {
            HkGlowBg = grayBg;
            HkGlowBorder = grayBorder;
            HkGlowForeground = grayBrush;
            HkGlowStatusText = LocalizationService.Get("HkOverlayOff");
            HkGlowStatusForeground = grayMuted;
            MenuGlowHeader = $"{LocalizationService.Get("GlowMenu")} ({HkGlowText})";
        }

        // Управление сервисом FPS (запускаем когда оверлей активен и FPS включен, либо активно авто-скрытие)
        bool needFps = (isVisible && isFpsOn) || cfg.AutoHideWhenNoGame;
        if (needFps)
        {
            if (!_fpsService.IsRunning) _fpsService.Start();
        }
        else
        {
            if (_fpsService.IsRunning) _fpsService.Stop();
        }

        // Управление сервисом CPU (запускаем только когда оверлей активен и CPU включен, либо открыт Диспетчер задач)
        bool needCpu = (isVisible && isCpuOn) || IsTaskManagerView;
        if (needCpu)
        {
            if (!_cpuService.IsRunning) _cpuService.Start();
        }
        else
        {
            if (_cpuService.IsRunning) _cpuService.Stop();
        }

        // Управление сервисом GPU (запускаем когда оверлей активен и GPU включен, либо открыт Диспетчер задач)
        bool needGpu = (isVisible && isGpuOn) || IsTaskManagerView;
        if (needGpu)
        {
            if (!_taskManagerService.Gpu.IsRunning) _taskManagerService.Gpu.Start();
        }
        else
        {
            if (_taskManagerService.Gpu.IsRunning) _taskManagerService.Gpu.Stop();
        }

        // Управление сервисом RAM (запускаем когда оверлей активен и RAM включена)
        if (isVisible && isRamOn)
        {
            if (!_ramService.IsRunning) _ramService.Start();
        }
        else
        {
            if (_ramService.IsRunning) _ramService.Stop();
        }

        // 6. Кнопка оверлея
        if (isVisible)
        {
            OverlayStatusDotBrush = greenBrush;
            if (isLocked)
            {
                OverlayButtonText = LocalizationService.Get("OverlayLocked");
                OverlayButtonToolTip = HkLockText == "..."
                    ? LocalizationService.Get("OverlayLockedTooltip")
                    : $"{LocalizationService.Get("OverlayLockedTooltip")} • {HkLockText}";
            }
            else
            {
                OverlayButtonText = LocalizationService.Get("OverlayActive");
                OverlayButtonToolTip = HkOverlayText == "..."
                    ? LocalizationService.Get("OverlayActiveTooltip")
                    : $"{LocalizationService.Get("OverlayActiveTooltip")} • {HkOverlayText}";
            }
        }
        else
        {
            OverlayStatusDotBrush = grayMuted;
            OverlayButtonText = LocalizationService.Get("Overlay");
            OverlayButtonToolTip = HkOverlayText == "..."
                ? LocalizationService.Get("OverlayTooltip")
                : $"{LocalizationService.Get("OverlayTooltip")} ({HkOverlayText})";
        }

        OnPropertyChanged(nameof(IsOverlayActive));
    }

    // =========================================================
    // THEMES
    // =========================================================

    private void OnThemeChanged(ThemeDefinition theme)
    {
        SelectThemeInList(theme.Id);
        ApplyCurrentThemeBrushes();

        foreach (var target in PingTargets)
        {
            target.UpdatePing(target.PingMs);
        }

        UpdateOverlayButtonState();
        UpdateSpeedtestTheme(theme);
        OnPropertyChanged(nameof(OptimizationStatusDotBrush));
        OnPropertyChanged(nameof(OptimizationButtonBackground));
        OnPropertyChanged(nameof(OptimizationButtonBorderBrush));
        OnPropertyChanged(nameof(OptimizationButtonForeground));

        if (_lastRecordedPingMs > 0)
        {
            UpdatePingStatus(_lastRecordedPingMs);
        }
        else if (_isLastPingTimeout)
        {
            ApplyTimeoutState();
        }

        if (_lastAllRecords != null && _lastValidRecords != null)
        {
            UpdateStatistics(_lastAllRecords, _lastValidRecords);
        }

        if (SessionRecords.Count > 0)
        {
            GraphDataUpdated?.Invoke(SessionRecords.ToList());
        }
        else if (_lastAllRecords != null && _lastAllRecords.Count > 0)
        {
            GraphDataUpdated?.Invoke(_lastAllRecords);
        }

        if (_lastSdrResults != null && _lastSdrResults.Count > 0)
        {
            UpdateDotaSdrList(_lastSdrResults);
        }

        foreach (var item in HistoryItems)
        {
            item.RefreshTheme();
        }
    }

    private void ApplyCurrentThemeBrushes()
    {
        if (_lastRecordedPingMs > 0)
        {
            UpdatePingStatus(_lastRecordedPingMs);
        }
        else
        {
            CurrentPingForeground = CreateBrush(ThemeService.Current.AccentGreen);
            StatusBadgeBackground = CreateBrush(ThemeService.Current.AccentGreenSoft);
            StatusBadgeBorderBrush = CreateBrush(ThemeService.Current.AccentGreen);
        }

        MinPingForeground = CreateBrush(ThemeService.Current.AccentGreen);
        MaxPingForeground = CreateBrush(ThemeService.Current.AccentRed);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void Dispatch(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    // =========================================================
    // НАВИГАЦИЯ: DOTA 2 PING / ДИСПЕТЧЕР ЗАДАЧ
    // =========================================================

    [ObservableProperty]
    private bool _isTaskManagerView;

    [ObservableProperty]
    private bool _isNavMenuOpen;

    public Visibility DotaPingVisibility => (!IsTaskManagerView && !IsSpeedtestView) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TaskManagerVisibility => IsTaskManagerView ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DotaAutoMatchButtonVisibility => (!IsTaskManagerView && !IsSpeedtestView && _gameService.CurrentGame.Id.Equals("dota2", StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;

    public string HeaderTitleText => IsTaskManagerView 
        ? LocalizationService.Get("TaskManagerTitle") 
        : IsSpeedtestView
        ? LocalizationService.Get("NavSpeedtestTitle").ToUpperInvariant()
        : _gameService.CurrentGame.HeaderTitle;
    public string HeaderSubtitleText => IsTaskManagerView 
        ? LocalizationService.Get("TaskManagerSubtitle") 
        : IsSpeedtestView
        ? LocalizationService.Get("NavSpeedtestDesc")
        : (LocalizationService.IsRussian ? _gameService.CurrentGame.Subtitle : _gameService.CurrentGame.SubtitleEn);
    public string SdrHeaderTitle => _gameService.CurrentGame.SdrHeaderTitle;
    public string SdrHeaderBadge => _gameService.CurrentGame.SdrHeaderBadge;
    public string HeaderLogoGlyph => IsTaskManagerView ? "⚡" : IsSpeedtestView ? "⏱" : _gameService.CurrentGame.IconGlyph;
    public Brush HeaderLogoForeground
    {
        get
        {
            if (IsTaskManagerView) return (Brush)new BrushConverter().ConvertFromString("#57C1FF")!;
            if (IsSpeedtestView) return (Brush)new BrushConverter().ConvertFromString("#FFC533")!;
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(_gameService.CurrentGame.LogoColorHex)!;
            }
            catch
            {
                return (Brush)new BrushConverter().ConvertFromString("#59D499")!;
            }
        }
    }
    public string WindowTitle => IsTaskManagerView 
        ? $"{LocalizationService.Get("NavTmTitle")} — Ping Monitoring" 
        : IsSpeedtestView
        ? $"{LocalizationService.Get("NavSpeedtestTitle")} — Ping Monitoring"
        : "Ping Monitoring";
    public string NavGamePingTitle => $"{_gameService.CurrentGame.DisplayName} — Ping Monitoring";

    [RelayCommand]
    public void ToggleNavMenu()
    {
        IsNavMenuOpen = !IsNavMenuOpen;
    }

    [RelayCommand]
    public void SwitchToDotaPing()
    {
        IsTaskManagerView = false;
        IsSpeedtestView = false;
        IsNavMenuOpen = false;
        _taskManagerService.Stop();
        PauseSpeedtestRender();
        UpdateOverlayButtonState();
        OnPropertyChanged(nameof(DotaPingVisibility));
        OnPropertyChanged(nameof(TaskManagerVisibility));
        OnPropertyChanged(nameof(SpeedtestVisibility));
        OnPropertyChanged(nameof(DotaAutoMatchButtonVisibility));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(HeaderLogoGlyph));
        OnPropertyChanged(nameof(HeaderLogoForeground));
        OnPropertyChanged(nameof(SdrHeaderTitle));
        OnPropertyChanged(nameof(SdrHeaderBadge));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(NavGamePingTitle));
    }

    [RelayCommand]
    public void SelectGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;

        // Если находились в диспетчере задач или спидтесте, возвращаемся в режим пинга
        if (IsTaskManagerView || IsSpeedtestView)
        {
            SwitchToDotaPing();
        }

        if (_gameService.CurrentGame.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 1. Сохраняем текущую сессию пинга и историю замеров для уходящей игры
        string oldGameId = _gameService.CurrentGame.Id;
        _gameSessionRecords[oldGameId] = SessionRecords.ToList();
        _gameHistoryItems[oldGameId] = HistoryItems.ToList();

        var cfg = _overlayConfigService.Load();
        if (SelectedPingTarget != null)
        {
            cfg.SetSelectedServerForGame(oldGameId, SelectedPingTarget.Name);
        }

        // 2. Переключаем дисциплину в GameService
        _gameService.SetCurrentGame(gameId);
        var newGame = _gameService.CurrentGame;
        _dotaMatchTrackerService.SetGame(newGame.Id);

        // 3. Обновляем статус выбора в коллекции AvailableGames
        foreach (var g in AvailableGames)
        {
            g.IsSelected = g.Id.Equals(newGame.Id, StringComparison.OrdinalIgnoreCase);
        }
        _isGameChangingInternal = true;
        try
        {
            SelectedGame = AvailableGames.FirstOrDefault(g => g.IsSelected);
        }
        finally
        {
            _isGameChangingInternal = false;
        }

        // 4. Перезаполняем пул серверов (PingTargets) актуальными серверами новой игры
        PingTargets.Clear();
        foreach (var target in newGame.Targets)
        {
            PingTargets.Add(target);
        }
        _ = LoadCustomTargetsForActiveGameAsync();

        // 5. Выбираем сервер дисциплины (сохраненный или дефолтный)
        string preferredServer = cfg.GetSelectedServerForGame(newGame.Id);

        var matchingTarget = !string.IsNullOrWhiteSpace(preferredServer)
            ? PingTargets.FirstOrDefault(t => t.Name.Equals(preferredServer, StringComparison.OrdinalIgnoreCase) || t.Name.Contains(preferredServer, StringComparison.OrdinalIgnoreCase))
            : null;

        _isInitialized = false;
        SelectedPingTarget = matchingTarget ?? PingTargets.FirstOrDefault(t => t.Name.Equals(newGame.DefaultServerName, StringComparison.OrdinalIgnoreCase)) ?? PingTargets.FirstOrDefault();
        _isInitialized = true;

        cfg.SelectedGame = newGame.Id;
        cfg.SetSelectedServerForGame(newGame.Id, SelectedPingTarget?.Name ?? "");
        _overlayConfigService.Save(cfg);

        // 6. Восстанавливаем или сбрасываем историю замеров под новую игру
        SessionRecords.Clear();
        HistoryItems.Clear();

        if (_gameSessionRecords.TryGetValue(newGame.Id, out var savedRecords) && savedRecords.Count > 0)
        {
            SessionRecords.AddRange(savedRecords);
            if (_gameHistoryItems.TryGetValue(newGame.Id, out var savedHistory))
            {
                foreach (var h in savedHistory)
                {
                    HistoryItems.Add(h);
                }
            }
            var valid = SessionRecords.Where(x => !x.IsTimeout && x.PingMs > 0).ToList();
            UpdateStatistics(SessionRecords, valid);
        }
        else
        {
            ResetStatistics();
            CurrentPingText = "-- ms";
        }

        _ = _database.ClearAllAsync();
        GraphDataUpdated?.Invoke(SessionRecords.ToList());

        // 7. Обновляем заголовки интерфейса
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(HeaderLogoGlyph));
        OnPropertyChanged(nameof(HeaderLogoForeground));
        OnPropertyChanged(nameof(SdrHeaderTitle));
        OnPropertyChanged(nameof(SdrHeaderBadge));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(NavGamePingTitle));
        OnPropertyChanged(nameof(DotaAutoMatchButtonVisibility));

        // 8. Переключаем SDR-сервис на соответствующий AppID и кластеры
        _ = _dotaSdr.SwitchGameAsync(newGame.SteamAppId, newGame.DefaultPops, newGame.DefaultRouteName);

        // 9. Немедленно запускаем пинг для выбранного сервера и пула серверов
        _ = CheckPingAsync();
        _ = RefreshAllTargetsPingAsync();
    }

    [RelayCommand]
    public void SwitchToTaskManager()
    {
        IsTaskManagerView = true;
        IsSpeedtestView = false;
        IsNavMenuOpen = false;
        PauseSpeedtestRender();
        if (!_cpuService.IsRunning) _cpuService.Start();
        _taskManagerService.Start();
        OnPropertyChanged(nameof(DotaPingVisibility));
        OnPropertyChanged(nameof(TaskManagerVisibility));
        OnPropertyChanged(nameof(SpeedtestVisibility));
        OnPropertyChanged(nameof(DotaAutoMatchButtonVisibility));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(HeaderLogoGlyph));
        OnPropertyChanged(nameof(HeaderLogoForeground));
        OnPropertyChanged(nameof(WindowTitle));

        // Мгновенная отрисовка кэшированного снимка без задержки кадра (0 мс latency)
        if (_latestSnapshot != null)
        {
            ApplyProcessFilterAndSort();
        }
    }

    // =========================================================
    // СВОЙСТВА И КОМАНДЫ ДИСПЕТЧЕРА ЗАДАЧ
    // =========================================================

    [ObservableProperty]
    private string _totalCpuText = "0%";

    [ObservableProperty]
    private double _totalCpuPercent;

    [ObservableProperty]
    private string _totalGpuText = "0%";

    [ObservableProperty]
    private double _totalGpuPercent;

    [ObservableProperty]
    private string _totalRamText = "0 ГБ / 0 ГБ (0%)";

    [ObservableProperty]
    private double _totalRamPercent;

    [ObservableProperty]
    private string _totalDiskText = "0 МБ/с";

    [ObservableProperty]
    private string _totalNetworkText = "0 МБ/с";

    [ObservableProperty]
    private double _totalNetworkPercent;

    [ObservableProperty]
    private string _totalNetworkSubText = "Прием и передача";

    [ObservableProperty]
    private string _processCountText = "0 процессов";

    [ObservableProperty]
    private string _threadCountText = "0 потоков";

    [ObservableProperty]
    private string _searchProcessFilter = string.Empty;

    [ObservableProperty]
    private ProcessItem? _selectedProcess;

    [ObservableProperty]
    private string _sortColumn = "Cpu";

    [ObservableProperty]
    private bool _sortAscending;

    [ObservableProperty]
    private string _taskManagerStatusMessage = string.Empty;

    public ObservableCollection<ProcessItem> Processes { get; } = new();

    private TaskManagerSnapshot? _latestSnapshot;

    private void OnTaskManagerSnapshotUpdated(TaskManagerSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        Dispatch(() =>
        {
            int currentCpu = _cpuService.IsRunning ? _cpuService.CurrentCpu : (int)Math.Round(snapshot.TotalCpuPercent);
            TotalCpuText = $"{currentCpu}%";
            TotalCpuPercent = currentCpu;
            _overlayWindow?.UpdateCpu(currentCpu);

            int currentGpu = _taskManagerService.Gpu.IsRunning ? _taskManagerService.CurrentGpu : (int)Math.Round(snapshot.TotalGpuPercent);
            TotalGpuText = $"{currentGpu}%";
            TotalGpuPercent = currentGpu;
            _overlayWindow?.UpdateGpu(currentGpu);

            double usedGb = (double)snapshot.UsedRamBytes / (1024 * 1024 * 1024);
            double totalGb = (double)snapshot.TotalRamBytes / (1024 * 1024 * 1024);
            string ramUnit = LocalizationService.IsRussian ? "ГБ" : "GB";
            TotalRamText = $"{usedGb:F1} {ramUnit} / {totalGb:F1} {ramUnit} ({snapshot.RamPercent:F0}%)";
            TotalRamPercent = snapshot.RamPercent;

            string mbSec = LocalizationService.IsRussian ? "МБ/с" : "MB/s";
            string kbSec = LocalizationService.IsRussian ? "КБ/с" : "KB/s";

            if (snapshot.TotalDiskSpeedBytesPerSec >= 1024 * 1024)
            {
                TotalDiskText = $"{snapshot.TotalDiskSpeedBytesPerSec / (1024 * 1024):F1} {mbSec}";
            }
            else
            {
                TotalDiskText = $"{snapshot.TotalDiskSpeedBytesPerSec / 1024:F0} {kbSec}";
            }

            if (snapshot.TotalNetworkSpeedBytesPerSec >= 1024 * 1024)
            {
                TotalNetworkText = $"{snapshot.TotalNetworkSpeedBytesPerSec / (1024.0 * 1024.0):F1} {mbSec}";
            }
            else if (snapshot.TotalNetworkSpeedBytesPerSec >= 1024)
            {
                TotalNetworkText = $"{snapshot.TotalNetworkSpeedBytesPerSec / 1024.0:F0} {kbSec}";
            }
            else
            {
                TotalNetworkText = $"0 {mbSec}";
            }
            TotalNetworkPercent = snapshot.TotalNetworkPercent;
            TotalNetworkSubText = snapshot.TotalNetworkPercent > 0.05
                ? (LocalizationService.IsRussian
                    ? $"{snapshot.TotalNetworkPercent:F0}% пропускной способности"
                    : $"{snapshot.TotalNetworkPercent:F0}% bandwidth")
                : (LocalizationService.IsRussian ? "Прием и передача" : "Send & receive");

            ProcessCountText = LocalizationService.IsRussian
                ? $"{snapshot.ProcessCount} процессов"
                : $"{snapshot.ProcessCount} processes";
            ThreadCountText = LocalizationService.IsRussian
                ? $"{snapshot.ThreadCount:N0} потоков"
                : $"{snapshot.ThreadCount:N0} threads";

            ApplyProcessFilterAndSort();
        });
    }

    partial void OnSearchProcessFilterChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    [RelayCommand]
    public void SetSortColumn(string column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = column == "Name";
        }
        ApplyProcessFilterAndSort();
    }

    private void ApplyProcessFilterAndSort()
    {
        if (_latestSnapshot == null) return;

        var list = _latestSnapshot.Processes;
        if (!string.IsNullOrWhiteSpace(SearchProcessFilter))
        {
            string query = SearchProcessFilter.Trim().ToLowerInvariant();
            list = list.FindAll(p => p.Name.ToLowerInvariant().Contains(query) ||
                                     p.DisplayName.ToLowerInvariant().Contains(query) ||
                                     p.Pid.ToString().Contains(query));
        }

        list = SortColumn switch
        {
            "Name" => SortAscending ? list.OrderBy(p => p.Name).ToList() : list.OrderByDescending(p => p.Name).ToList(),
            "Pid" => SortAscending ? list.OrderBy(p => p.Pid).ToList() : list.OrderByDescending(p => p.Pid).ToList(),
            "Ram" => SortAscending ? list.OrderBy(p => p.RamBytes).ToList() : list.OrderByDescending(p => p.RamBytes).ToList(),
            "Gpu" => SortAscending ? list.OrderBy(p => p.GpuPercent).ToList() : list.OrderByDescending(p => p.GpuPercent).ToList(),
            "Disk" => SortAscending ? list.OrderBy(p => p.DiskBytesPerSec).ToList() : list.OrderByDescending(p => p.DiskBytesPerSec).ToList(),
            "Network" => SortAscending ? list.OrderBy(p => p.NetworkBytesPerSec).ToList() : list.OrderByDescending(p => p.NetworkBytesPerSec).ToList(),
            _ => SortAscending ? list.OrderBy(p => p.CpuPercent).ToList() : list.OrderByDescending(p => p.CpuPercent).ToList()
        };

        int selectedPid = SelectedProcess?.Pid ?? -1;

        // ДИФФИНГ И IN-PLACE РЕКОНСИЛЯЦИЯ (Diffing / Reconciliation):
        // Запрещаем Processes.Clear(), чтобы исключить мерцание, исчезновение и перемонтирование DOM-дерева в WPF.
        // Сопоставление строк выполняется строго по уникальному ключу PID.
        var currentByPid = new Dictionary<int, ProcessItem>(Processes.Count);
        foreach (var p in Processes)
        {
            currentByPid[p.Pid] = p;
        }

        // 1. Формируем список целевых элементов, обновляя метрики у существующих объектов на месте
        var targetList = new List<ProcessItem>(list.Count);
        var targetPids = new HashSet<int>(list.Count);

        foreach (var snapItem in list)
        {
            targetPids.Add(snapItem.Pid);
            if (currentByPid.TryGetValue(snapItem.Pid, out var existingItem))
            {
                existingItem.UpdateMetricsFrom(snapItem);
                targetList.Add(existingItem);
            }
            else
            {
                if (ThemeService.IsOptimizedMode)
                {
                    snapItem.Icon = null;
                }
                targetList.Add(snapItem);
            }
        }

        // 2. Удаляем только завершенные процессы (которых больше нет в новом снимке или фильтре)
        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            if (!targetPids.Contains(Processes[i].Pid))
            {
                Processes.RemoveAt(i);
            }
        }

        // 3. Синхронизируем порядок с минимальными перемещениями (Move/Insert), не трогая остальные строки
        for (int i = 0; i < targetList.Count; i++)
        {
            var targetItem = targetList[i];
            if (i < Processes.Count)
            {
                if (Processes[i].Pid == targetItem.Pid)
                {
                    continue;
                }

                int foundIndex = -1;
                for (int j = i + 1; j < Processes.Count; j++)
                {
                    if (Processes[j].Pid == targetItem.Pid)
                    {
                        foundIndex = j;
                        break;
                    }
                }

                if (foundIndex != -1)
                {
                    Processes.Move(foundIndex, i);
                }
                else
                {
                    Processes.Insert(i, targetItem);
                }
            }
            else
            {
                Processes.Add(targetItem);
            }
        }

        // 4. Очищаем излишки, если целевой список короче текущего
        while (Processes.Count > targetList.Count)
        {
            Processes.RemoveAt(Processes.Count - 1);
        }

        // 5. Сохраняем активный выбор процесса по PID
        if (selectedPid != -1 && (SelectedProcess == null || SelectedProcess.Pid != selectedPid))
        {
            for (int i = 0; i < Processes.Count; i++)
            {
                if (Processes[i].Pid == selectedPid)
                {
                    SelectedProcess = Processes[i];
                    break;
                }
            }
        }
    }

    [RelayCommand]
    public async Task EndTask()
    {
        if (SelectedProcess == null) return;
        var proc = SelectedProcess;
        int pid = proc.Pid;
        string name = proc.Name;

        // 1. Мгновенно удаляем процесс из списка UI для моментальной визуальной реакции
        Processes.Remove(proc);
        _latestSnapshot?.Processes.RemoveAll(p => p.Pid == pid || (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && name != "svchost" && name != "explorer"));

        // Также сразу убираем из интерфейса другие связанные экземпляры этого приложения (например, вкладки Chrome или потоки Discord)
        if (!name.Equals("svchost", StringComparison.OrdinalIgnoreCase) && !name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            var siblings = Processes.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var sib in siblings)
            {
                Processes.Remove(sib);
            }
        }

        TaskManagerStatusMessage = $"Завершение процесса {name}...";

        // 2. В фоновом потоке принудительно и моментально уничтожаем процесс и его дерево
        bool success = await Task.Run(() => _taskManagerService.KillProcess(pid, name, true));
        if (success)
        {
            TaskManagerStatusMessage = $"Процесс {name} (PID {pid}) успешно завершён";
        }
        else
        {
            TaskManagerStatusMessage = $"Не удалось завершить процесс {name} (PID {pid})";
        }

        // 3. Через 250 мс делаем контрольный сбор снимка, когда Windows полностью освободила дескрипторы
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            var snap = _taskManagerService.CollectSnapshot();
            OnTaskManagerSnapshotUpdated(snap);
        });
    }

    [RelayCommand]
    public void OpenProcessLocation()
    {
        if (SelectedProcess == null) return;
        _taskManagerService.OpenProcessLocation(SelectedProcess.Pid);
    }

    [RelayCommand]
    public void RefreshTaskManager()
    {
        Task.Run(() =>
        {
            var snap = _taskManagerService.CollectSnapshot();
            OnTaskManagerSnapshotUpdated(snap);
        });
    }
}
