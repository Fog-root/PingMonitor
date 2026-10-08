using System.Globalization;

namespace DotaPingMonitor.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, string> RuDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Game"] = "ИГРА",
        ["Server"] = "СЕРВЕР",
        ["Theme"] = "ТЕМА",
        ["Language"] = "ЯЗЫК",
        ["LanguageTooltip"] = "Язык интерфейса / Interface Language (RU | EN)",

        ["OptimizationActive"] = "⚡ Оптимизирован (Flat)",
        ["OptimizationInactive"] = "⚡ Оптимизировать",
        ["OptimizationActiveTooltip"] = "Режим экстремальной производительности активен:\n• Все размытия, тени, блумы и градиенты полностью отключены\n• Чистый легковесный flat-режим с нулевой нагрузкой на GPU\n• Кликните, чтобы восстановить декоративные эффекты оформления",
        ["OptimizationInactiveTooltip"] = "Включить режим максимальной производительности (Flat UI):\n• Мгновенное отключение радиальных градиентов, бликов и размытий\n• Полный сброс всех теней (DropShadow / Bloom)\n• 1px четкая векторная линия графика без градиентных заливок\n• Оптимизация интервалов опроса телеметрии",

        ["MtrDiagnostics"] = "MTR Диагностика",
        ["MtrDiagnosticsTooltip"] = "Трассировка сетевых узлов (MTR) • Определение источника потерь: роутер, провайдер или магистраль",
        ["IspTicketButton"] = "Отчёт для техподдержки",
        ["IspTicketTooltip"] = "Сформировать готовое вежливое обращение и технический отчёт для службы техподдержки интернет-провайдера",
        ["NavIspTicketTitle"] = "Отчёт для техподдержки",
        ["NavIspTicketDesc"] = "Сформировать готовый тикет для провайдера (MTR, логи)",

        ["NavSections"] = "РАЗДЕЛЫ ПРИЛОЖЕНИЯ",
        ["NavPingDesc"] = "Телеметрия сети, SDR, график",
        ["NavTmTitle"] = "Диспетчер задач",
        ["NavTmDesc"] = "Процессы, ЦП, память Windows",
        ["NavSpeedtestTitle"] = "Спидтест сети",
        ["NavSpeedtestDesc"] = "Замер скорости интернета, Download, Upload, Jitter",
        ["NavActive"] = "АКТИВНО",
        ["NavMenuTooltip"] = "Разделы приложения\nЛКМ — открыть меню: Ping, Диспетчер задач или Спидтест",
        ["GameComboTooltip"] = "Выбор активной игры для мониторинга пинга",

        ["SpeedtestStart"] = "СТАРТ",
        ["SpeedtestCancel"] = "ОТМЕНА",
        ["SpeedtestTesting"] = "ТЕСТИРОВАНИЕ...",
        ["SpeedtestIdle"] = "ГОТОВ К ЗАМЕРУ",
        ["SpeedtestDownload"] = "ВХОДЯЩАЯ СКОРОСТЬ",
        ["SpeedtestUpload"] = "ИСХОДЯЩАЯ СКОРОСТЬ",
        ["SpeedtestPing"] = "ЗАДЕРЖКА (PING)",
        ["SpeedtestJitter"] = "ДЖИТТЕР (JITTER)",
        ["SpeedtestIsp"] = "ПРОВАЙДЕР",
        ["SpeedtestServer"] = "СЕРВЕР",
        ["SpeedtestClientIp"] = "ВНЕШНИЙ IP",
        ["SpeedtestPeak"] = "Пик",
        ["SpeedtestAverage"] = "Средняя",
        ["SpeedtestWaveGraph"] = "ВОЛНА СКОРОСТИ В РЕАЛЬНОМ ВРЕМЕНИ",
        ["SpeedtestHistoryTitle"] = "ИСТОРИЯ ЗАМЕРОВ",
        ["SpeedtestClearHistory"] = "Очистить историю",
        ["SpeedtestNoHistory"] = "История замеров пуста. Нажмите «СТАРТ» для первого теста.",
        ["SpeedtestMbps"] = "Мбит/с",
        ["SpeedtestColDownload"] = "ВХОДЯЩАЯ",
        ["SpeedtestColPeak"] = "ПИК",
        ["SpeedtestColUpload"] = "ОТДАЧА",
        ["SpeedtestColPing"] = "ПИНГ",
        ["SpeedtestColJitter"] = "ДЖИТТЕР",
        ["SpeedtestColServer"] = "СЕРВЕР / ПРОВАЙДЕР",

        ["Overlay"] = "🎮 Оверлей",
        ["OverlayActive"] = "🎮 HUD ВКЛ",
        ["OverlayLocked"] = "🎮 HUD 🔒",
        ["OverlayTooltip"] = "Игровой HUD-оверлей поверх экрана (Клик: включить)",
        ["OverlayActiveTooltip"] = "HUD оверлей активен • Клик: скрыть",
        ["OverlayLockedTooltip"] = "HUD зафиксирован (сквозные клики) • Клик: скрыть",

        ["HkOverlayOn"] = "вкл",
        ["HkOverlayOff"] = "выкл",
        ["HkLockOn"] = "сквозь",
        ["HkLockOff"] = "клик",
        ["HkGraph"] = "граф",
        ["HkFps"] = "фпс",
        ["HkCpu"] = "цп",
        ["HkGpu"] = "гп",
        ["HkRam"] = "озу",
        ["HkHint"] = "ЛКМ — вкл/выкл оверлей и опции • ПКМ — настроить хоткеи",
        ["HkOverlayTooltip"] = "Включить / скрыть HUD-оверлей\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkLockTooltip"] = "Зафиксировать / разблокировать для сквозных кликов\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkSparklineTooltip"] = "Включить / скрыть мини-график кардиограммы в HUD\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkFpsTooltip"] = "Включить / скрыть отображение FPS в HUD\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkCpuTooltip"] = "Включить / скрыть отображение нагрузки ЦП (CPU) в HUD\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkGpuTooltip"] = "Включить / скрыть отображение нагрузки ГП (GPU) в HUD\nЛКМ — переключить | ПКМ — настроить горячую клавишу",
        ["HkRamTooltip"] = "Включить / скрыть отображение оперативной памяти (ОЗУ) в HUD\nЛКМ — переключить | ПКМ — настроить горячую клавишу",

        ["SparklineMenu"] = "Кардиограмма сети",
        ["FpsMenu"] = "Кадры в секунду (FPS)",
        ["CpuMenu"] = "Нагрузка ЦП (CPU)",
        ["GpuMenu"] = "Нагрузка ГП (GPU)",
        ["RamMenu"] = "Нагрузка ОЗУ (RAM)",
        ["ThemeMenu"] = "Тема оформления",
        ["OverlayScaleMenu"] = "Масштаб HUD",
        ["Scale75"] = "75% (Компактный)",
        ["Scale90"] = "90%",
        ["Scale100"] = "100% (По умолчанию)",
        ["Scale115"] = "115%",
        ["Scale130"] = "130% (Увеличенный)",
        ["Scale150"] = "150% (Большой / 2K)",
        ["Scale180"] = "180% (4K мониторы)",

        ["OverlayLayout"] = "Вид оверлея",
        ["PresetClassicBar"] = "Classic Bar (По умолчанию)",
        ["PresetMinimal"] = "Ultra-Minimalist (Микро-режим)",
        ["PresetVerticalStack"] = "Vertical Widget (Угловой стек)",
        ["PresetNetGraph"] = "Pro Gamer (NetGraph телеметрия)",
        ["PresetCyberHud"] = "Cyber HUD (Футуристичный)",

        ["OverlayOpacity"] = "Прозрачность",
        ["Opacity100"] = "100% (Непрозрачный)",
        ["Opacity85"] = "85% (Оптимальная)",
        ["Opacity75"] = "75%",
        ["Opacity50"] = "50% (Полупрозрачный)",
        ["Opacity25"] = "25% (Призрачный)",

        ["CurrentPing"] = "ТЕКУЩИЙ ОТКЛИК",
        ["AveragePing"] = "СРЕДНИЙ PING",
        ["Minimum"] = "МИНИМАЛЬНЫЙ",
        ["Maximum"] = "МАКСИМАЛЬНЫЙ",
        ["PacketLoss"] = "ПОТЕРИ ПАКЕТОВ",
        ["Spikes"] = "ПРОСАДКИ (>100ms)",
        ["SpikesCountSuffix"] = " скачков",
        ["BestSuffix"] = " лучший",
        ["PeakSuffix"] = " пик",

        ["StatusStable"] = "Соединение стабильное",
        ["StatusModerate"] = "Пинг повышен (>100ms)",
        ["StatusHigh"] = "Высокий пинг (>200ms)!",
        ["StatusTimeout"] = "Нет ответа",
        ["StatusConnError"] = "Ошибка соединения",
        ["CheckingConnection"] = "Проверяем соединение...",
        ["NoPacketsLost"] = "Потерь нет (0%)",

        ["NetworkLatencyGraph"] = "ГРАФИК ОТКЛИКА СЕТИ",
        ["MeasurementHistory"] = "ИСТОРИЯ ЗАМЕРОВ",
        ["ColTime"] = "ВРЕМЯ",
        ["ColPing"] = "ОТКЛИК",
        ["ColLoss"] = "ПОТЕРИ",
        ["ColStatus"] = "СТАТУС",
        ["ColServer"] = "СЕРВЕР",

        ["Live"] = "LIVE",
        ["LiveMode"] = "В ЭФИРЕ",
        ["ReturnToLive"] = "◄ В ЭФИР",
        ["ReturnToLiveTooltip"] = "Нажмите, чтобы вернуться к мониторингу в реальном времени",
        ["ChartBackTooltip"] = "Назад в историю (на 5 сек)",
        ["ChartForwardTooltip"] = "Вперед к эфиру (на 5 сек)",
        ["Router"] = "Роутер:",
        ["Isp"] = "Провайдер:",
        ["AnalyzeMtr"] = "Анализ узлов (MTR)",
        ["IcmpPingLabel"] = "ICMP Ping",

        ["Interval1m"] = "1 мин",
        ["Interval5m"] = "5 мин",
        ["Interval10m"] = "10 мин",
        ["Interval60m"] = "60 мин",

        ["ValveSdrRelays"] = "SDR РЕЛЕИ VALVE",
        ["AwsRegions"] = "РЕГИОНЫ AWS / EDGE СЕРВЕРЫ",
        ["ServerClusters"] = "КЛАСТЕРЫ СЕРВЕРОВ",
        ["RefreshIn"] = "ОБНОВЛЕНИЕ ЧЕРЕЗ",
        ["Best"] = "ЛУЧШИЙ",
        ["Refresh"] = "↻ Обновить",
        ["SecSuffix"] = "сек",
        ["ColRegion"] = "РЕГИОН",
        ["ColCode"] = "КОД",
        ["ColRoute"] = "МАРШРУТ В КЛАСТЕР",
        ["WaitDotaData"] = "Ожидаем данные от Dota 2...",
        ["DotaAutoMatchActive"] = "🎯 Авто-катка: ВКЛ",
        ["DotaAutoMatchInactive"] = "🎯 Авто-катка: ВЫКЛ",
        ["DotaAutoMatchTooltip"] = "Автоматический перехват активного сервера катки Dota 2:\n• Мгновенно распознает вход в катку и подключение к SDR-релею\n• Переключает мониторинг на реальный игровой сервер\n• Отображает точный пинг, потери пакетов и jitter матча\n• Автоматически возвращает исходный сервер после окончания игры",
        ["DotaInMatchBadge"] = "В КАТКЕ",
        ["DotaMatchServerLabel"] = "Сервер матча:",
        ["DotaMatchRelayLabel"] = "Релей SDR:",
        ["DotaMatchLossLabel"] = "Потери в катке:",
        ["DotaMatchDurationLabel"] = "Время в матче:",
        ["DotaLogWarningTitle"] = "Dota 2 запущена без записи консоли в файл",
        ["DotaLogWarningText"] = "Для авто-захвата текущей катки введите в консоли Dota 2 (~):",
        ["DotaLogWarningTip"] = "Совет: добавьте -condebug в параметры запуска игры в Steam, чтобы захват работал всегда автоматически.",
        ["DotaCopyCommand"] = "Скопировать команду",
        ["DotaCopyCondebug"] = "Скопировать -condebug",

        ["HistoryStatusStable"] = "✓ Стабильно",
        ["HistoryStatusSpike"] = "⚠ Скачок (>100ms)",
        ["HistoryStatusHigh"] = "⚠ Высокий (>200ms)",
        ["HistoryStatusTimeout"] = "✕ Таймаут",

        ["ProcessorCpu"] = "ПРОЦЕССОР (ЦП)",
        ["GraphicsGpu"] = "ВИДЕОКАРТА (ГП)",
        ["MemoryRam"] = "ПАМЯТЬ (ОЗУ)",
        ["DiskActivity"] = "АКТИВНОСТЬ ДИСКА",
        ["Network"] = "СЕТЬ",
        ["ActiveProcesses"] = "АКТИВНЫЕ ПРОЦЕССЫ",
        ["SearchPlaceholder"] = "Поиск процесса или PID...",
        ["EndTask"] = "Снять задачу",
        ["Folder"] = "Папка",
        ["FolderTooltip"] = "Открыть папку с файлом выбранного процесса",
        ["EndTaskTooltip"] = "Завершить выбранный процесс (Del)",
        ["RefreshTmTooltip"] = "Обновить список процессов (F5)",
        ["TotalCpuLoad"] = "Суммарная нагрузка ЦП",
        ["TotalGpuLoad"] = "Суммарная нагрузка ГП",
        ["TotalDiskIo"] = "Суммарный ввод / вывод",
        ["System"] = "СИСТЕМА",
        ["ActiveThreads"] = "Активные потоки Windows",
        ["Threads"] = "потоков",
        ["ColName"] = "Имя процесса / Приложение",
        ["ColPid"] = "ИД (PID)",
        ["ColCpuSort"] = "ЦП %",
        ["ColGpuSort"] = "ГП %",
        ["ColRamSort"] = "Память",
        ["ColDiskSort"] = "Диск",
        ["ColNetSort"] = "Сеть",
        ["ColThreadsSort"] = "Потоки",
        ["ColStateSort"] = "Состояние",
        ["TaskManagerTitle"] = "ДИСПЕТЧЕР ЗАДАЧ",
        ["TaskManagerSubtitle"] = "Мониторинг процессов и системных ресурсов в реальном времени"
    };

    private static readonly Dictionary<string, string> EnDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Game"] = "GAME",
        ["Server"] = "SERVER",
        ["Theme"] = "THEME",
        ["Language"] = "LANG",
        ["LanguageTooltip"] = "Interface Language / Язык интерфейса (RU | EN)",

        ["OptimizationActive"] = "⚡ Optimized (Flat)",
        ["OptimizationInactive"] = "⚡ Optimize",
        ["OptimizationActiveTooltip"] = "Extreme performance mode active:\n• All blurs, shadows, blooms, and gradients are disabled\n• Clean lightweight flat mode with 0% GPU overhead\n• Click to restore decorative theme effects",
        ["OptimizationInactiveTooltip"] = "Enable maximum performance mode (Flat UI):\n• Instantly disables radial gradients, glows, and blurs\n• Drops all drop shadows and blooms\n• 1px crisp vector line without gradient fill\n• Optimized telemetry polling intervals",

        ["MtrDiagnostics"] = "MTR Diagnostics",
        ["MtrDiagnosticsTooltip"] = "Network route tracing (MTR) • Identify loss sources: router, ISP, or backbone",
        ["IspTicketButton"] = "ISP Support Ticket",
        ["IspTicketTooltip"] = "Generate a complete technical support ticket and route diagnostics for your Internet Service Provider",
        ["NavIspTicketTitle"] = "ISP Support Ticket",
        ["NavIspTicketDesc"] = "Generate support ticket for provider (MTR, logs)",

        ["NavSections"] = "APP SECTIONS",
        ["NavPingDesc"] = "Network telemetry, SDR, graph",
        ["NavTmTitle"] = "Task Manager",
        ["NavTmDesc"] = "Processes, CPU, Windows memory",
        ["NavSpeedtestTitle"] = "Network Speedtest",
        ["NavSpeedtestDesc"] = "Internet speed test, Download, Upload, Jitter",
        ["NavActive"] = "ACTIVE",
        ["NavMenuTooltip"] = "App sections\nLMB — open menu: Game Ping, Task Manager or Speedtest",
        ["GameComboTooltip"] = "Select active game for ping monitoring",

        ["SpeedtestStart"] = "START",
        ["SpeedtestCancel"] = "CANCEL",
        ["SpeedtestTesting"] = "TESTING...",
        ["SpeedtestIdle"] = "READY TO TEST",
        ["SpeedtestDownload"] = "DOWNLOAD SPEED",
        ["SpeedtestUpload"] = "UPLOAD SPEED",
        ["SpeedtestPing"] = "LATENCY (PING)",
        ["SpeedtestJitter"] = "JITTER",
        ["SpeedtestIsp"] = "PROVIDER",
        ["SpeedtestServer"] = "SERVER",
        ["SpeedtestClientIp"] = "PUBLIC IP",
        ["SpeedtestPeak"] = "Peak",
        ["SpeedtestAverage"] = "Average",
        ["SpeedtestWaveGraph"] = "REAL-TIME SPEED WAVEFORM",
        ["SpeedtestHistoryTitle"] = "SPEEDTEST HISTORY",
        ["SpeedtestClearHistory"] = "Clear History",
        ["SpeedtestNoHistory"] = "No speedtest history yet. Click \"START\" to run a test.",
        ["SpeedtestMbps"] = "Mbps",
        ["SpeedtestColDownload"] = "DOWNLOAD",
        ["SpeedtestColPeak"] = "PEAK",
        ["SpeedtestColUpload"] = "UPLOAD",
        ["SpeedtestColPing"] = "PING",
        ["SpeedtestColJitter"] = "JITTER",
        ["SpeedtestColServer"] = "SERVER / ISP",

        ["Overlay"] = "🎮 Overlay",
        ["OverlayActive"] = "🎮 HUD ON",
        ["OverlayLocked"] = "🎮 HUD 🔒",
        ["OverlayTooltip"] = "Game HUD overlay on top of screen (Click: enable)",
        ["OverlayActiveTooltip"] = "HUD overlay active • Click: hide",
        ["OverlayLockedTooltip"] = "HUD locked (click-through) • Click: hide",

        ["HkOverlayOn"] = "on",
        ["HkOverlayOff"] = "off",
        ["HkLockOn"] = "click-thru",
        ["HkLockOff"] = "click",
        ["HkGraph"] = "graph",
        ["HkFps"] = "fps",
        ["HkCpu"] = "cpu",
        ["HkGpu"] = "gpu",
        ["HkRam"] = "ram",
        ["HkHint"] = "LMB — toggle overlay & options • RMB — configure hotkeys",
        ["HkOverlayTooltip"] = "Toggle HUD overlay\nLMB — toggle | RMB — configure hotkey",
        ["HkLockTooltip"] = "Lock / unlock for click-through in game\nLMB — toggle | RMB — configure hotkey",
        ["HkSparklineTooltip"] = "Toggle mini ping sparkline in HUD\nLMB — toggle | RMB — configure hotkey",
        ["HkFpsTooltip"] = "Toggle FPS counter in HUD\nLMB — toggle | RMB — configure hotkey",
        ["HkCpuTooltip"] = "Toggle CPU load in HUD\nLMB — toggle | RMB — configure hotkey",
        ["HkGpuTooltip"] = "Toggle GPU load in HUD\nLMB — toggle | RMB — configure hotkey",
        ["HkRamTooltip"] = "Toggle RAM load in HUD\nLMB — toggle | RMB — configure hotkey",

        ["SparklineMenu"] = "Network Sparkline",
        ["FpsMenu"] = "Frames per second (FPS)",
        ["CpuMenu"] = "CPU load",
        ["GpuMenu"] = "GPU load",
        ["RamMenu"] = "RAM load",
        ["ThemeMenu"] = "Color Theme",
        ["OverlayScaleMenu"] = "HUD Scale",
        ["Scale75"] = "75% (Compact)",
        ["Scale90"] = "90%",
        ["Scale100"] = "100% (Default)",
        ["Scale115"] = "115%",
        ["Scale130"] = "130% (Enlarged)",
        ["Scale150"] = "150% (Large / 2K)",
        ["Scale180"] = "180% (4K Displays)",

        ["OverlayLayout"] = "Overlay Layout",
        ["PresetClassicBar"] = "Classic Bar (Default)",
        ["PresetMinimal"] = "Ultra-Minimalist (Compact)",
        ["PresetVerticalStack"] = "Vertical Widget (Corner Stack)",
        ["PresetNetGraph"] = "Pro Gamer (NetGraph Telemetry)",
        ["PresetCyberHud"] = "Cyber HUD (Futuristic)",

        ["OverlayOpacity"] = "Opacity",
        ["Opacity100"] = "100% (Solid)",
        ["Opacity85"] = "85% (Optimal)",
        ["Opacity75"] = "75%",
        ["Opacity50"] = "50% (Semi-transparent)",
        ["Opacity25"] = "25% (Ghost)",

        ["CurrentPing"] = "CURRENT PING",
        ["AveragePing"] = "AVERAGE PING",
        ["Minimum"] = "MINIMUM",
        ["Maximum"] = "MAXIMUM",
        ["PacketLoss"] = "PACKET LOSS",
        ["Spikes"] = "SPIKES (>100ms)",
        ["SpikesCountSuffix"] = " spikes",
        ["BestSuffix"] = " best",
        ["PeakSuffix"] = " peak",

        ["StatusStable"] = "Connection stable",
        ["StatusModerate"] = "Elevated ping (>100ms)",
        ["StatusHigh"] = "High ping (>200ms)!",
        ["StatusTimeout"] = "No response",
        ["StatusConnError"] = "Connection error",
        ["CheckingConnection"] = "Checking connection...",
        ["NoPacketsLost"] = "No loss (0%)",

        ["NetworkLatencyGraph"] = "NETWORK LATENCY GRAPH",
        ["MeasurementHistory"] = "MEASUREMENT HISTORY",
        ["ColTime"] = "TIME",
        ["ColPing"] = "PING",
        ["ColLoss"] = "LOSS",
        ["ColStatus"] = "STATUS",
        ["ColServer"] = "SERVER",

        ["Live"] = "LIVE",
        ["LiveMode"] = "LIVE",
        ["ReturnToLive"] = "◄ TO LIVE",
        ["ReturnToLiveTooltip"] = "Click to return to real-time monitoring",
        ["ChartBackTooltip"] = "Back in history (by 5s)",
        ["ChartForwardTooltip"] = "Forward to live (by 5s)",
        ["Router"] = "Router:",
        ["Isp"] = "ISP:",
        ["AnalyzeMtr"] = "Trace nodes (MTR)",
        ["IcmpPingLabel"] = "ICMP Ping",

        ["Interval1m"] = "1 min",
        ["Interval5m"] = "5 min",
        ["Interval10m"] = "10 min",
        ["Interval60m"] = "60 min",

        ["ValveSdrRelays"] = "VALVE SDR RELAYS",
        ["AwsRegions"] = "AWS REGIONS / EDGE SERVERS",
        ["ServerClusters"] = "SERVER CLUSTERS",
        ["RefreshIn"] = "REFRESH IN",
        ["Best"] = "BEST",
        ["Refresh"] = "↻ Refresh",
        ["SecSuffix"] = "s",
        ["ColRegion"] = "REGION",
        ["ColCode"] = "CODE",
        ["ColRoute"] = "CLUSTER ROUTE",
        ["WaitDotaData"] = "Waiting for Dota 2 data...",
        ["DotaAutoMatchActive"] = "🎯 Auto-Match: ON",
        ["DotaAutoMatchInactive"] = "🎯 Auto-Match: OFF",
        ["DotaAutoMatchTooltip"] = "Automatic Dota 2 active match server detection:\n• Instantly detects when you connect to a match relay\n• Switches ping telemetry to the live match server\n• Displays exact real-time ping, packet loss %, and jitter\n• Restores original server automatically when match ends",
        ["DotaInMatchBadge"] = "IN MATCH",
        ["DotaMatchServerLabel"] = "Match Server:",
        ["DotaMatchRelayLabel"] = "SDR Relay:",
        ["DotaMatchLossLabel"] = "Match Packet Loss:",
        ["DotaMatchDurationLabel"] = "Match Duration:",
        ["DotaLogWarningTitle"] = "Dota 2 running without console file logging",
        ["DotaLogWarningText"] = "To auto-track your ongoing match, enter in Dota 2 console (~):",
        ["DotaLogWarningTip"] = "Tip: Add -condebug to Dota 2 Steam Launch Options for permanent auto-tracking.",
        ["DotaCopyCommand"] = "Copy command",
        ["DotaCopyCondebug"] = "Copy -condebug",

        ["HistoryStatusStable"] = "✓ Stable",
        ["HistoryStatusSpike"] = "⚠ Spike (>100ms)",
        ["HistoryStatusHigh"] = "⚠ High (>200ms)",
        ["HistoryStatusTimeout"] = "✕ Timeout",

        ["ProcessorCpu"] = "PROCESSOR (CPU)",
        ["GraphicsGpu"] = "GRAPHICS (GPU)",
        ["MemoryRam"] = "MEMORY (RAM)",
        ["DiskActivity"] = "DISK ACTIVITY",
        ["Network"] = "NETWORK",
        ["ActiveProcesses"] = "ACTIVE PROCESSES",
        ["SearchPlaceholder"] = "Search process or PID...",
        ["EndTask"] = "End task",
        ["Folder"] = "Folder",
        ["FolderTooltip"] = "Open file location of selected process",
        ["EndTaskTooltip"] = "Terminate selected process (Del)",
        ["RefreshTmTooltip"] = "Refresh process list (F5)",
        ["TotalCpuLoad"] = "Total CPU load",
        ["TotalGpuLoad"] = "Total GPU load",
        ["TotalDiskIo"] = "Total disk I/O",
        ["System"] = "SYSTEM",
        ["ActiveThreads"] = "Active Windows threads",
        ["Threads"] = "threads",
        ["ColName"] = "Process name / Application",
        ["ColPid"] = "PID",
        ["ColCpuSort"] = "CPU %",
        ["ColGpuSort"] = "GPU %",
        ["ColRamSort"] = "RAM",
        ["ColDiskSort"] = "Disk",
        ["ColNetSort"] = "Network",
        ["ColThreadsSort"] = "Threads",
        ["ColStateSort"] = "Status",
        ["TaskManagerTitle"] = "TASK MANAGER",
        ["TaskManagerSubtitle"] = "Real-time process and system resource monitoring"
    };

    public static string CurrentLanguage { get; private set; } = "ru";

    public static bool IsRussian => CurrentLanguage.Equals("ru", StringComparison.OrdinalIgnoreCase);

    public static event Action<string>? LanguageChanged;

    public static void SetLanguage(string lang)
    {
        string normalized = NormalizeLanguage(lang);
        if (CurrentLanguage.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            return;

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke(CurrentLanguage);
    }

    public static string Get(string key)
    {
        var dict = IsRussian ? RuDictionary : EnDictionary;
        if (dict.TryGetValue(key, out var val))
            return val;

        // Fallback to Russian
        if (RuDictionary.TryGetValue(key, out var ruVal))
            return ruVal;

        return key;
    }

    public static string DetectDefaultLanguage()
    {
        try
        {
            var uiCulture = CultureInfo.CurrentUICulture;
            if (uiCulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase))
                return "ru";
        }
        catch { }

        return "en";
    }

    public static string NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return DetectDefaultLanguage();

        return lang.ToLowerInvariant().StartsWith("ru") ? "ru" : "en";
    }

    private static readonly CultureInfo RussianCulture = new("ru-RU");

    /// <summary>
    /// Форматирует скорость в зависимости от текущего языка интерфейса:
    /// Для EN: 32.7 Mbps (точка, единица 'Mbps')
    /// Для RU: 32,7 Мбит/с (запятая, единица 'Мбит/с')
    /// </summary>
    public static string FormatSpeed(double value, string? lang = null)
    {
        bool isRu = lang != null 
            ? lang.Equals("ru", StringComparison.OrdinalIgnoreCase) 
            : IsRussian;

        if (isRu)
        {
            string num = value.ToString("0.0", RussianCulture);
            return $"{num} Мбит/с";
        }
        else
        {
            string num = value.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{num} Mbps";
        }
    }

    /// <summary>
    /// Форматирует джиттер с учетом локали:
    /// Для EN: ±20.7 ms
    /// Для RU: ±20,7 ms
    /// </summary>
    public static string FormatJitter(double value, string? lang = null)
    {
        bool isRu = lang != null 
            ? lang.Equals("ru", StringComparison.OrdinalIgnoreCase) 
            : IsRussian;

        string num = isRu 
            ? value.ToString("0.0", RussianCulture) 
            : value.ToString("0.0", CultureInfo.InvariantCulture);

        return $"±{num} ms";
    }

    /// <summary>
    /// Форматирует число с разделителем точки (EN) или запятой (RU).
    /// </summary>
    public static string FormatNumber(double value, string format = "0.0", string? lang = null)
    {
        bool isRu = lang != null 
            ? lang.Equals("ru", StringComparison.OrdinalIgnoreCase) 
            : IsRussian;

        return isRu 
            ? value.ToString(format, RussianCulture) 
            : value.ToString(format, CultureInfo.InvariantCulture);
    }
}
