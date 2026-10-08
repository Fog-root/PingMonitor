using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotaPingMonitor.Services;
using Microsoft.Win32;

namespace DotaPingMonitor.Views;

public partial class IspTicketExportWindow : Window
{
    private readonly IspTicketData _data;
    private bool _isUpdatingText;

    public IspTicketExportWindow(IspTicketData data)
    {
        _data = data ?? new IspTicketData();
        InitializeComponent();
        ApplyLocalization();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateReportText();
    }

    private void ApplyLocalization()
    {
        bool isRu = LocalizationService.IsRussian;
        Title = isRu ? "Отчёт для техподдержки провайдера" : "ISP Support Ticket Export";
        TitleText.Text = isRu ? "СФОРМИРОВАТЬ ОТЧЁТ ДЛЯ ТЕХПОДДЕРЖКИ ПРОВАЙДЕРА" : "ISP TECHNICAL SUPPORT TICKET EXPORT";
        SubtitleText.Text = isRu 
            ? "Готовое обращение к оператору связи с поузловой MTR-трассировкой и логами сбоев" 
            : "Complete ISP support ticket with MTR hop-by-hop diagnostics and failure logs";

        LabelContract.Text = isRu ? "Номер договора / лицевой счёт:" : "Account / Contract ID:";
        LabelAddress.Text = isRu ? "Адрес подключения (город, улица):" : "Service Address:";
        LabelPhone.Text = isRu ? "Контактный телефон:" : "Contact Phone:";

        CopyTicketBtnText.Text = isRu ? "Скопировать обращение" : "Copy to Clipboard";
        SaveFileBtnText.Text = isRu ? "Сохранить в .txt" : "Save to .txt";
        CloseBtnText.Text = isRu ? "Закрыть" : "Close";
        StatusInfoText.Text = isRu ? "Готово к отправке в службу поддержки" : "Ready to submit to ISP support";
    }

    private void UpdateReportText()
    {
        if (_isUpdatingText) return;
        _isUpdatingText = true;

        _data.ContractNumber = ContractBox?.Text?.Trim() ?? string.Empty;
        _data.ClientAddress = AddressBox?.Text?.Trim() ?? string.Empty;
        _data.ClientPhone = PhoneBox?.Text?.Trim() ?? string.Empty;

        string text = IspTicketService.GenerateTicketText(_data, LocalizationService.IsRussian);
        if (ReportTextBox != null)
        {
            ReportTextBox.Text = text;
        }

        _isUpdatingText = false;
    }

    private void FieldBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateReportText();
    }

    private async void CopyTicketBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ReportTextBox.Text)) return;

        try
        {
            Clipboard.SetText(ReportTextBox.Text);

            string origText = CopyTicketBtnText.Text;
            CopyTicketBtnText.Text = LocalizationService.IsRussian ? "✓ Скопировано в буфер!" : "✓ Copied to Clipboard!";
            StatusInfoText.Text = LocalizationService.IsRussian 
                ? "Текст скопирован. Вставьте его в чат или веб-форму вашего провайдера." 
                : "Ticket copied. Paste it into your ISP chat or portal.";
            StatusInfoText.Foreground = (Brush)new BrushConverter().ConvertFromString("#59D499")!;

            await Task.Delay(2000);
            CopyTicketBtnText.Text = origText;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveFileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ReportTextBox.Text)) return;

        try
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = $"ISP_Report_{DateTime.Now:yyyyMMdd_HHmm}.txt",
                Title = LocalizationService.IsRussian ? "Сохранить отчёт для техподдержки" : "Save ISP Report"
            };

            if (sfd.ShowDialog(this) == true)
            {
                File.WriteAllText(sfd.FileName, ReportTextBox.Text, System.Text.Encoding.UTF8);
                StatusInfoText.Text = LocalizationService.IsRussian 
                    ? $"Отчёт успешно сохранён: {Path.GetFileName(sfd.FileName)}" 
                    : $"Report saved: {Path.GetFileName(sfd.FileName)}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
