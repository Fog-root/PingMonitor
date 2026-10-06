using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DotaPingMonitor.Services;

public record ThemeDefinition(
    string Id,
    string Name,
    string Description,
    string BgPrimary,
    string BgSurface,
    string BgCard,
    string BgCardElevated,
    string BgCardHover,
    string BgInput,
    string BorderColor,
    string BorderSubtle,
    string BorderHover,
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string AccentGreen,
    string AccentGreenSoft,
    string AccentYellow,
    string AccentYellowSoft,
    string AccentRed,
    string AccentRedSoft,
    string AccentBlue,
    string AccentBlueSoft,
    string PreviewColor,
    string HudPillBg,
    string HudBorder,
    string HudSparklineBg,
    string HudSparklineBorder,
    string HudSdrBadgeBg,
    string HudSdrBadgeText,
    string HudTextColor,
    string AccentColor = "#5E6AD2",
    string AccentGlow = "#265E6AD2",
    string PreviewColorSecondary = ""
);

public partial class ThemeItemViewModel : ObservableObject
{
    public string Id { get; set; } = "";
    [ObservableProperty]
    private string _name = "";
    [ObservableProperty]
    private string _description = "";
    public Brush PreviewBrush { get; set; } = Brushes.Transparent;
}

public static class ThemeService
{
    public static readonly IReadOnlyList<ThemeDefinition> Themes = new List<ThemeDefinition>
    {
        // 1. Linear Dark (Основная тема по умолчанию)
        new(
            Id: "Linear",
            Name: "Linear Dark",
            Description: "Минималистичный премиум-графит Linear, индиго-фиолетовый акцент #5E6AD2 и субтильные границы",
            BgPrimary: "#0B0C0E",
            BgSurface: "#101114",
            BgCard: "#121316",
            BgCardElevated: "#181A1F",
            BgCardHover: "#22252C",
            BgInput: "#0E0F12",
            BorderColor: "#232730",
            BorderSubtle: "#181B22",
            BorderHover: "#383E4C",
            TextPrimary: "#F7F8F8",
            TextSecondary: "#969CA6",
            TextMuted: "#5F6570",
            AccentGreen: "#22C55E",
            AccentGreenSoft: "#2022C55E",
            AccentYellow: "#EAB308",
            AccentYellowSoft: "#20EAB308",
            AccentRed: "#EF4444",
            AccentRedSoft: "#20EF4444",
            AccentBlue: "#5E6AD2",
            AccentBlueSoft: "#265E6AD2",
            PreviewColor: "#5E6AD2",
            HudPillBg: "#F2121316",
            HudBorder: "#2E3440",
            HudSparklineBg: "#0E0F12",
            HudSparklineBorder: "#232730",
            HudSdrBadgeBg: "#265E6AD2",
            HudSdrBadgeText: "#8E98FF",
            HudTextColor: "#969CA6",
            AccentColor: "#5E6AD2",
            AccentGlow: "#265E6AD2",
            PreviewColorSecondary: "#8E98FF"
        ),

        // 2. Cyberpunk Neon
        new(
            Id: "Cyberpunk",
            Name: "Cyberpunk Neon",
            Description: "Ультраглубокий ночной фиолетовый #08070D, неоновый бирюзовый Cyan #00F0FF и горячий Magenta #FF0055",
            BgPrimary: "#08070D",
            BgSurface: "#0D0B16",
            BgCard: "#100D1A",
            BgCardElevated: "#19142A",
            BgCardHover: "#281F44",
            BgInput: "#0D0A17",
            BorderColor: "#4D1C66",
            BorderSubtle: "#2C123D",
            BorderHover: "#8025A8",
            TextPrimary: "#FFFFFF",
            TextSecondary: "#D8B4FE",
            TextMuted: "#8668A6",
            AccentGreen: "#00FF9D",
            AccentGreenSoft: "#2000FF9D",
            AccentYellow: "#FFE600",
            AccentYellowSoft: "#20FFE600",
            AccentRed: "#FF0055",
            AccentRedSoft: "#25FF0055",
            AccentBlue: "#00F0FF",
            AccentBlueSoft: "#2500F0FF",
            PreviewColor: "#00F0FF",
            HudPillBg: "#F40D0B16",
            HudBorder: "#6E2596",
            HudSparklineBg: "#100D1A",
            HudSparklineBorder: "#4D1C66",
            HudSdrBadgeBg: "#2E00F0FF",
            HudSdrBadgeText: "#00F0FF",
            HudTextColor: "#D8B4FE",
            AccentColor: "#00F0FF",
            AccentGlow: "#2E00F0FF",
            PreviewColorSecondary: "#FF0055"
        ),

        // 3. Emerald Matrix / Toxic Green
        new(
            Id: "Matrix",
            Name: "Emerald Matrix / Toxic Green",
            Description: "Темно-изумрудный глубокий фон #0A110E, неоновый электрический лайм #10B981 и #00FF88",
            BgPrimary: "#0A110E",
            BgSurface: "#0D1612",
            BgCard: "#0F1A15",
            BgCardElevated: "#15261F",
            BgCardHover: "#1D362B",
            BgInput: "#0B1410",
            BorderColor: "#1E3E2E",
            BorderSubtle: "#142A1F",
            BorderHover: "#2D6349",
            TextPrimary: "#F0FDF4",
            TextSecondary: "#86EFAC",
            TextMuted: "#4E7862",
            AccentGreen: "#00FF88",
            AccentGreenSoft: "#2200FF88",
            AccentYellow: "#FACC15",
            AccentYellowSoft: "#22FACC15",
            AccentRed: "#F43F5E",
            AccentRedSoft: "#22F43F5E",
            AccentBlue: "#10B981",
            AccentBlueSoft: "#2210B981",
            PreviewColor: "#00FF88",
            HudPillBg: "#F40D1612",
            HudBorder: "#25593F",
            HudSparklineBg: "#0F1A15",
            HudSparklineBorder: "#1E3E2E",
            HudSdrBadgeBg: "#2810B981",
            HudSdrBadgeText: "#00FF88",
            HudTextColor: "#86EFAC",
            AccentColor: "#00FF88",
            AccentGlow: "#2800FF88",
            PreviewColorSecondary: "#10B981"
        ),

        // 4. Blood Moon / Crimson Fury
        new(
            Id: "BloodMoon",
            Name: "Blood Moon / Crimson Fury",
            Description: "Темно-бордовый угольный фон #0F070A, ярко-алый рубин #EF4444 и пламенно-оранжевые блики",
            BgPrimary: "#0F070A",
            BgSurface: "#14090E",
            BgCard: "#180C10",
            BgCardElevated: "#241219",
            BgCardHover: "#351A25",
            BgInput: "#12070C",
            BorderColor: "#4A1C2A",
            BorderSubtle: "#30121B",
            BorderHover: "#782840",
            TextPrimary: "#FFF1F2",
            TextSecondary: "#FDA4AF",
            TextMuted: "#8C5A66",
            AccentGreen: "#10B981",
            AccentGreenSoft: "#2210B981",
            AccentYellow: "#F97316",
            AccentYellowSoft: "#22F97316",
            AccentRed: "#EF4444",
            AccentRedSoft: "#25EF4444",
            AccentBlue: "#F43F5E",
            AccentBlueSoft: "#25F43F5E",
            PreviewColor: "#EF4444",
            HudPillBg: "#F414090E",
            HudBorder: "#662238",
            HudSparklineBg: "#180C10",
            HudSparklineBorder: "#4A1C2A",
            HudSdrBadgeBg: "#28EF4444",
            HudSdrBadgeText: "#FDA4AF",
            HudTextColor: "#FDA4AF",
            AccentColor: "#EF4444",
            AccentGlow: "#28EF4444",
            PreviewColorSecondary: "#F97316"
        ),

        // 5. Nordic Frost / Deep Ice
        new(
            Id: "Nordic",
            Name: "Nordic Frost / Deep Ice",
            Description: "Холодный темно-синий #0B1320, ледяной аквамарин #38BDF8 и чистый белый иней #E0F2FE",
            BgPrimary: "#0B1320",
            BgSurface: "#0E1727",
            BgCard: "#101B2E",
            BgCardElevated: "#17263E",
            BgCardHover: "#213554",
            BgInput: "#0D1524",
            BorderColor: "#263D61",
            BorderSubtle: "#182942",
            BorderHover: "#3B5E94",
            TextPrimary: "#F0F9FF",
            TextSecondary: "#BAE6FD",
            TextMuted: "#6282A8",
            AccentGreen: "#34D399",
            AccentGreenSoft: "#2234D399",
            AccentYellow: "#FBBF24",
            AccentYellowSoft: "#22FBBF24",
            AccentRed: "#F87171",
            AccentRedSoft: "#22F87171",
            AccentBlue: "#38BDF8",
            AccentBlueSoft: "#2538BDF8",
            PreviewColor: "#38BDF8",
            HudPillBg: "#F40E1727",
            HudBorder: "#335384",
            HudSparklineBg: "#101B2E",
            HudSparklineBorder: "#263D61",
            HudSdrBadgeBg: "#2838BDF8",
            HudSdrBadgeText: "#BAE6FD",
            HudTextColor: "#BAE6FD",
            AccentColor: "#38BDF8",
            AccentGlow: "#2838BDF8",
            PreviewColorSecondary: "#E0F2FE"
        ),

        // 6. Synthwave Sunset
        new(
            Id: "Synthwave",
            Name: "Synthwave Sunset",
            Description: "Темный индиго/пурпурный #0E091B, теплый закат от золотого персика #F59E0B к фуксии #D946EF",
            BgPrimary: "#0E091B",
            BgSurface: "#120C23",
            BgCard: "#160E2B",
            BgCardElevated: "#20143E",
            BgCardHover: "#2E1E57",
            BgInput: "#110B22",
            BorderColor: "#43266B",
            BorderSubtle: "#2B1745",
            BorderHover: "#7338A8",
            TextPrimary: "#FAF5FF",
            TextSecondary: "#E9D5FF",
            TextMuted: "#8469A3",
            AccentGreen: "#10B981",
            AccentGreenSoft: "#2210B981",
            AccentYellow: "#F59E0B",
            AccentYellowSoft: "#25F59E0B",
            AccentRed: "#F43F5E",
            AccentRedSoft: "#25F43F5E",
            AccentBlue: "#D946EF",
            AccentBlueSoft: "#25D946EF",
            PreviewColor: "#D946EF",
            HudPillBg: "#F4120C23",
            HudBorder: "#5C3194",
            HudSparklineBg: "#160E2B",
            HudSparklineBorder: "#43266B",
            HudSdrBadgeBg: "#28D946EF",
            HudSdrBadgeText: "#E9D5FF",
            HudTextColor: "#E9D5FF",
            AccentColor: "#D946EF",
            AccentGlow: "#28D946EF",
            PreviewColorSecondary: "#F59E0B"
        ),

        // 7. Monochrome Cyber / Noir Cyberpunk
        new(
            Id: "MonochromeCyber",
            Name: "Monochrome Cyber",
            Description: "Строгий неоновый нуар-киберпанк: глубокий матовый черный #000000, графитовые подложки и неоновые белые контуры #FFFFFF",
            BgPrimary: "#000000",
            BgSurface: "#09090B",
            BgCard: "#0D0D10",
            BgCardElevated: "#141417",
            BgCardHover: "#202025",
            BgInput: "#08080A",
            BorderColor: "#333338",
            BorderSubtle: "#1C1C20",
            BorderHover: "#666672",
            TextPrimary: "#FFFFFF",
            TextSecondary: "#E4E4E7",
            TextMuted: "#71717A",
            AccentGreen: "#FFFFFF",
            AccentGreenSoft: "#26FFFFFF",
            AccentYellow: "#A1A1AA",
            AccentYellowSoft: "#20A1A1AA",
            AccentRed: "#EF4444",
            AccentRedSoft: "#25EF4444",
            AccentBlue: "#FFFFFF",
            AccentBlueSoft: "#25FFFFFF",
            PreviewColor: "#FFFFFF",
            HudPillBg: "#F209090B",
            HudBorder: "#383842",
            HudSparklineBg: "#050507",
            HudSparklineBorder: "#27272A",
            HudSdrBadgeBg: "#25FFFFFF",
            HudSdrBadgeText: "#FFFFFF",
            HudTextColor: "#E4E4E7",
            AccentColor: "#FFFFFF",
            AccentGlow: "#33FFFFFF",
            PreviewColorSecondary: "#000000"
        )
    };

    public static ThemeDefinition Current { get; private set; } = Themes[0];

    public static bool IsOptimizedMode { get; private set; }

    public static event Action<ThemeDefinition>? ThemeChanged;
    public static event Action<bool>? OptimizationModeChanged;

    public static void SetOptimizationMode(bool enabled)
    {
        IsOptimizedMode = enabled;

        if (Application.Current != null)
        {
            UpdateAmbientGlowBrushes(Current);
        }

        OptimizationModeChanged?.Invoke(enabled);
    }

    public static Brush CreateThemePreviewBrush(ThemeDefinition theme)
    {
        try
        {
            if (!string.IsNullOrEmpty(theme.PreviewColorSecondary))
            {
                var c1 = (Color)ColorConverter.ConvertFromString(theme.PreviewColor);
                var c2 = (Color)ColorConverter.ConvertFromString(theme.PreviewColorSecondary);
                var grad = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
                grad.Freeze();
                return grad;
            }

            var c = (Color)ColorConverter.ConvertFromString(theme.PreviewColor);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public static void ApplyTheme(string themeId)
    {
        var theme = Themes.FirstOrDefault(t => t.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        if (theme == null)
        {
            theme = themeId?.ToLowerInvariant() switch
            {
                "raycast" => Themes[0], // Linear Dark (По умолчанию)
                "dota2" => Themes.FirstOrDefault(t => t.Id == "BloodMoon") ?? Themes[0],
                "nord" => Themes.FirstOrDefault(t => t.Id == "Nordic") ?? Themes[0],
                "dracula" => Themes.FirstOrDefault(t => t.Id == "Synthwave") ?? Themes[0],
                "monochrome" or "noir" or "cybermonochrome" => Themes.FirstOrDefault(t => t.Id == "MonochromeCyber") ?? Themes[0],
                _ => Themes[0]
            };
        }
        Current = theme;

        UpdateBrush("BgPrimary", theme.BgPrimary);
        UpdateBrush("BgSurface", theme.BgSurface);
        UpdateBrush("BgCard", theme.BgCard);
        UpdateBrush("BgCardElevated", theme.BgCardElevated);
        UpdateBrush("BgCardHover", theme.BgCardHover);
        UpdateBrush("BgInput", theme.BgInput);

        UpdateBrush("BorderColor", theme.BorderColor);
        UpdateBrush("BorderSubtle", theme.BorderSubtle);
        UpdateBrush("BorderHover", theme.BorderHover);

        UpdateBrush("TextPrimary", theme.TextPrimary);
        UpdateBrush("TextSecondary", theme.TextSecondary);
        UpdateBrush("TextMuted", theme.TextMuted);

        UpdateBrush("AccentGreen", theme.AccentGreen);
        UpdateBrush("AccentGreenSoft", theme.AccentGreenSoft);
        UpdateBrush("AccentYellow", theme.AccentYellow);
        UpdateBrush("AccentYellowSoft", theme.AccentYellowSoft);
        UpdateBrush("AccentRed", theme.AccentRed);
        UpdateBrush("AccentRedSoft", theme.AccentRedSoft);
        UpdateBrush("AccentBlue", theme.AccentBlue);
        UpdateBrush("AccentBlueSoft", theme.AccentBlueSoft);

        UpdateBrush("AccentColorBrush", theme.AccentColor);
        UpdateBrush("AccentGlowBrush", theme.AccentGlow);

        if (Application.Current != null)
        {
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(theme.AccentColor);
                Application.Current.Resources["AccentColorValue"] = col;
            }
            catch { }
        }

        UpdateAmbientGlowBrushes(theme);

        ThemeChanged?.Invoke(theme);
    }

    private static void UpdateAmbientGlowBrushes(ThemeDefinition theme)
    {
        if (Application.Current == null) return;
        try
        {
            if (IsOptimizedMode)
            {
                // Режим оптимизации (Flat Mode): полное отключение всех радиальных бликов, верхних градиентов, неонов и теней
                Application.Current.Resources["ThemeAmbientRadialGlow"] = Brushes.Transparent;
                Application.Current.Resources["CardTopShineBrush"] = Brushes.Transparent;
                Application.Current.Resources["ThemeNeonBloom"] = null;
                Application.Current.Resources["CardElevationShadow"] = null;
                Application.Current.Resources["MenuDropShadow"] = null;
                return;
            }

            var glowColor = (Color)ColorConverter.ConvertFromString(theme.AccentGlow);
            var clearColor = Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B);

            // 1. Фоновое радиальное свечение окна (ThemeAmbientRadialGlow)
            var radGlow = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.0),
                GradientOrigin = new Point(0.5, 0.0),
                RadiusX = 0.65,
                RadiusY = 0.45
            };
            radGlow.GradientStops.Add(new GradientStop(glowColor, 0.0));
            radGlow.GradientStops.Add(new GradientStop(clearColor, 1.0));
            radGlow.Freeze();
            Application.Current.Resources["ThemeAmbientRadialGlow"] = radGlow;

            // 2. Блик в верхней части карточек и панелей (CardTopShineBrush)
            var cardShine = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0.0),
                EndPoint = new Point(0.5, 1.0)
            };
            var cardGlowColor = Color.FromArgb(0x18, glowColor.R, glowColor.G, glowColor.B);
            cardShine.GradientStops.Add(new GradientStop(cardGlowColor, 0.0));
            cardShine.GradientStops.Add(new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 1.0));
            cardShine.Freeze();
            Application.Current.Resources["CardTopShineBrush"] = cardShine;

            // 3. Неоновое свечение элементов (ThemeNeonBloom)
            var accentSolid = (Color)ColorConverter.ConvertFromString(theme.AccentColor);
            var bloom = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 0,
                Direction = 0,
                Color = accentSolid,
                Opacity = 0.40
            };
            bloom.Freeze();
            Application.Current.Resources["ThemeNeonBloom"] = bloom;

            // 4. Мягкая тень карточек (CardElevationShadow)
            var cardShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Direction = 270,
                Color = Colors.Black,
                Opacity = 0.35
            };
            cardShadow.Freeze();
            Application.Current.Resources["CardElevationShadow"] = cardShadow;

            // 5. Тень для всплывающих окон и контекстных меню (MenuDropShadow)
            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 5,
                Direction = 270,
                Color = Colors.Black,
                Opacity = 0.75
            };
            shadow.Freeze();
            Application.Current.Resources["MenuDropShadow"] = shadow;
        }
        catch { }
    }

    private static void UpdateBrush(string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (Application.Current != null)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }

    public static (string HexColor, string SoftHexColor) GetPingColor(long pingMs)
    {
        if (pingMs <= 0 || pingMs > 200)
            return (Current.AccentRed, Current.AccentRedSoft);

        if (pingMs > 100)
            return (Current.AccentYellow, Current.AccentYellowSoft);

        return (Current.AccentGreen, Current.AccentGreenSoft);
    }
}
