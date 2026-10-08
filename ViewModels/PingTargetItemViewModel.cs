using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.ViewModels;

/// <summary>
/// Модель элемента целевого сервера с динамическим индикатором качества связи (3 полоски сигнала и отклик)
/// </summary>
public class PingTargetItemViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public bool IsCustom { get; set; } = false;
    public int CustomId { get; set; } = 0;

    public string DisplayName => GetLocalizedName(Name);

    public static string GetLocalizedName(string name)
    {
        if (LocalizationService.IsRussian || string.IsNullOrEmpty(name))
            return name;

        var text = name;
        if (text.Contains("Россия"))
            text = text.Replace("Россия", "Russia");
        if (text.Contains("Мир танков"))
            text = text.Replace("Мир танков", "World of Tanks");
        if (text.Contains("Москва"))
            text = text.Replace("Москва", "Moscow");
        if (text.Contains("Красноярск"))
            text = text.Replace("Красноярск", "Krasnoyarsk");
        if (text.Contains("Екатеринбург"))
            text = text.Replace("Екатеринбург", "Yekaterinburg");
        if (text.Contains("Хабаровск"))
            text = text.Replace("Хабаровск", "Khabarovsk");

        return text;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        UpdatePing(PingMs);
    }

    private long _pingMs = -1;
    public long PingMs
    {
        get => _pingMs;
        private set => SetProperty(ref _pingMs, value);
    }

    private string _pingBadgeText = "-- ms";
    public string PingBadgeText
    {
        get => _pingBadgeText;
        private set => SetProperty(ref _pingBadgeText, value);
    }

    private Brush _pingBadgeForeground = _defaultMutedBrush;
    public Brush PingBadgeForeground
    {
        get => _pingBadgeForeground;
        private set => SetProperty(ref _pingBadgeForeground, value);
    }

    private string _qualityTooltip = "Замер задержки...";
    public string QualityTooltip
    {
        get => _qualityTooltip;
        private set => SetProperty(ref _qualityTooltip, value);
    }

    private Brush _bar1Brush = _inactiveBarBrush;
    public Brush Bar1Brush
    {
        get => _bar1Brush;
        private set => SetProperty(ref _bar1Brush, value);
    }

    private Brush _bar2Brush = _inactiveBarBrush;
    public Brush Bar2Brush
    {
        get => _bar2Brush;
        private set => SetProperty(ref _bar2Brush, value);
    }

    private Brush _bar3Brush = _inactiveBarBrush;
    public Brush Bar3Brush
    {
        get => _bar3Brush;
        private set => SetProperty(ref _bar3Brush, value);
    }

    private double _bar1Opacity = 0.25;
    public double Bar1Opacity
    {
        get => _bar1Opacity;
        private set => SetProperty(ref _bar1Opacity, value);
    }

    private double _bar2Opacity = 0.25;
    public double Bar2Opacity
    {
        get => _bar2Opacity;
        private set => SetProperty(ref _bar2Opacity, value);
    }

    private double _bar3Opacity = 0.25;
    public double Bar3Opacity
    {
        get => _bar3Opacity;
        private set => SetProperty(ref _bar3Opacity, value);
    }

    // Статические замороженные кисти по умолчанию
    private static readonly SolidColorBrush _defaultMutedBrush = CreateBrush("#7A8391");
    private static readonly SolidColorBrush _greenBrush = CreateBrush("#22C55E");
    private static readonly SolidColorBrush _yellowBrush = CreateBrush("#EAB308");
    private static readonly SolidColorBrush _redBrush = CreateBrush("#EF4444");
    private static readonly SolidColorBrush _inactiveBarBrush = CreateBrush("#4A5260");

    private static SolidColorBrush CreateBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public PingTargetItemViewModel()
    {
        UpdatePing(-1);
    }

    public PingTargetItemViewModel(string name, string ip)
    {
        Name = name;
        Ip = ip;
        UpdatePing(-1);
    }

    /// <summary>
    /// Обновляет состояние индикатора сигнала и текст отклика в соответствии с задержкой (мс)
    /// </summary>
    public void UpdatePing(long pingMs)
    {
        void Apply()
        {
            PingMs = pingMs;

            if (pingMs <= 0)
            {
                // Сервер недоступен / тайм-аут / замер идет: все 3 полоски приглушены
                PingBadgeText = "-- ms";
                PingBadgeForeground = _defaultMutedBrush;
                QualityTooltip = LocalizationService.IsRussian
                    ? $"{DisplayName}\nЗадержка: нет отклика / замер..."
                    : $"{DisplayName}\nLatency: no response / measuring...";

                Bar1Brush = _inactiveBarBrush;
                Bar2Brush = _inactiveBarBrush;
                Bar3Brush = _inactiveBarBrush;

                Bar1Opacity = 0.25;
                Bar2Opacity = 0.25;
                Bar3Opacity = 0.25;
            }
            else if (pingMs <= 100)
            {
                // Отличный пинг (до 100 мс): активны все 3 полоски, цвет зеленый
                var colorBrush = GetThemeGreenBrush();
                PingBadgeText = $"{pingMs} ms";
                PingBadgeForeground = colorBrush;
                QualityTooltip = LocalizationService.IsRussian
                    ? $"{DisplayName}\nОтличное соединение: {pingMs} ms (до 100 мс)"
                    : $"{DisplayName}\nExcellent connection: {pingMs} ms (<= 100 ms)";

                Bar1Brush = colorBrush;
                Bar2Brush = colorBrush;
                Bar3Brush = colorBrush;

                Bar1Opacity = 1.0;
                Bar2Opacity = 1.0;
                Bar3Opacity = 1.0;
            }
            else if (pingMs <= 200)
            {
                // Умеренный пинг (101–200 мс): активны 2 полоски (3-я полупрозрачная), цвет янтарный/желтый
                var colorBrush = GetThemeYellowBrush();
                PingBadgeText = $"{pingMs} ms";
                PingBadgeForeground = colorBrush;
                QualityTooltip = LocalizationService.IsRussian
                    ? $"{DisplayName}\nУмеренное соединение: {pingMs} ms (101–200 мс)"
                    : $"{DisplayName}\nModerate connection: {pingMs} ms (101–200 ms)";

                Bar1Brush = colorBrush;
                Bar2Brush = colorBrush;
                Bar3Brush = _inactiveBarBrush;

                Bar1Opacity = 1.0;
                Bar2Opacity = 1.0;
                Bar3Opacity = 0.22;
            }
            else
            {
                // Высокий пинг (> 200 мс): активна только 1 полоска, цвет красный
                var colorBrush = GetThemeRedBrush();
                PingBadgeText = $"{pingMs} ms";
                PingBadgeForeground = colorBrush;
                QualityTooltip = LocalizationService.IsRussian
                    ? $"{DisplayName}\nВысокая задержка: {pingMs} ms (> 200 мс)"
                    : $"{DisplayName}\nHigh latency: {pingMs} ms (> 200 ms)";

                Bar1Brush = colorBrush;
                Bar2Brush = _inactiveBarBrush;
                Bar3Brush = _inactiveBarBrush;

                Bar1Opacity = 1.0;
                Bar2Opacity = 0.22;
                Bar3Opacity = 0.22;
            }
        }

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)Apply);
        }
        else
        {
            Apply();
        }
    }

    private static Brush GetThemeGreenBrush()
    {
        try
        {
            if (!string.IsNullOrEmpty(ThemeService.Current?.AccentGreen))
            {
                return (Brush)new BrushConverter().ConvertFromString(ThemeService.Current.AccentGreen)!;
            }
        }
        catch { }
        return _greenBrush;
    }

    private static Brush GetThemeYellowBrush()
    {
        try
        {
            if (!string.IsNullOrEmpty(ThemeService.Current?.AccentYellow))
            {
                return (Brush)new BrushConverter().ConvertFromString(ThemeService.Current.AccentYellow)!;
            }
        }
        catch { }
        return _yellowBrush;
    }

    private static Brush GetThemeRedBrush()
    {
        try
        {
            if (!string.IsNullOrEmpty(ThemeService.Current?.AccentRed))
            {
                return (Brush)new BrushConverter().ConvertFromString(ThemeService.Current.AccentRed)!;
            }
        }
        catch { }
        return _redBrush;
    }

    public override string ToString() => Name;
}
