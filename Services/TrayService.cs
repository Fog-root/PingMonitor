using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис системного трея Windows (Notification Area / Область уведомлений панели задач).
/// Обеспечивает отображение иконки Ping Monitoring, контекстное меню Raycast Dark и сворачивание в трей.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 1024;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private readonly Window _mainWindow;
    private readonly Action _toggleOverlayAction;
    private readonly Action _explicitExitAction;

    private NOTIFYICONDATA _nid;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isCreated;
    private ContextMenu? _contextMenu;

    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool MinimizeToTrayOnMinimize { get; set; } = false;

    public TrayService(Window mainWindow, Action toggleOverlayAction, Action explicitExitAction)
    {
        _mainWindow = mainWindow;
        _toggleOverlayAction = toggleOverlayAction;
        _explicitExitAction = explicitExitAction;
    }

    /// <summary>
    /// Инициализирует иконку в трее и перехват событий оконных сообщений.
    /// </summary>
    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(_mainWindow).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        _hIcon = LoadTrayIcon();

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "Ping Monitoring"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);

        BuildContextMenu();
    }

    /// <summary>
    /// Обновляет всплывающую подсказку иконки при наведении мыши.
    /// </summary>
    public void UpdateTooltip(string tooltip)
    {
        if (!_isCreated) return;
        if (tooltip.Length > 127) tooltip = tooltip.Substring(0, 127);

        _nid.szTip = tooltip;
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    /// <summary>
    /// Показывает системное всплывающее уведомление Windows (Balloon notification).
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        if (!_isCreated) return;
        if (title.Length > 63) title = title.Substring(0, 63);
        if (message.Length > 255) message = message.Substring(0, 255);

        _nid.szInfoTitle = title;
        _nid.szInfo = message;
        _nid.dwInfoFlags = 0x00000001; // NIIF_INFO
        _nid.uFlags = NIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = lParam.ToInt32();
            switch (mouseMsg)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    _mainWindow.Dispatcher.Invoke(ShowMainWindow);
                    handled = true;
                    break;

                case WM_RBUTTONUP:
                    _mainWindow.Dispatcher.Invoke(ShowContextMenu);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void ShowMainWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    private void ShowContextMenu()
    {
        if (_contextMenu == null) BuildContextMenu();
        if (_contextMenu == null) return;

        UpdateMenuItemsState();

        SetForegroundWindow(_hwnd);
        _contextMenu.Placement = PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private MenuItem? _startupMenuItem;
    private MenuItem? _closeToTrayMenuItem;

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
            Header = "🚀 Автозагрузка с Windows (Диспетчер задач)",
            IsCheckable = true,
            IsChecked = StartupService.IsStartupEnabled()
        };
        _startupMenuItem.Click += (_, _) =>
        {
            bool newState = !StartupService.IsStartupEnabled();
            StartupService.SetStartupEnabled(newState);
            _startupMenuItem.IsChecked = StartupService.IsStartupEnabled();
        };
        menu.Items.Add(_startupMenuItem);

        // 4. Сворачивать в трей при закрытии
        _closeToTrayMenuItem = new MenuItem
        {
            Header = "📥 Сворачивать в трей при закрытии [X]",
            IsCheckable = true,
            IsChecked = MinimizeToTrayOnClose
        };
        _closeToTrayMenuItem.Click += (_, _) =>
        {
            MinimizeToTrayOnClose = !MinimizeToTrayOnClose;
            _closeToTrayMenuItem.IsChecked = MinimizeToTrayOnClose;
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

        _contextMenu = menu;
    }

    private void UpdateMenuItemsState()
    {
        if (_startupMenuItem != null)
        {
            _startupMenuItem.IsChecked = StartupService.IsStartupEnabled();
        }
        if (_closeToTrayMenuItem != null)
        {
            _closeToTrayMenuItem.IsChecked = MinimizeToTrayOnClose;
        }
    }

    private IntPtr LoadTrayIcon()
    {
        try
        {
            // 1. Попытка извлечь иконку из запущенного .exe
            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                IntPtr icon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (icon != IntPtr.Zero) return icon;
            }

            // 2. Попытка загрузить из файла app.ico рядом с программой
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(localIco))
            {
                IntPtr icon = LoadImage(IntPtr.Zero, localIco, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
                if (icon != IntPtr.Zero) return icon;
            }
        }
        catch
        {
            // Игнорируем ошибки загрузки иконки
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
        }

        if (_hwndSource != null)
        {
            try { _hwndSource.RemoveHook(WndProc); } catch { }
            _hwndSource = null;
        }
    }
}
