using System.Diagnostics;
using System.Runtime.InteropServices;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

/// <summary>
/// Глобальный хук клавиатуры через WH_KEYBOARD_LL.
/// Поддерживает кастомные комбинации клавиш с сохранением в конфигурации.
/// </summary>
public class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // Конфигурируемые бинды клавиш
    public HotkeyBinding OverlayBinding { get; set; } = new(true, true, false, false, 0x4F, "Ctrl+Shift+O");
    public HotkeyBinding LockBinding { get; set; } = new(true, true, false, false, 0x4C, "Ctrl+Shift+L");
    public HotkeyBinding SparklineBinding { get; set; } = new(true, true, false, false, 0x47, "Ctrl+Shift+G");
    public HotkeyBinding FpsBinding { get; set; } = new(true, true, false, false, 0x46, "Ctrl+Shift+F");
    public HotkeyBinding CpuBinding { get; set; } = new(true, true, false, false, 0x43, "Ctrl+Shift+C");
    public HotkeyBinding GpuBinding { get; set; } = new(true, true, false, false, 0x55, "Ctrl+Shift+U");
    public HotkeyBinding RamBinding { get; set; } = new(true, true, false, false, 0x52, "Ctrl+Shift+R");

    /// <summary>
    /// Флаг паузы перехвата (активен, когда открыто окно переназначения клавиш).
    /// </summary>
    public bool IsPaused { get; set; }

    public event Action? ToggleOverlayRequested;
    public event Action? ToggleLockRequested;
    public event Action? ToggleSparklineRequested;
    public event Action? ToggleFpsRequested;
    public event Action? ToggleCpuRequested;
    public event Action? ToggleGpuRequested;
    public event Action? ToggleRamRequested;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
        Start();
    }

    public void Start()
    {
        if (_hookId == IntPtr.Zero)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            if (IsPaused)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int vkCode = Marshal.ReadInt32(lParam);

            // Игнорируем нажатия самих клавиш-модификаторов
            if (vkCode == VK_CONTROL || vkCode == 0xA2 || vkCode == 0xA3 || // Control
                vkCode == VK_SHIFT || vkCode == 0xA0 || vkCode == 0xA1 ||   // Shift
                vkCode == VK_MENU || vkCode == 0xA4 || vkCode == 0xA5 ||    // Alt
                vkCode == VK_LWIN || vkCode == VK_RWIN)                    // Windows
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool winDown = ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);

            if (OverlayBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleOverlayRequested?.Invoke();
            }
            else if (LockBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown) ||
                     // Запасной fallback для Ctrl+Shift+K если хоткей остался дефолтным
                     (LockBinding.KeyCode == 0x4C && LockBinding.Ctrl && LockBinding.Shift && vkCode == 0x4B && ctrlDown && shiftDown))
            {
                ToggleLockRequested?.Invoke();
            }
            else if (SparklineBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleSparklineRequested?.Invoke();
            }
            else if (FpsBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleFpsRequested?.Invoke();
            }
            else if (CpuBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleCpuRequested?.Invoke();
            }
            else if (GpuBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleGpuRequested?.Invoke();
            }
            else if (RamBinding.Matches(vkCode, ctrlDown, shiftDown, altDown, winDown))
            {
                ToggleRamRequested?.Invoke();
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
