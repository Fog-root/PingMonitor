using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.ViewModels;

public partial class GameItemViewModel : ObservableObject
{
    private static readonly Brush ActiveBg = CreateFrozenBrush("#181B21");
    private static readonly Brush ActiveBorder = CreateFrozenBrush("#353C46");
    private static readonly Brush InactiveFg = CreateFrozenBrush("#A0A8B4");
    private static readonly Brush ActiveFg = CreateFrozenBrush("#FFFFFF");

    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
    public string IconColorHex { get; set; } = "#59D499";
    public int SteamAppId { get; set; }

    private Brush? _iconBrush;
    public Brush IconBrush
    {
        get
        {
            if (_iconBrush == null)
            {
                try
                {
                    _iconBrush = CreateFrozenBrush(IconColorHex);
                }
                catch
                {
                    _iconBrush = CreateFrozenBrush("#59D499");
                }
            }
            return _iconBrush;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonBackground))]
    [NotifyPropertyChangedFor(nameof(ButtonBorderBrush))]
    [NotifyPropertyChangedFor(nameof(ButtonForeground))]
    [NotifyPropertyChangedFor(nameof(ButtonFontWeight))]
    private bool _isSelected;

    public Brush ButtonBackground => IsSelected ? ActiveBg : Brushes.Transparent;
    public Brush ButtonBorderBrush => IsSelected ? ActiveBorder : Brushes.Transparent;
    public Brush ButtonForeground => IsSelected ? ActiveFg : InactiveFg;
    public FontWeight ButtonFontWeight => IsSelected ? FontWeights.SemiBold : FontWeights.Normal;

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
