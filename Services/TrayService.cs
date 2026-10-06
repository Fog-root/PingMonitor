using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис системного трея Windows (Notification Area / Область уведомлений панели задач).
/// Обеспечивает отображение иконки Ping Monitoring, контекстное меню Raycast Dark и сворачивание в трей.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Window _mainWindow;
    private readonly Action _toggleOverlayAction;
    private readonly Action _explicitExitAction;

    private TaskbarIcon? _taskbarIcon;
    private ContextMenu? _contextMenu;
    private MenuItem? _startupMenuItem;
    private MenuItem? _closeToTrayMenuItem;

    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool MinimizeToTrayOnMinimize { get; set; } = false;

    public TrayService(Window mainWindow, Action toggleOverlayAction, Action explicitExitAction)
    {
        _mainWindow = mainWindow;
        _toggleOverlayAction = toggleOverlayAction;
        _explicitExitAction = explicitExitAction;
    }

    /// <summary>
    /// Инициализирует иконку в трее и контекстное меню.
    /// </summary>
    public void Initialize()
    {
        BuildContextMenu();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Ping Monitoring",
            ContextMenu = _contextMenu
        };

        // Назначаем системную или локальную иконку
        AssignTrayIcon();

        // Открытие главного окна по левому клику или двойному клику
        _taskbarIcon.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private void AssignTrayIcon()
    {
        if (_taskbarIcon == null) return;

        try
        {
            // 1. Попытка извлечь иконку из запущенного .exe
            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (exeIcon != null)
                {
                    _taskbarIcon.Icon = exeIcon;
                    return;
                }
            }

            // 2. Попытка загрузить app.ico рядом с программой
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(localIco))
            {
                _taskbarIcon.Icon = new System.Drawing.Icon(localIco);
                return;
            }

            // 3. Fallback: системная иконка приложения
            _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        catch
        {
            try
            {
                _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            catch { }
        }
    }

    /// <summary>
    /// Обновляет всплывающую подсказку иконки при наведении мыши.
    /// </summary>
    public void UpdateTooltip(string tooltip)
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.ToolTipText = tooltip;
        }
    }

    /// <summary>
    /// Показывает системное всплывающее уведомление Windows (Balloon notification).
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        _taskbarIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    public void ShowMainWindow()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),       // Raycast Dark BgSecondary (#18181B)
            BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 51)),      // BorderSubtle (#2E2E33)
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
            FontSize = 13,
            Padding = new Thickness(4)
        };

        // Заголовок меню
        var header = new MenuItem
        {
            Header = "🎮 Ping Monitoring",
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)) });

        // 1. Открыть окно
        var openItem = new MenuItem
        {
            Header = "🖥️ Открыть главное окно",
            FontWeight = FontWeights.SemiBold
        };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        // 2. Вкл / Выкл HUD-оверлей
        var overlayItem = new MenuItem
        {
            Header = "🎮 HUD-Оверлей (Вкл / Выкл)",
            InputGestureText = "Ctrl+Shift+O"
        };
        overlayItem.Click += (_, _) => _toggleOverlayAction();
        menu.Items.Add(overlayItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)) });

        // 3. Автозагрузка Windows
        _startupMenuItem = new MenuItem
        {
            IsCheckable = true
        };
        _startupMenuItem.Click += (_, _) =>
        {
            bool newState = !StartupService.IsStartupEnabled();
            StartupService.SetStartupEnabled(newState);
            UpdateMenuItemsState();
        };
        menu.Items.Add(_startupMenuItem);

        // 4. Сворачивать в трей при закрытии
        _closeToTrayMenuItem = new MenuItem
        {
            IsCheckable = true
        };
        _closeToTrayMenuItem.Click += (_, _) =>
        {
            MinimizeToTrayOnClose = !MinimizeToTrayOnClose;
            UpdateMenuItemsState();
        };
        menu.Items.Add(_closeToTrayMenuItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)) });

        // 5. Выход
        var exitItem = new MenuItem
        {
            Header = "❌ Выход",
            Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) // Красный акцент
        };
        exitItem.Click += (_, _) => _explicitExitAction();
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) => UpdateMenuItemsState();

        UpdateMenuItemsState();

        _contextMenu = menu;
    }

    private void UpdateMenuItemsState()
    {
        if (_startupMenuItem != null)
        {
            bool startup = StartupService.IsStartupEnabled();
            _startupMenuItem.IsChecked = startup;
            _startupMenuItem.Header = startup
                ? "✅ Автозагрузка с Windows [ВКЛ]"
                : "⚪ Автозагрузка с Windows [ВЫКЛ]";
        }
        if (_closeToTrayMenuItem != null)
        {
            _closeToTrayMenuItem.IsChecked = MinimizeToTrayOnClose;
            _closeToTrayMenuItem.Header = MinimizeToTrayOnClose
                ? "✅ Сворачивать в трей при закрытии [ВКЛ]"
                : "⚪ Сворачивать в трей при закрытии [ВЫКЛ]";
        }
    }

    public void Dispose()
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.Visibility = Visibility.Collapsed;
            _taskbarIcon.Dispose();
            _taskbarIcon = null;
        }
    }
}
