using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DotaPingMonitor.Services;

/// <summary>
/// Высокопроизводительный сервис извлечения и кэширования иконок процессов Windows Shell (HICON -> WPF ImageSource).
/// Все изображения замораживаются (Freeze), потокобезопасны, дескрипторы GDI уничтожаются немедленно (0 утечек).
/// </summary>
public class ProcessIconService
{
    private static readonly Lazy<ProcessIconService> _lazy = new(() => new ProcessIconService());
    public static ProcessIconService Instance => _lazy.Value;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x00000100;
    private const uint SHGFI_LARGEICON = 0x00000000; // 32x32
    private const uint SHGFI_SMALLICON = 0x00000001; // 16x16
    private const uint SHGFI_USEFILEATTRIBUTES = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pendingResolutions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<ImageSource?> _defaultExeIcon;
    private readonly Lazy<ImageSource?> _systemIcon;
    private readonly Lazy<ImageSource?> _appIcon;

    public ProcessIconService()
    {
        _defaultExeIcon = new Lazy<ImageSource?>(CreateDefaultExeIcon);
        _systemIcon = new Lazy<ImageSource?>(CreateSystemIcon);
        _appIcon = new Lazy<ImageSource?>(CreateAppIcon);
    }

    /// <summary>
    /// Неблокирующий возврат иконки: возвращает кэшированную иконку за 0 мс.
    /// Если иконка еще не извлечена, возвращает null (срабатывает мгновенный векторный глиф) и запускает асинхронное извлечение в фоне.
    /// </summary>
    public ImageSource? GetCachedIcon(string? filePath, string? processName, int pid, Action<ImageSource?>? onResolved = null)
    {
        string safeProc = processName ?? string.Empty;
        string safePath = filePath?.Trim('"', ' ', '\t') ?? string.Empty;

        if (!string.IsNullOrEmpty(safePath))
        {
            try
            {
                safePath = Environment.ExpandEnvironmentVariables(safePath);
            }
            catch { }
        }

        string cacheKey = !string.IsNullOrEmpty(safePath)
            ? safePath.ToLowerInvariant()
            : (!string.IsNullOrEmpty(safeProc) ? safeProc.ToLowerInvariant() : $"pid_{pid}");

        if (_cache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        // Запуск неблокирующего фонового извлечения
        if (_pendingResolutions.TryAdd(cacheKey, 0))
        {
            Task.Run(() =>
            {
                try
                {
                    var resolved = ResolveIcon(safePath, safeProc, pid);
                    _cache[cacheKey] = resolved;
                    if (resolved != null && onResolved != null)
                    {
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            onResolved(resolved);
                        });
                    }
                }
                catch { }
                finally
                {
                    _pendingResolutions.TryRemove(cacheKey, out _);
                }
            });
        }

        return null;
    }

    /// <summary>
    /// Получить готовую иконку для процесса (кэшируется навсегда, мгновенный O(1) возврат для известных процессов).
    /// </summary>
    public ImageSource? GetIcon(string? filePath, string? processName, int pid)
    {
        string safeProc = processName ?? string.Empty;
        string safePath = filePath?.Trim('"', ' ', '\t') ?? string.Empty;

        if (!string.IsNullOrEmpty(safePath))
        {
            try
            {
                safePath = Environment.ExpandEnvironmentVariables(safePath);
            }
            catch { }
        }

        // Ключ кэша: приоритет полному пути исполняемого файла, иначе имени процесса
        string cacheKey = !string.IsNullOrEmpty(safePath)
            ? safePath.ToLowerInvariant()
            : (!string.IsNullOrEmpty(safeProc) ? safeProc.ToLowerInvariant() : $"pid_{pid}");

        if (_cache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        ImageSource? resolved = ResolveIcon(safePath, safeProc, pid);
        _cache[cacheKey] = resolved;
        return resolved;
    }

    private ImageSource? ResolveIcon(string safePath, string safeProc, int pid)
    {
        // 1. Собственное приложение DotaPingMonitor
        if (safeProc.Equals("DotaPingMonitor", StringComparison.OrdinalIgnoreCase) ||
            safePath.Contains("DotaPingMonitor", StringComparison.OrdinalIgnoreCase))
        {
            if (_appIcon.Value != null) return _appIcon.Value;
        }

        // 2. Системные псевдопроцессы ядра (System PID 4, Memory Compression, Idle)
        if (pid == 4 ||
            safeProc.Equals("System", StringComparison.OrdinalIgnoreCase) ||
            safeProc.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
            safeProc.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            if (_systemIcon.Value != null) return _systemIcon.Value;
        }

        // 3. Если путь известен и файл существует на диске
        if (!string.IsNullOrEmpty(safePath) && File.Exists(safePath))
        {
            var icon = ExtractIconFromPath(safePath);
            if (icon != null) return icon;
        }

        // 4. Попытка разрешить известные системные каталоги Windows
        if (!string.IsNullOrEmpty(safeProc))
        {
            try
            {
                string sys32 = Path.Combine(Environment.SystemDirectory, safeProc + ".exe");
                if (File.Exists(sys32))
                {
                    var icon = ExtractIconFromPath(sys32);
                    if (icon != null) return icon;
                }

                string win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), safeProc + ".exe");
                if (File.Exists(win))
                {
                    var icon = ExtractIconFromPath(win);
                    if (icon != null) return icon;
                }

                string wbem = Path.Combine(Environment.SystemDirectory, "wbem", safeProc + ".exe");
                if (File.Exists(wbem))
                {
                    var icon = ExtractIconFromPath(wbem);
                    if (icon != null) return icon;
                }
            }
            catch { }
        }

        // 5. Запасной вариант: официальная иконка исполняемого файла Windows (.EXE)
        return _defaultExeIcon.Value;
    }

    private static ImageSource? ExtractIconFromPath(string path)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            // Сначала пробуем извлечь 32x32 качественную иконку
            IntPtr res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                return CreateFrozenBitmapFromHIcon(shinfo.hIcon);
            }

            // Резерв: маленькая 16x16 иконка
            res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);
            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                return CreateFrozenBitmapFromHIcon(shinfo.hIcon);
            }

            // Резерв по атрибутам файла
            res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                return CreateFrozenBitmapFromHIcon(shinfo.hIcon);
            }
        }
        catch { }

        return null;
    }

    private static ImageSource? CreateFrozenBitmapFromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var bs = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            if (bs.CanFreeze)
            {
                bs.Freeze();
            }
            return bs;
        }
        catch
        {
            return null;
        }
        finally
        {
            // Уничтожаем HICON дескриптор, чтобы исключить утечки GDI дескрипторов Windows
            DestroyIcon(hIcon);
        }
    }

    private static ImageSource? CreateDefaultExeIcon()
    {
        try
        {
            var sh = new SHFILEINFO();
            IntPtr res = SHGetFileInfo(".exe", FILE_ATTRIBUTE_NORMAL, ref sh, (uint)Marshal.SizeOf(sh), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (res != IntPtr.Zero && sh.hIcon != IntPtr.Zero)
            {
                return CreateFrozenBitmapFromHIcon(sh.hIcon);
            }
        }
        catch { }
        return null;
    }

    private static ImageSource? CreateSystemIcon()
    {
        try
        {
            string ntos = Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe");
            if (File.Exists(ntos))
            {
                var icon = ExtractIconFromPath(ntos);
                if (icon != null) return icon;
            }
        }
        catch { }
        return CreateDefaultExeIcon();
    }

    private static ImageSource? CreateAppIcon()
    {
        try
        {
            // Пробуем pack URI ресурса app.ico
            var uri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
            var frame = BitmapFrame.Create(uri);
            if (frame.CanFreeze) frame.Freeze();
            return frame;
        }
        catch { }

        try
        {
            // Резервный поиск файла app.ico рядом со сборкой или исходниками
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(localPath))
            {
                var bmp = new BitmapImage(new Uri(localPath, UriKind.Absolute));
                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
        }
        catch { }

        try
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                return ExtractIconFromPath(procPath);
            }
        }
        catch { }

        return null;
    }
}
