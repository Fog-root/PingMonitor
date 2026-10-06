using CommunityToolkit.Mvvm.ComponentModel;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.ViewModels;

public partial class DotaSdrItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _ping = string.Empty;

    [ObservableProperty]
    private string _route = string.Empty;

    [ObservableProperty]
    private string _pingColor = string.Empty;

    [ObservableProperty]
    private string _pingBg = string.Empty;

    public long RawPingMs { get; set; }

    public string DisplayName => LocalizationService.IsRussian
        ? Name
        : Name.Replace("Москва", "Moscow")
              .Replace("Красноярск", "Krasnoyarsk")
              .Replace("Екатеринбург", "Yekaterinburg")
              .Replace("Хабаровск", "Khabarovsk");

    public string DisplayRoute
    {
        get
        {
            if (LocalizationService.IsRussian)
            {
                return Route;
            }

            if (string.IsNullOrEmpty(Route))
                return string.Empty;

            if (Route.Equals("Прямой (Direct)", StringComparison.OrdinalIgnoreCase) ||
                Route.Equals("Прямой", StringComparison.OrdinalIgnoreCase))
            {
                return "Direct";
            }

            if (Route.Equals("Недоступен", StringComparison.OrdinalIgnoreCase))
            {
                return "Unavailable";
            }

            if (Route.StartsWith("Прямой (", StringComparison.OrdinalIgnoreCase) && Route.EndsWith(")"))
            {
                return "Direct (" + Route.Substring(8);
            }

            if (Route.Contains(" через ", StringComparison.OrdinalIgnoreCase))
            {
                return Route.Replace(" через ", " via ", StringComparison.OrdinalIgnoreCase);
            }

            return Route;
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnRouteChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayRoute));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayRoute));
    }

    public void RefreshTheme()
    {
        var (color, softColor) = ThemeService.GetPingColor(RawPingMs);
        PingColor = color;
        PingBg = softColor;
    }
}
