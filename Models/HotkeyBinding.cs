using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace DotaPingMonitor.Models;

/// <summary>
/// Модель настраиваемой комбинации горячих клавиш.
/// Поддерживает модификаторы (Ctrl, Shift, Alt, Win) и виртуальные коды клавиш (VK).
/// </summary>
public class HotkeyBinding
{
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }
    public int KeyCode { get; set; }
    public string DisplayText { get; set; } = string.Empty;

    public HotkeyBinding()
    {
    }

    public HotkeyBinding(bool ctrl, bool shift, bool alt, bool win, int keyCode, string? displayText = null)
    {
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
        Win = win;
        KeyCode = keyCode;
        DisplayText = displayText ?? FormatDisplayText(ctrl, shift, alt, win, keyCode);
    }

    /// <summary>
    /// Проверяет, совпадает ли нажатая комбинация с текущим биндом.
    /// </summary>
    public bool Matches(int vkCode, bool ctrl, bool shift, bool alt, bool win)
    {
        if (KeyCode <= 0) return false;
        return KeyCode == vkCode &&
               Ctrl == ctrl &&
               Shift == shift &&
               Alt == alt &&
               Win == win;
    }

    public HotkeyBinding Clone()
    {
        return new HotkeyBinding(Ctrl, Shift, Alt, Win, KeyCode, DisplayText);
    }

    /// <summary>
    /// Формирует аккуратную строку для отображения комбинации в интерфейсе (например, "Ctrl+Shift+O" или "...").
    /// </summary>
    public static string FormatDisplayText(bool ctrl, bool shift, bool alt, bool win, int vkCode)
    {
        if (vkCode <= 0) return "...";

        var parts = new List<string>();

        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");

        string keyName = GetKeyName(vkCode);
        if (!string.IsNullOrEmpty(keyName))
        {
            parts.Add(keyName);
        }

        return parts.Count > 0 ? string.Join("+", parts) : "Не задано";
    }

    /// <summary>
    /// Преобразует Virtual Key (VK) код в понятное имя клавиши.
    /// </summary>
    public static string GetKeyName(int vkCode)
    {
        // 1. Буквы A-Z
        if (vkCode >= 0x41 && vkCode <= 0x5A)
        {
            return ((char)vkCode).ToString();
        }

        // 2. Цифры 0-9
        if (vkCode >= 0x30 && vkCode <= 0x39)
        {
            return ((char)vkCode).ToString();
        }

        // 3. Функциональные клавиши F1-F24
        if (vkCode >= 0x70 && vkCode <= 0x7B)
        {
            return $"F{vkCode - 0x70 + 1}";
        }
        if (vkCode >= 0x7C && vkCode <= 0x87)
        {
            return $"F{vkCode - 0x7C + 13}";
        }

        // 4. NumPad
        if (vkCode >= 0x60 && vkCode <= 0x69)
        {
            return $"Num{vkCode - 0x60}";
        }

        switch (vkCode)
        {
            case 0x6A: return "Num*";
            case 0x6B: return "Num+";
            case 0x6D: return "Num-";
            case 0x6E: return "Num.";
            case 0x6F: return "Num/";

            case 0x20: return "Space";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x08: return "Backspace";
            case 0x1B: return "Esc";

            case 0x2D: return "Ins";
            case 0x2E: return "Del";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x21: return "PgUp";
            case 0x22: return "PgDn";

            case 0x26: return "Up";
            case 0x28: return "Down";
            case 0x25: return "Left";
            case 0x27: return "Right";

            case 0xC0: return "~";
            case 0xBD: return "-";
            case 0xBB: return "=";
            case 0xDB: return "[";
            case 0xDD: return "]";
            case 0xBA: return ";";
            case 0xDE: return "'";
            case 0xBC: return ",";
            case 0xBE: return ".";
            case 0xBF: return "/";
            case 0xDC: return "\\";

            case 0x2C: return "PrtScn";
            case 0x91: return "ScrollLock";
            case 0x13: return "Pause";
            case 0x14: return "CapsLock";

            default:
                try
                {
                    var k = KeyInterop.KeyFromVirtualKey(vkCode);
                    if (k != Key.None) return k.ToString();
                }
                catch
                {
                }
                return $"0x{vkCode:X2}";
        }
    }
}
