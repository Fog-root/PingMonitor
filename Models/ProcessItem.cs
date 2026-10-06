using System;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Models;

/// <summary>
/// Высокопроизводительная модель элемента процесса для Диспетчера задач с замороженными (Frozen) кистями для нулевых аллокаций в WPF.
/// </summary>
public partial class ProcessItem : ObservableObject
{
    private static readonly Brush ColorRed = CreateFrozenBrush("#FF6161");
    private static readonly Brush ColorYellow = CreateFrozenBrush("#FFC533");
    private static readonly Brush ColorBlue = CreateFrozenBrush("#57C1FF");
    private static readonly Brush ColorPurple = CreateFrozenBrush("#BD93F9");
    private static readonly Brush ColorGreen = CreateFrozenBrush("#59D499");
    private static readonly Brush ColorWhite = CreateFrozenBrush("#FFFFFF");
    private static readonly Brush ColorSecondary = CreateFrozenBrush("#A0A8B4");
    private static readonly Brush ColorMuted = CreateFrozenBrush("#606773");

    private static readonly Brush BadgeRed = CreateFrozenBrush("#22FF6161");
    private static readonly Brush BadgeYellow = CreateFrozenBrush("#22FFC533");
    private static readonly Brush BadgeBlue = CreateFrozenBrush("#1857C1FF");
    private static readonly Brush BadgePurple = CreateFrozenBrush("#18BD93F9");
    private static readonly Brush StatusSoftGreen = CreateFrozenBrush("#1859D499");

    public Visibility IconVisibility => ThemeService.IsOptimizedMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FilePathVisibility => ThemeService.IsOptimizedMode ? Visibility.Collapsed : Visibility.Visible;
    public Thickness RowMargin => ThemeService.IsOptimizedMode ? new Thickness(2, 0, 2, 0) : new Thickness(4, 2, 4, 2);

    public CornerRadius BadgeCornerRadius => ThemeService.IsOptimizedMode ? new CornerRadius(0) : new CornerRadius(4);
    public Thickness BadgePadding => ThemeService.IsOptimizedMode ? new Thickness(4, 1, 4, 1) : new Thickness(6, 2, 6, 2);

    public string FormattedProcessName => ThemeService.IsOptimizedMode ? Name : DisplayName;
    public FontFamily ProcessFontFamily => ThemeService.IsOptimizedMode 
        ? new FontFamily("Consolas, Segoe UI") 
        : new FontFamily("Segoe UI, Inter");
    public double ProcessNameFontSize => ThemeService.IsOptimizedMode ? 11.0 : 12.0;
    public FontWeight ProcessNameFontWeight => ThemeService.IsOptimizedMode ? FontWeights.Normal : FontWeights.SemiBold;

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    [NotifyPropertyChangedFor(nameof(CpuForeground))]
    [NotifyPropertyChangedFor(nameof(CpuBadgeBackground))]
    private double _cpuPercent;

    public string CpuText => CpuPercent > 0.05 ? $"{CpuPercent:F1}%" : "0,0%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuText))]
    [NotifyPropertyChangedFor(nameof(GpuForeground))]
    [NotifyPropertyChangedFor(nameof(GpuBadgeBackground))]
    private double _gpuPercent;

    public string GpuText => GpuPercent > 0.05 ? $"{GpuPercent:F1}%" : "0,0%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamText))]
    [NotifyPropertyChangedFor(nameof(RamForeground))]
    [NotifyPropertyChangedFor(nameof(RamBadgeBackground))]
    private long _ramBytes;

    public string RamText
    {
        get
        {
            string gb = LocalizationService.IsRussian ? "ГБ" : "GB";
            string mb = LocalizationService.IsRussian ? "МБ" : "MB";
            if (RamBytes >= 1024L * 1024 * 1024)
            {
                return $"{(double)RamBytes / (1024 * 1024 * 1024):F1} {gb}";
            }
            return $"{(double)RamBytes / (1024 * 1024):F0} {mb}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskText))]
    private double _diskBytesPerSec;

    public string DiskText
    {
        get
        {
            string mb = LocalizationService.IsRussian ? "МБ/с" : "MB/s";
            string kb = LocalizationService.IsRussian ? "КБ/с" : "KB/s";
            if (DiskBytesPerSec <= 1024) return $"0 {mb}";
            if (DiskBytesPerSec >= 1024 * 1024)
            {
                return $"{DiskBytesPerSec / (1024 * 1024):F1} {mb}";
            }
            return $"{DiskBytesPerSec / 1024:F0} {kb}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkText))]
    [NotifyPropertyChangedFor(nameof(NetworkForeground))]
    [NotifyPropertyChangedFor(nameof(NetworkBadgeBackground))]
    private double _networkBytesPerSec;

    public string NetworkText
    {
        get
        {
            string mb = LocalizationService.IsRussian ? "МБ/с" : "MB/s";
            string kb = LocalizationService.IsRussian ? "КБ/с" : "KB/s";
            if (NetworkBytesPerSec >= 1024 * 1024)
            {
                return $"{NetworkBytesPerSec / (1024.0 * 1024.0):F1} {mb}";
            }
            if (NetworkBytesPerSec >= 1024)
            {
                return $"{NetworkBytesPerSec / 1024.0:F0} {kb}";
            }
            return $"0 {mb}";
        }
    }

    [ObservableProperty]
    private int _threadsCount;

    [ObservableProperty]
    private string _status = "Работает";

    [ObservableProperty]
    private bool _isResponding = true;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(DiskText));
        OnPropertyChanged(nameof(NetworkText));
        Status = IsResponding
            ? (LocalizationService.IsRussian ? "Работает" : "Running")
            : (LocalizationService.IsRussian ? "Не отвечает" : "Not responding");
    }

    /// <summary>
    /// Мутация метрик существующего объекта на месте (In-Place Reconciliation) без пересоздания объекта или строк в DOM/WPF.
    /// Отфильтровывает микро-колебания ниже порога отображения (epsilon) для исключения лишних уведомлений и layout thrashing.
    /// </summary>
    public void UpdateMetricsFrom(ProcessItem other)
    {
        // 1. CPU: обновление только при видимом изменении (>= 0.05%) или переходе через 0
        if (Math.Abs(CpuPercent - other.CpuPercent) >= 0.05 || (CpuPercent == 0 && other.CpuPercent > 0) || (CpuPercent > 0 && other.CpuPercent == 0))
        {
            CpuPercent = other.CpuPercent;
        }

        // 2. GPU: обновление только при видимом изменении (>= 0.05%)
        if (Math.Abs(GpuPercent - other.GpuPercent) >= 0.05 || (GpuPercent == 0 && other.GpuPercent > 0) || (GpuPercent > 0 && other.GpuPercent == 0))
        {
            GpuPercent = other.GpuPercent;
        }

        // 3. RAM: порог 512 КБ для предотвращения микродропов
        if (Math.Abs(RamBytes - other.RamBytes) >= 512 * 1024)
        {
            RamBytes = other.RamBytes;
        }

        // 4. Диск (I/O)
        if (Math.Abs(DiskBytesPerSec - other.DiskBytesPerSec) >= 1024)
        {
            DiskBytesPerSec = other.DiskBytesPerSec;
        }

        // 5. Сеть
        if (Math.Abs(NetworkBytesPerSec - other.NetworkBytesPerSec) >= 1024)
        {
            NetworkBytesPerSec = other.NetworkBytesPerSec;
        }

        // 6. Потоки
        if (ThreadsCount != other.ThreadsCount)
        {
            ThreadsCount = other.ThreadsCount;
        }

        // 7. Состояние отклика
        if (IsResponding != other.IsResponding)
        {
            IsResponding = other.IsResponding;
            Status = other.Status;
        }

        // 8. Заголовок окна / отображаемое имя
        if (!string.IsNullOrEmpty(other.DisplayName) && DisplayName != other.DisplayName)
        {
            DisplayName = other.DisplayName;
            WindowTitle = other.WindowTitle;
            OnPropertyChanged(nameof(FormattedProcessName));
        }

        // 9. Путь к файлу
        if (!string.IsNullOrEmpty(other.FilePath) && FilePath != other.FilePath)
        {
            FilePath = other.FilePath;
        }

        // 10. Иконка
        if (Icon == null && other.Icon != null && !ThemeService.IsOptimizedMode)
        {
            Icon = other.Icon;
        }
    }

    // Стилизация для тепловой карты (Frozen Brushes = 0 allocations, hardware accelerated)
    // В режиме оптимизации используется чистый монохромный вид
    public Brush CpuForeground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return CpuPercent > 0.05 ? ColorWhite : ColorMuted;
            if (CpuPercent >= 35.0) return ColorRed;
            if (CpuPercent >= 15.0) return ColorYellow;
            if (CpuPercent >= 3.0) return ColorBlue;
            return ColorMuted;
        }
    }

    public Brush CpuBadgeBackground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return Brushes.Transparent;
            if (CpuPercent >= 35.0) return BadgeRed;
            if (CpuPercent >= 15.0) return BadgeYellow;
            if (CpuPercent >= 3.0) return BadgeBlue;
            return Brushes.Transparent;
        }
    }

    public Brush GpuForeground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return GpuPercent > 0.05 ? ColorWhite : ColorMuted;
            if (GpuPercent >= 35.0) return ColorRed;
            if (GpuPercent >= 15.0) return ColorYellow;
            if (GpuPercent >= 3.0) return ColorPurple;
            return ColorMuted;
        }
    }

    public Brush GpuBadgeBackground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return Brushes.Transparent;
            if (GpuPercent >= 35.0) return BadgeRed;
            if (GpuPercent >= 15.0) return BadgeYellow;
            if (GpuPercent >= 3.0) return BadgePurple;
            return Brushes.Transparent;
        }
    }

    public Brush RamForeground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return ColorWhite;
            if (RamBytes >= 2L * 1024 * 1024 * 1024) return ColorYellow;
            if (RamBytes >= 800L * 1024 * 1024) return ColorBlue;
            return ColorMuted;
        }
    }

    public Brush RamBadgeBackground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return Brushes.Transparent;
            if (RamBytes >= 2L * 1024 * 1024 * 1024) return BadgeYellow;
            if (RamBytes >= 800L * 1024 * 1024) return BadgeBlue;
            return Brushes.Transparent;
        }
    }

    public Brush DiskForeground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return DiskBytesPerSec > 1024 ? ColorWhite : ColorMuted;
            return ColorSecondary;
        }
    }

    public Brush NetworkForeground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return NetworkBytesPerSec > 1024 ? ColorWhite : ColorMuted;
            if (NetworkBytesPerSec >= 5 * 1024 * 1024) return ColorRed;
            if (NetworkBytesPerSec >= 1024 * 1024) return ColorYellow;
            if (NetworkBytesPerSec >= 50 * 1024) return ColorBlue;
            return ColorMuted;
        }
    }

    public Brush NetworkBadgeBackground
    {
        get
        {
            if (ThemeService.IsOptimizedMode) return Brushes.Transparent;
            if (NetworkBytesPerSec >= 5 * 1024 * 1024) return BadgeRed;
            if (NetworkBytesPerSec >= 1024 * 1024) return BadgeYellow;
            if (NetworkBytesPerSec >= 50 * 1024) return BadgeBlue;
            return Brushes.Transparent;
        }
    }

    public Brush StatusForeground => ThemeService.IsOptimizedMode ? ColorSecondary : ColorGreen;

    public Brush StatusBadgeBackground => ThemeService.IsOptimizedMode ? Brushes.Transparent : StatusSoftGreen;

    public void RefreshOptimizationMode()
    {
        OnPropertyChanged(nameof(IconVisibility));
        OnPropertyChanged(nameof(FilePathVisibility));
        OnPropertyChanged(nameof(RowMargin));
        OnPropertyChanged(nameof(BadgeCornerRadius));
        OnPropertyChanged(nameof(BadgePadding));
        OnPropertyChanged(nameof(FormattedProcessName));
        OnPropertyChanged(nameof(ProcessFontFamily));
        OnPropertyChanged(nameof(ProcessNameFontSize));
        OnPropertyChanged(nameof(ProcessNameFontWeight));
        OnPropertyChanged(nameof(CpuForeground));
        OnPropertyChanged(nameof(CpuBadgeBackground));
        OnPropertyChanged(nameof(GpuForeground));
        OnPropertyChanged(nameof(GpuBadgeBackground));
        OnPropertyChanged(nameof(RamForeground));
        OnPropertyChanged(nameof(RamBadgeBackground));
        OnPropertyChanged(nameof(DiskForeground));
        OnPropertyChanged(nameof(NetworkForeground));
        OnPropertyChanged(nameof(NetworkBadgeBackground));
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(StatusBadgeBackground));
    }

    private string? _iconGlyph;
    public string IconGlyph
    {
        get
        {
            if (_iconGlyph != null) return _iconGlyph;
            string lower = Name.ToLowerInvariant();
            if (lower.Contains("dota")) _iconGlyph = "🎮";
            else if (lower.Contains("steam")) _iconGlyph = "🕹";
            else if (lower.Contains("discord") || lower.Contains("telegram") || lower.Contains("ayugram")) _iconGlyph = "💬";
            else if (lower.Contains("chrome") || lower.Contains("firefox") || lower.Contains("edge")) _iconGlyph = "🌐";
            else if (lower.Contains("code") || lower.Contains("antigravity")) _iconGlyph = "💻";
            else if (lower.Contains("dwm") || lower.Contains("explorer") || lower.Contains("system")) _iconGlyph = "⚙";
            else _iconGlyph = "📄";
            return _iconGlyph;
        }
    }
}
