using System.Net;
using System.Windows;
using System.Windows.Input;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Views;

public partial class AddCustomTargetDialog : Window
{
    public string ResultName { get; private set; } = string.Empty;
    public string ResultHost { get; private set; } = string.Empty;

    public AddCustomTargetDialog()
    {
        InitializeComponent();
        ApplyLocalization();

        Loaded += (_, _) =>
        {
            TargetNameBox.Focus();
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                SaveButton_Click(s, e);
            }
        };
    }

    private void ApplyLocalization()
    {
        bool isRu = LocalizationService.IsRussian;
        TitleText.Text = isRu ? "ДОБАВИТЬ СВОЙ СЕРВЕР" : "ADD CUSTOM SERVER";
        LabelName.Text = isRu ? "Название сервера или метка:" : "Server Name or Label:";
        LabelHost.Text = isRu ? "IP-адрес или домен (IPv4 / Hostname):" : "IP Address or Domain (IPv4 / Hostname):";
        CancelButton.Content = isRu ? "Отмена" : "Cancel";
        SaveButton.Content = isRu ? "Сохранить" : "Save";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name = TargetNameBox.Text.Trim();
        string host = TargetHostBox.Text.Trim();

        bool isRu = LocalizationService.IsRussian;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError(isRu ? "Введите название сервера" : "Please enter server name");
            TargetNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            ShowError(isRu ? "Введите IP-адрес или домен" : "Please enter IP address or domain");
            TargetHostBox.Focus();
            return;
        }

        // Простая проверка формата хоста
        if (!IPAddress.TryParse(host, out _) && !Uri.CheckHostName(host).Equals(UriHostNameType.Dns))
        {
            ShowError(isRu ? "Некорректный IP-адрес или адрес узла" : "Invalid IP address or host name");
            TargetHostBox.Focus();
            return;
        }

        ResultName = name;
        ResultHost = host;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
