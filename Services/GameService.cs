using DotaPingMonitor.Models;
using DotaPingMonitor.ViewModels;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис управления игровыми дисциплинами (Dota 2, CS2 и будущие игры Valve/SDR).
/// Обеспечивает масштабируемость, пулы серверов и переключение контекста.
/// </summary>
public class GameService
{
    private readonly Dictionary<string, GameProfile> _games = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<GameProfile> Games => _games.Values;

    public GameProfile CurrentGame { get; private set; }

    public event Action<GameProfile>? GameChanged;

    public GameService(string initialGameId = "dota2")
    {
        RegisterDefaultGames();
        CurrentGame = GetGame(initialGameId) ?? _games["dota2"];
    }

    public GameProfile? GetGame(string id)
    {
        return _games.TryGetValue(id, out var game) ? game : null;
    }

    public bool SetCurrentGame(string id)
    {
        if (_games.TryGetValue(id, out var newGame))
        {
            if (CurrentGame.Id.Equals(newGame.Id, StringComparison.OrdinalIgnoreCase))
                return false;

            CurrentGame = newGame;
            GameChanged?.Invoke(CurrentGame);
            return true;
        }

        return false;
    }

    private void RegisterDefaultGames()
    {
        // ==========================================
        // 1. DOTA 2 (AppID: 570)
        // ==========================================
        var dota2 = new GameProfile
        {
            Id = "dota2",
            DisplayName = "Dota 2",
            ShortName = "Dota 2",
            IconGlyph = "🛡",
            SteamAppId = 570,
            HeaderTitle = "DOTA 2 PING MONITOR",
            SdrHeaderTitle = "DOTA 2 SDR",
            SdrHeaderBadge = "RELAY",
            Subtitle = "Телеметрия сети и Steam Datagram Relay",
            SubtitleEn = "Network telemetry & Steam Datagram Relay",
            DefaultServerName = "Россия — Stockholm (Valve)",
            LogoColorHex = "#59D499",
            DefaultRouteName = "Прямой (Valve SDR)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Россия — Stockholm (Valve)", "162.254.198.41"),
                new("Россия — Stockholm 2 (Valve)", "185.25.182.1"),
                new("EU West — Frankfurt (Valve)", "155.133.226.68"),
                new("EU East — Vienna (Valve)", "146.66.155.1"),
                new("Poland — Warsaw (Valve)", "155.133.230.98"),
                new("US East — Virginia (Valve)", "208.78.164.1"),
                new("US West — Seattle (Valve)", "205.196.6.135"),
                new("SE Asia — Singapore", "103.10.124.116"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "162.254.198.41", "162.254.198.42", "162.254.198.43" } },
                new() { Code = "sto2", Desc = "Stockholm 2 (Sweden)", Relays = new() { "155.133.252.50", "155.133.252.51" } },
                new() { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98", "155.133.230.99", "155.133.230.100" } },
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "155.133.226.68", "155.133.226.70", "162.254.197.36" } },
                new() { Code = "vie", Desc = "Vienna (Austria)", Relays = new() { "155.133.242.4", "155.133.242.5" } },
                new() { Code = "ams", Desc = "Amsterdam (Netherlands)", Relays = new() { "155.133.248.50", "155.133.248.51" } },
                new() { Code = "lhr", Desc = "London (UK)", Relays = new() { "162.254.196.82", "162.254.196.83" } },
                new() { Code = "par", Desc = "Paris (France)", Relays = new() { "162.254.199.178", "162.254.199.179" } },
                new() { Code = "mad", Desc = "Madrid (Spain)", Relays = new() { "155.133.246.39", "155.133.246.40" } },
                new() { Code = "dxb", Desc = "Dubai (UAE)", Relays = new() { "185.25.183.65", "185.25.183.66" } },
                new() { Code = "sgp", Desc = "Singapore", Relays = new() { "103.10.124.116", "103.10.124.118" } },
                new() { Code = "tyo", Desc = "Tokyo (Japan)", Relays = new() { "45.121.184.24", "45.121.184.26" } },
                new() { Code = "seo", Desc = "Seoul (Korea)", Relays = new() { "155.133.234.98" } },
                new() { Code = "hkg", Desc = "Hong Kong", Relays = new() { "155.133.244.18" } },
                new() { Code = "syd", Desc = "Sydney (Australia)", Relays = new() { "103.10.125.146" } },
                new() { Code = "iad", Desc = "Washington (US East)", Relays = new() { "162.254.192.82" } },
                new() { Code = "ord", Desc = "Chicago (US Central)", Relays = new() { "162.254.193.7" } },
                new() { Code = "lax", Desc = "Los Angeles (US West)", Relays = new() { "162.254.194.42" } },
                new() { Code = "sea", Desc = "Seattle (US Northwest)", Relays = new() { "205.196.6.135", "205.196.6.149" } }
            }
        };

        // ==========================================
        // 2. COUNTER-STRIKE 2 (CS2) (AppID: 730)
        // ==========================================
        var cs2 = new GameProfile
        {
            Id = "cs2",
            DisplayName = "Counter-Strike 2",
            ShortName = "CS2",
            IconGlyph = "🎯",
            SteamAppId = 730,
            HeaderTitle = "CS2 PING MONITOR",
            SdrHeaderTitle = "CS2 SDR",
            SdrHeaderBadge = "RELAY",
            Subtitle = "Матчмейкинг CS2 и Steam Datagram Relay",
            SubtitleEn = "CS2 matchmaking & Steam Datagram Relay",
            DefaultServerName = "CS2: Stockholm (Sweden)",
            LogoColorHex = "#FFC533",
            DefaultRouteName = "Прямой (CS2 SDR)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("CS2: Stockholm (Sweden)", "162.254.198.41"),
                new("CS2: Stockholm 2 - Bromma", "155.133.252.37"),
                new("CS2: Poland — Warsaw", "155.133.230.98"),
                new("CS2: Germany — Frankfurt", "155.133.226.68"),
                new("CS2: Austria — Vienna", "146.66.155.66"),
                new("CS2: Finland — Helsinki", "155.133.252.50"),
                new("CS2: Spain — Madrid", "155.133.246.34"),
                new("CS2: UK — London", "162.254.196.66"),
                new("CS2: Netherlands — Amsterdam", "155.133.248.36"),
                new("CS2: France — Paris", "185.25.182.18"),
                new("CS2: US East — Virginia", "162.254.192.88"),
                new("CS2: US Central — Chicago", "162.254.193.71"),
                new("CS2: US West — Los Angeles", "162.254.195.52"),
                new("CS2: Asia — Singapore", "103.10.124.116"),
                new("CS2: Japan — Tokyo", "45.121.184.5"),
                new("CS2: UAE — Dubai", "185.25.183.163"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "162.254.198.41", "162.254.198.42", "162.254.198.43" } },
                new() { Code = "sto2", Desc = "Stockholm 2 (Sweden)", Relays = new() { "155.133.252.37", "155.133.252.38" } },
                new() { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98", "155.133.230.99", "155.133.230.100" } },
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "155.133.226.68", "155.133.226.70", "162.254.197.36" } },
                new() { Code = "vie", Desc = "Vienna (Austria)", Relays = new() { "146.66.155.66", "146.66.155.67" } },
                new() { Code = "mad", Desc = "Madrid (Spain)", Relays = new() { "155.133.246.34", "155.133.246.39" } },
                new() { Code = "hel", Desc = "Helsinki (Finland)", Relays = new() { "155.133.252.50", "95.217.163.14" } },
                new() { Code = "ams", Desc = "Amsterdam (Netherlands)", Relays = new() { "155.133.248.36", "155.133.248.37" } },
                new() { Code = "lhr", Desc = "London (England)", Relays = new() { "162.254.196.66", "162.254.196.70" } },
                new() { Code = "par", Desc = "Paris (France)", Relays = new() { "185.25.182.18", "185.25.182.19" } },
                new() { Code = "iad", Desc = "Washington (US East)", Relays = new() { "162.254.192.88", "162.254.192.89" } },
                new() { Code = "ord", Desc = "Chicago (US Central)", Relays = new() { "162.254.193.71", "162.254.193.73" } },
                new() { Code = "lax", Desc = "Los Angeles (US West)", Relays = new() { "162.254.195.52", "162.254.195.70" } },
                new() { Code = "sea", Desc = "Seattle (US Northwest)", Relays = new() { "205.196.6.135", "205.196.6.149" } },
                new() { Code = "dxb", Desc = "Dubai (UAE)", Relays = new() { "185.25.183.163", "185.25.183.179" } },
                new() { Code = "sgp", Desc = "Singapore", Relays = new() { "103.10.124.116", "103.10.124.117" } },
                new() { Code = "tyo", Desc = "Tokyo (Japan)", Relays = new() { "45.121.184.5", "45.121.184.24" } },
                new() { Code = "syd", Desc = "Sydney (Australia)", Relays = new() { "103.10.125.20", "103.10.125.40" } },
                new() { Code = "gru", Desc = "São Paulo (Brazil)", Relays = new() { "155.133.227.35", "155.133.227.40" } },
                new() { Code = "eze", Desc = "Buenos Aires (Argentina)", Relays = new() { "155.133.255.98", "155.133.255.162" } }
            }
        };

        // ==========================================
        // 3. TEAM FORTRESS 2 (TF2) (AppID: 440)
        // ==========================================
        var tf2 = new GameProfile
        {
            Id = "tf2",
            DisplayName = "Team Fortress 2",
            ShortName = "TF2",
            IconGlyph = "⊕",
            SteamAppId = 440,
            HeaderTitle = "TF2 PING MONITOR",
            SdrHeaderTitle = "TF2 SDR",
            SdrHeaderBadge = "RELAY",
            Subtitle = "Матчмейкинг TF2 и Steam Datagram Relay",
            SubtitleEn = "TF2 matchmaking & Steam Datagram Relay",
            DefaultServerName = "TF2: Europe — Stockholm (Valve)",
            LogoColorHex = "#FF8800",
            DefaultRouteName = "Прямой (TF2 SDR)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("TF2: Europe — Stockholm (Valve)", "162.254.198.41"),
                new("TF2: Europe — Frankfurt (Valve)", "155.133.226.68"),
                new("TF2: Europe — Warsaw (Valve)", "155.133.230.98"),
                new("TF2: Europe — Luxembourg (Valve)", "146.66.152.1"),
                new("TF2: Europe — London (Valve)", "162.254.196.66"),
                new("TF2: Europe — Madrid (Valve)", "155.133.246.34"),
                new("TF2: US East — Virginia (Valve)", "162.254.192.88"),
                new("TF2: US Central — Chicago (Valve)", "162.254.193.71"),
                new("TF2: US West — Washington (Valve)", "205.196.6.135"),
                new("TF2: US West — Los Angeles (Valve)", "162.254.195.52"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "162.254.198.41", "162.254.198.42" } },
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "155.133.226.68", "155.133.226.70" } },
                new() { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98", "155.133.230.99" } },
                new() { Code = "lux", Desc = "Luxembourg (Luxembourg)", Relays = new() { "146.66.152.1", "155.133.226.68" } },
                new() { Code = "lhr", Desc = "London (England)", Relays = new() { "162.254.196.66", "162.254.196.70" } },
                new() { Code = "mad", Desc = "Madrid (Spain)", Relays = new() { "155.133.246.34", "155.133.246.39" } },
                new() { Code = "iad", Desc = "Virginia (US East)", Relays = new() { "162.254.192.88", "162.254.192.89" } },
                new() { Code = "ord", Desc = "Chicago (US Central)", Relays = new() { "162.254.193.71", "162.254.193.73" } },
                new() { Code = "eat", Desc = "Washington (US West)", Relays = new() { "205.196.6.135", "205.196.6.149" } },
                new() { Code = "lax", Desc = "Los Angeles (US West)", Relays = new() { "162.254.195.52", "162.254.195.70" } }
            }
        };

        // ==========================================
        // 4. FORTNITE (Epic Games / AWS Cloud)
        // ==========================================
        var fortnite = new GameProfile
        {
            Id = "fortnite",
            DisplayName = "Fortnite",
            ShortName = "Fortnite",
            IconGlyph = "Ⓕ",
            SteamAppId = 0,
            HeaderTitle = "FORTNITE PING MONITOR",
            SdrHeaderTitle = "AWS REGIONS",
            SdrHeaderBadge = "EDGE SERVERS",
            Subtitle = "Облачная сеть Epic Games и AWS Global Edge",
            SubtitleEn = "Epic Games cloud network & AWS Global Edge",
            DefaultServerName = "Fortnite: eu-north-1 (Stockholm)",
            LogoColorHex = "#9D65FF",
            DefaultRouteName = "Прямой (AWS Edge)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Fortnite: eu-north-1 (Stockholm)", "35.71.98.1"),
                new("Fortnite: eu-central-1 (Frankfurt)", "35.71.105.104"),
                new("Fortnite: eu-west-1 (Ireland)", "35.71.74.116"),
                new("Fortnite: eu-west-2 (London)", "35.71.111.1"),
                new("Fortnite: eu-south-1 (Milan)", "35.71.113.1"),
                new("Fortnite: me-south-1 (Bahrain)", "13.248.66.6"),
                new("Fortnite: us-east-1 (N. Virginia)", "3.218.180.106"),
                new("Fortnite: us-east-2 (Ohio)", "35.71.102.103"),
                new("Fortnite: us-west-2 (Oregon)", "35.71.65.129"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "eu-north-1", Desc = "Stockholm (Sweden)", Relays = new() { "35.71.98.1", "35.71.98.102" } },
                new() { Code = "eu-central-1", Desc = "Frankfurt (Germany)", Relays = new() { "35.71.105.104", "35.71.105.106" } },
                new() { Code = "eu-west-1", Desc = "Ireland", Relays = new() { "35.71.74.116", "35.71.75.120" } },
                new() { Code = "eu-west-2", Desc = "London (UK)", Relays = new() { "35.71.111.1", "35.71.111.128" } },
                new() { Code = "eu-south-1", Desc = "Milan (Italy)", Relays = new() { "35.71.113.1", "35.71.113.128" } },
                new() { Code = "me-south-1", Desc = "Bahrain (Middle East)", Relays = new() { "13.248.66.6", "3.5.50.14" } },
                new() { Code = "us-east-1", Desc = "N. Virginia (US East)", Relays = new() { "3.218.180.106", "3.218.181.213" } },
                new() { Code = "us-east-2", Desc = "Ohio (US Central)", Relays = new() { "35.71.102.103", "35.71.102.14" } },
                new() { Code = "us-west-2", Desc = "Oregon (US West)", Relays = new() { "35.71.65.129", "35.71.64.117" } }
            }
        };

        // ==========================================
        // 5. МИР ТАНКОВ (Lesta Games / RU)
        // ==========================================
        var tanksRu = new GameProfile
        {
            Id = "tanks_ru",
            DisplayName = "Мир танков",
            ShortName = "Мир танков",
            IconGlyph = "★",
            SteamAppId = 0,
            HeaderTitle = "МИР ТАНКОВ PING MONITOR",
            SdrHeaderTitle = "LESTA GAMES",
            SdrHeaderBadge = "CLUSTERS",
            Subtitle = "Официальные игровые серверы Lesta Games (RU)",
            SubtitleEn = "Official Lesta Games server clusters (RU)",
            DefaultServerName = "Мир танков: RU1 (Москва)",
            LogoColorHex = "#FF5722",
            DefaultRouteName = "Прямой (Lesta Direct)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Мир танков: RU1 (Москва)", "92.223.6.32"),
                new("Мир танков: RU2 (Москва)", "92.223.5.17"),
                new("Мир танков: RU6 (Москва)", "92.223.5.42"),
                new("Мир танков: RU12 (Москва)", "92.223.4.1"),
                new("Мир танков: RU4 (Красноярск)", "92.223.38.16"),
                new("Мир танков: RU8 (Екатеринбург)", "92.223.14.32"),
                new("Мир танков: RU9 (Хабаровск)", "92.223.36.32"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "ru1", Desc = "Москва RU1 (Lesta)", Relays = new() { "92.223.6.32", "92.223.6.33" } },
                new() { Code = "ru2", Desc = "Москва RU2 (Lesta)", Relays = new() { "92.223.5.17", "92.223.5.18" } },
                new() { Code = "ru6", Desc = "Москва RU6 (Lesta)", Relays = new() { "92.223.5.42", "92.223.5.43" } },
                new() { Code = "ru12", Desc = "Москва RU12 (Lesta)", Relays = new() { "92.223.4.1" } },
                new() { Code = "ru4", Desc = "Красноярск RU4 (Lesta)", Relays = new() { "92.223.38.16", "92.223.38.17" } },
                new() { Code = "ru8", Desc = "Екатеринбург RU8 (Lesta)", Relays = new() { "92.223.14.32", "92.223.14.33" } },
                new() { Code = "ru9", Desc = "Хабаровск RU9 (Lesta)", Relays = new() { "92.223.36.32", "92.223.36.33" } },
            }
        };

        // ==========================================
        // 6. WORLD OF TANKS (Wargaming / EU & NA)
        // ==========================================
        var wotEu = new GameProfile
        {
            Id = "wot_eu",
            DisplayName = "World of Tanks",
            ShortName = "WoT (EU/NA)",
            IconGlyph = "🎖",
            SteamAppId = 0,
            HeaderTitle = "WORLD OF TANKS PING MONITOR",
            SdrHeaderTitle = "WARGAMING",
            SdrHeaderBadge = "CLUSTERS",
            Subtitle = "Кластеры Wargaming Europe & NA",
            SubtitleEn = "Wargaming Europe & NA game clusters",
            DefaultServerName = "WoT: EU1 (Frankfurt)",
            LogoColorHex = "#4ADE80",
            DefaultRouteName = "Прямой (WG Direct)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("WoT: EU1 (Frankfurt)", "185.12.240.40"),
                new("WoT: EU2 (Frankfurt)", "92.223.20.215"),
                new("WoT: EU3 (Warsaw)", "92.223.54.116"),
                new("WoT: EU4 (Warsaw)", "45.82.31.74"),
                new("WoT: NA Central (Chicago)", "92.223.56.1"),
                new("WoT: NA East (Virginia)", "3.218.180.106"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "eu1", Desc = "Frankfurt EU1 (WG)", Relays = new() { "185.12.240.40" } },
                new() { Code = "eu2", Desc = "Frankfurt EU2 (WG)", Relays = new() { "92.223.20.215" } },
                new() { Code = "eu3", Desc = "Warsaw EU3 (WG)", Relays = new() { "92.223.54.116" } },
                new() { Code = "eu4", Desc = "Warsaw EU4 (WG)", Relays = new() { "45.82.31.74" } },
                new() { Code = "na_c", Desc = "NA Central - Chicago (WG)", Relays = new() { "92.223.56.1" } },
                new() { Code = "na_e", Desc = "NA East - Virginia (WG)", Relays = new() { "3.218.180.106" } },
            }
        };

        // ==========================================
        // 7. DEADLOCK (Valve SDR / AppID: 1422450)
        // ==========================================
        var deadlock = new GameProfile
        {
            Id = "deadlock",
            DisplayName = "Deadlock",
            ShortName = "Deadlock",
            IconGlyph = "⚡",
            SteamAppId = 1422450,
            HeaderTitle = "DEADLOCK PING MONITOR",
            SdrHeaderTitle = "DEADLOCK SDR",
            SdrHeaderBadge = "RELAY",
            Subtitle = "Матчмейкинг Deadlock и Steam Datagram Relay",
            SubtitleEn = "Deadlock matchmaking & Steam Datagram Relay",
            DefaultServerName = "Deadlock: Europe — Stockholm (Valve)",
            LogoColorHex = "#57C1FF",
            DefaultRouteName = "Прямой (Valve SDR)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Deadlock: Europe — Stockholm (Valve)", "162.254.198.41"),
                new("Deadlock: Europe — Warsaw (Valve)", "155.133.230.98"),
                new("Deadlock: Europe — Frankfurt (Valve)", "155.133.226.68"),
                new("Deadlock: Europe — Amsterdam (Valve)", "155.133.248.36"),
                new("Deadlock: Europe — London (Valve)", "162.254.196.66"),
                new("Deadlock: Europe — Vienna (Valve)", "146.66.155.66"),
                new("Deadlock: US East — Virginia (Valve)", "162.254.192.88"),
                new("Deadlock: US Central — Chicago (Valve)", "162.254.193.71"),
                new("Deadlock: US West — Los Angeles (Valve)", "162.254.195.52"),
                new("Deadlock: US West — Seattle (Valve)", "205.196.6.135"),
                new("Deadlock: Asia — Singapore (Valve)", "103.10.124.116"),
                new("Deadlock: Japan — Tokyo (Valve)", "45.121.184.5"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "162.254.198.41", "162.254.198.42" } },
                new() { Code = "sto2", Desc = "Stockholm 2 (Sweden)", Relays = new() { "155.133.252.37", "155.133.252.38" } },
                new() { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98", "155.133.230.99" } },
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "155.133.226.68", "155.133.226.70" } },
                new() { Code = "ams", Desc = "Amsterdam (Netherlands)", Relays = new() { "155.133.248.36", "155.133.248.37" } },
                new() { Code = "lhr", Desc = "London (England)", Relays = new() { "162.254.196.66", "162.254.196.70" } },
                new() { Code = "vie", Desc = "Vienna (Austria)", Relays = new() { "146.66.155.66", "146.66.155.67" } },
                new() { Code = "iad", Desc = "Virginia (US East)", Relays = new() { "162.254.192.88", "162.254.192.89" } },
                new() { Code = "ord", Desc = "Chicago (US Central)", Relays = new() { "162.254.193.71", "162.254.193.73" } },
                new() { Code = "lax", Desc = "Los Angeles (US West)", Relays = new() { "162.254.195.52", "162.254.195.70" } },
                new() { Code = "sea", Desc = "Seattle (US Northwest)", Relays = new() { "205.196.6.135", "205.196.6.149" } },
                new() { Code = "sgp", Desc = "Singapore", Relays = new() { "103.10.124.116", "103.10.124.117" } },
                new() { Code = "tyo", Desc = "Tokyo (Japan)", Relays = new() { "45.121.184.5", "45.121.184.24" } },
            }
        };

        // ==========================================
        // 8. APEX LEGENDS (EA / Respawn)
        // ==========================================
        var apex = new GameProfile
        {
            Id = "apex",
            DisplayName = "Apex Legends",
            ShortName = "Apex",
            IconGlyph = "🔺",
            SteamAppId = 0,
            HeaderTitle = "APEX LEGENDS PING MONITOR",
            SdrHeaderTitle = "APEX EA",
            SdrHeaderBadge = "DATA CENTERS",
            Subtitle = "Дата-центры EA Multiplay & Respawn",
            SubtitleEn = "EA Multiplay & Respawn data centers",
            DefaultServerName = "Apex: Frankfurt 1",
            LogoColorHex = "#FF4655",
            DefaultRouteName = "Прямой (EA Edge)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Apex: Frankfurt 1", "35.71.105.104"),
                new("Apex: Frankfurt 2", "35.71.105.130"),
                new("Apex: London", "35.71.111.1"),
                new("Apex: Amsterdam", "155.133.248.36"),
                new("Apex: Belgium", "193.190.198.27"),
                new("Apex: Bahrain", "13.248.66.10"),
                new("Apex: Tokyo", "35.71.114.129"),
                new("Apex: Virginia (US East)", "3.218.180.106"),
                new("Apex: Oregon (US West)", "35.71.65.121"),
                new("Apex: Iowa (US Central)", "35.71.102.18"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "fra1", Desc = "Frankfurt 1 (Germany)", Relays = new() { "35.71.105.104" } },
                new() { Code = "fra2", Desc = "Frankfurt 2 (Germany)", Relays = new() { "35.71.105.130" } },
                new() { Code = "lhr", Desc = "London (UK)", Relays = new() { "35.71.111.1" } },
                new() { Code = "ams", Desc = "Amsterdam (Netherlands)", Relays = new() { "155.133.248.36" } },
                new() { Code = "bel", Desc = "Belgium", Relays = new() { "193.190.198.27" } },
                new() { Code = "bhr", Desc = "Bahrain (Middle East)", Relays = new() { "13.248.66.10" } },
                new() { Code = "tyo", Desc = "Tokyo (Japan)", Relays = new() { "35.71.114.129" } },
                new() { Code = "iad", Desc = "Virginia (US East)", Relays = new() { "3.218.180.106" } },
                new() { Code = "pdx", Desc = "Oregon (US West)", Relays = new() { "35.71.65.121" } },
                new() { Code = "dsm", Desc = "Iowa (US Central)", Relays = new() { "35.71.102.18" } },
            }
        };

        // ==========================================
        // 9. VALORANT (Riot Games / Riot Direct)
        // ==========================================
        var valorant = new GameProfile
        {
            Id = "valorant",
            DisplayName = "Valorant",
            ShortName = "Valorant",
            IconGlyph = "⚔",
            SteamAppId = 0,
            HeaderTitle = "VALORANT PING MONITOR",
            SdrHeaderTitle = "RIOT DIRECT",
            SdrHeaderBadge = "REGIONS",
            Subtitle = "Глобальная сеть маршрутизации Riot Direct",
            SubtitleEn = "Riot Direct global routing network",
            DefaultServerName = "Valorant: Frankfurt (Riot Direct)",
            LogoColorHex = "#FA4454",
            DefaultRouteName = "Прямой (Riot Direct)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Valorant: Frankfurt (Riot Direct)", "35.71.105.104"),
                new("Valorant: Stockholm (Riot Direct)", "35.71.98.130"),
                new("Valorant: London (Riot Direct)", "35.71.111.1"),
                new("Valorant: Warsaw (Riot Direct)", "155.133.230.98"),
                new("Valorant: Paris (Riot Direct)", "35.71.101.129"),
                new("Valorant: Istanbul (Riot Direct)", "185.169.54.1"),
                new("Valorant: Madrid (Riot Direct)", "35.71.120.107"),
                new("Valorant: Bahrain (Riot Direct)", "13.248.66.10"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "35.71.105.104" } },
                new() { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "35.71.98.130" } },
                new() { Code = "lhr", Desc = "London (UK)", Relays = new() { "35.71.111.1" } },
                new() { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98" } },
                new() { Code = "par", Desc = "Paris (France)", Relays = new() { "35.71.101.129" } },
                new() { Code = "ist", Desc = "Istanbul (Turkey)", Relays = new() { "185.169.54.1" } },
                new() { Code = "mad", Desc = "Madrid (Spain)", Relays = new() { "35.71.120.107" } },
                new() { Code = "bhr", Desc = "Bahrain (Middle East)", Relays = new() { "13.248.66.10" } },
            }
        };

        // ==========================================
        // 10. PUBG: BATTLEGROUNDS (Krafton)
        // ==========================================
        var pubg = new GameProfile
        {
            Id = "pubg",
            DisplayName = "PUBG: Battlegrounds",
            ShortName = "PUBG",
            IconGlyph = "🍳",
            SteamAppId = 0,
            HeaderTitle = "PUBG PING MONITOR",
            SdrHeaderTitle = "PUBG NODES",
            SdrHeaderBadge = "AWS/AZURE",
            Subtitle = "Облачные узлы AWS и Azure матчмейкинга PUBG",
            SubtitleEn = "PUBG matchmaking AWS & Azure cloud nodes",
            DefaultServerName = "PUBG: Europe — Frankfurt",
            LogoColorHex = "#FBBF24",
            DefaultRouteName = "Прямой (Krafton Edge)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("PUBG: Europe — Frankfurt", "35.71.105.104"),
                new("PUBG: Europe — London", "35.71.111.1"),
                new("PUBG: Europe — Ireland", "35.71.74.116"),
                new("PUBG: North America — Virginia", "3.218.180.106"),
                new("PUBG: Asia — Singapore", "35.71.118.12"),
                new("PUBG: Asia — Seoul", "35.71.109.104"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "35.71.105.104" } },
                new() { Code = "lhr", Desc = "London (UK)", Relays = new() { "35.71.111.1" } },
                new() { Code = "irl", Desc = "Ireland", Relays = new() { "35.71.74.116" } },
                new() { Code = "iad", Desc = "Virginia (US East)", Relays = new() { "3.218.180.106" } },
                new() { Code = "sgp", Desc = "Singapore", Relays = new() { "35.71.118.12" } },
                new() { Code = "icn", Desc = "Seoul (Korea)", Relays = new() { "35.71.109.104" } },
            }
        };

        // ==========================================
        // 11. RUST (Facepunch Studios)
        // ==========================================
        var rust = new GameProfile
        {
            Id = "rust",
            DisplayName = "Rust",
            ShortName = "Rust",
            IconGlyph = "☢",
            SteamAppId = 0,
            HeaderTitle = "RUST PING MONITOR",
            SdrHeaderTitle = "RUST HUBS",
            SdrHeaderBadge = "OFFICIAL",
            Subtitle = "Официальные выделенные серверы Facepunch Studios",
            SubtitleEn = "Facepunch Studios official dedicated servers",
            DefaultServerName = "Rust: EU Central (Frankfurt)",
            LogoColorHex = "#CE422B",
            DefaultRouteName = "Прямой (Rust Direct)",
            Targets = new List<PingTargetItemViewModel>
            {
                new("Google DNS (8.8.8.8)", "8.8.8.8"),
                new("Cloudflare DNS (1.1.1.1)", "1.1.1.1"),
                new("Rust: EU Central (Frankfurt)", "185.38.148.1"),
                new("Rust: EU North (Helsinki)", "65.21.0.1"),
                new("Rust: UK (London)", "185.38.149.1"),
                new("Rust: US East (Virginia)", "3.218.180.106"),
            },
            DefaultPops = new List<ValvePop>
            {
                new() { Code = "fra", Desc = "Frankfurt (EU Central)", Relays = new() { "185.38.148.1" } },
                new() { Code = "hel", Desc = "Helsinki (EU North)", Relays = new() { "65.21.0.1" } },
                new() { Code = "lhr", Desc = "London (UK)", Relays = new() { "185.38.149.1" } },
                new() { Code = "iad", Desc = "Virginia (US East)", Relays = new() { "3.218.180.106" } },
            }
        };

        _games[dota2.Id] = dota2;
        _games[cs2.Id] = cs2;
        _games[tf2.Id] = tf2;
        _games[fortnite.Id] = fortnite;
        _games[tanksRu.Id] = tanksRu;
        _games[wotEu.Id] = wotEu;
        _games[deadlock.Id] = deadlock;
        _games[apex.Id] = apex;
        _games[valorant.Id] = valorant;
        _games[pubg.Id] = pubg;
        _games[rust.Id] = rust;
    }
}
