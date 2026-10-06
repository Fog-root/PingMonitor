using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Views;

public partial class HotkeyRebindDialog : Window
{
    private readonly HotkeyBinding _originalBinding;
    private readonly HotkeyBinding _defaultBinding;
    private HotkeyBinding _currentBinding;

    public HotkeyBinding ResultBinding => _currentBinding;

    public HotkeyRebindDialog(string actionTitle, HotkeyBinding currentBinding, HotkeyBinding defaultBinding)
    {
        InitializeComponent();

        _originalBinding = currentBinding.Clone();
        _defaultBinding = defaultBinding.Clone();
        _currentBinding = currentBinding.Clone();

        ActionNameText.Text = actionTitle;
        UpdateDisplay(_currentBinding);

        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(this);
        };
    }

    private void UpdateDisplay(HotkeyBinding binding)
    {
        if (binding.KeyCode <= 0 || binding.DisplayText == "...")
        {
            KeyCombinationText.Text = "...";
            KeyBadgeBorder.BorderBrush = (SolidColorBrush)FindResource("DlgBorder");
            KeyCombinationText.Foreground = (SolidColorBrush)FindResource("DlgTextMuted");
            StatusHintText.Text = "Горячая клавиша отключена. Нажмите «Применить» или нажмите новую комбинацию...";
            return;
        }

        KeyCombinationText.Text = binding.DisplayText;
        KeyBadgeBorder.BorderBrush = (SolidColorBrush)FindResource("DlgAccentGreen");
        KeyCombinationText.Foreground = (SolidColorBrush)FindResource("DlgAccentGreen");
        StatusHintText.Text = "Нажмите новую комбинацию клавиш для изменения...";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // 1. Esc = Отмена
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        // 2. Enter = Применить
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
            return;
        }

        // 3. Backspace = Сброс на дефолт
        if (e.Key == Key.Back)
        {
            _currentBinding = _defaultBinding.Clone();
            UpdateDisplay(_currentBinding);
            return;
        }

        // 4. Delete = Очистить бинд (...)
        if (e.Key == Key.Delete)
        {
            _currentBinding = new HotkeyBinding(false, false, false, false, 0, "...");
            UpdateDisplay(_currentBinding);
            return;
        }

        // 5. Определение клавиши
        Key realKey = e.Key == Key.System ? e.SystemKey : e.Key;

        // Если нажат только сам модификатор — отображаем промежуточное состояние
        if (IsModifierKey(realKey))
        {
            bool ctrlOnly = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shiftOnly = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool altOnly = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool winOnly = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

            var parts = new List<string>();
            if (ctrlOnly) parts.Add("Ctrl");
            if (shiftOnly) parts.Add("Shift");
            if (altOnly) parts.Add("Alt");
            if (winOnly) parts.Add("Win");
            parts.Add("...");

            KeyCombinationText.Text = string.Join("+", parts);
            KeyBadgeBorder.BorderBrush = (SolidColorBrush)FindResource("DlgAccentBlue");
            KeyCombinationText.Foreground = (SolidColorBrush)FindResource("DlgAccentBlue");
            StatusHintText.Text = "Удерживайте модификатор и нажмите основную клавишу...";
            return;
        }

        // 6. Полноценная клавиша
        int vkCode = KeyInterop.VirtualKeyFromKey(realKey);
        if (vkCode <= 0) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        _currentBinding = new HotkeyBinding(ctrl, shift, alt, win, vkCode);
        UpdateDisplay(_currentBinding);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
                   or Key.LeftShift or Key.RightShift
                   or Key.LeftAlt or Key.RightAlt
                   or Key.LWin or Key.RWin;
    }

    private void ResetDefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentBinding = _defaultBinding.Clone();
        UpdateDisplay(_currentBinding);
    }

    private void ClearBindingBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentBinding = new HotkeyBinding(false, false, false, false, 0, "...");
        UpdateDisplay(_currentBinding);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
