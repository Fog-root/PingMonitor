using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Views;

public partial class NetworkTweaksWindow : Window
{
    private readonly GameBoostService _gameBoostService;
    private readonly NetworkTweaksService _networkTweaksService;

    public NetworkTweaksWindow(GameBoostService gameBoostService, NetworkTweaksService networkTweaksService)
    {
        InitializeComponent();
        _gameBoostService = gameBoostService;
        _networkTweaksService = networkTweaksService;

        ApplyLocalization();
        UpdateGameBoostButtonState();
    }

    private void ApplyLocalization()
    {
        bool isRu = LocalizationService.IsRussian;
        TitleText.Text = isRu ? "ОПТИМИЗАЦИЯ СЕТИ И WINDOWS" : "NETWORK & WINDOWS OPTIMIZATION";
        GbTitle.Text = isRu ? "Игровой режим (Game Boost)" : "Game Boost Mode";
        GbDesc.Text = isRu
            ? "Повышает приоритет процесса игры до High и режим I/O, снижая приоритет фоновых браузеров и загрузчиков."
            : "Sets game process priority to High and elevates I/O, reducing priority of background browsers and downloads.";
        FlushDnsTitle.Text = isRu ? "Очистка кэша DNS (Flush DNS)" : "Flush DNS Resolver Cache";
        FlushDnsDesc.Text = isRu
            ? "Сбрасывает устаревшие и зависшие DNS-записи в резолвере Windows."
            : "Clears stale and stuck DNS records in the Windows DNS resolver.";
        NagleTitle.Text = isRu ? "Оптимизация Nagle (TCP NoDelay)" : "Nagle Algorithm (TCP NoDelay)";
        NagleDesc.Text = isRu
            ? "Отключает склейку TCP пакетов (TcpAckFrequency = 1), минимизируя задержку передачи в онлайн-играх."
            : "Disables packet aggregation (TcpAckFrequency = 1), minimizing network latency in games.";
        AdapterTitle.Text = isRu ? "Перезапуск сетевого адаптера" : "Restart Network Adapter";
        AdapterDesc.Text = isRu
            ? "Мягко перезапускает сетевой стек при зависании Wi-Fi/Ethernet или потере пакетов."
            : "Gracefully restarts the active network adapter upon Wi-Fi/Ethernet freeze or packet loss.";

        FlushDnsBtn.Content = isRu ? "Очистить" : "Flush";
        NagleBtn.Content = isRu ? "Применить" : "Apply";
        RestartAdapterBtn.Content = isRu ? "Перезапуск" : "Restart";
    }

    private void UpdateGameBoostButtonState()
    {
        bool isRu = LocalizationService.IsRussian;
        if (_gameBoostService.IsBoostActive)
        {
            GameBoostBtn.Content = isRu ? "Отключить" : "Disable";
            GameBoostBtn.Foreground = Brushes.Tomato;
            GameBoostBtn.BorderBrush = Brushes.Tomato;
        }
        else
        {
            GameBoostBtn.Content = isRu ? "Включить" : "Enable";
            GameBoostBtn.SetResourceReference(ForegroundProperty, "AccentBlue");
            GameBoostBtn.SetResourceReference(BorderBrushProperty, "AccentBlue");
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void GameBoostBtn_Click(object sender, RoutedEventArgs e)
    {
        GameBoostBtn.IsEnabled = false;
        try
        {
            if (_gameBoostService.IsBoostActive)
            {
                var res = await _gameBoostService.DisableBoostAsync();
                StatusMessageText.Text = res.Message;
                StatusMessageText.SetResourceReference(ForegroundProperty, res.Success ? "AccentGreen" : "AccentRed");
            }
            else
            {
                var res = await _gameBoostService.EnableBoostAsync();
                StatusMessageText.Text = res.Message;
                StatusMessageText.SetResourceReference(ForegroundProperty, res.Success ? "AccentGreen" : "AccentRed");
            }
            UpdateGameBoostButtonState();
        }
        finally
        {
            GameBoostBtn.IsEnabled = true;
        }
    }

    private async void FlushDnsBtn_Click(object sender, RoutedEventArgs e)
    {
        FlushDnsBtn.IsEnabled = false;
        try
        {
            var res = await _networkTweaksService.FlushDnsAsync();
            StatusMessageText.Text = res.Message;
            StatusMessageText.SetResourceReference(ForegroundProperty, res.Success ? "AccentGreen" : "AccentRed");
        }
        finally
        {
            FlushDnsBtn.IsEnabled = true;
        }
    }

    private async void NagleBtn_Click(object sender, RoutedEventArgs e)
    {
        NagleBtn.IsEnabled = false;
        try
        {
            var res = await _networkTweaksService.SetNagleOptimizationAsync(true);
            StatusMessageText.Text = res.Message;
            StatusMessageText.SetResourceReference(ForegroundProperty, res.Success ? "AccentGreen" : "AccentRed");
        }
        finally
        {
            NagleBtn.IsEnabled = true;
        }
    }

    private async void RestartAdapterBtn_Click(object sender, RoutedEventArgs e)
    {
        RestartAdapterBtn.IsEnabled = false;
        try
        {
            var res = await _networkTweaksService.RestartNetworkAdapterAsync();
            StatusMessageText.Text = res.Message;
            StatusMessageText.SetResourceReference(ForegroundProperty, res.Success ? "AccentGreen" : "AccentRed");
        }
        finally
        {
            RestartAdapterBtn.IsEnabled = true;
        }
    }
}
