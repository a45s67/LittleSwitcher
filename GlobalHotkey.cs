using System.Runtime.InteropServices;

namespace LittleSwitcher;

public class GlobalHotkey
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;
    
    // Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    // Virtual Key Codes
    public const uint VK_A = 0x41;
    public const uint VK_W = 0x57;
    public const uint VK_1 = 0x31;
    public const uint VK_2 = 0x32;
    public const uint VK_3 = 0x33;
    public const uint VK_4 = 0x34;
    public const uint VK_5 = 0x35;
    public const uint VK_6 = 0x36;
    public const uint VK_7 = 0x37;
    public const uint VK_8 = 0x38;
    public const uint VK_9 = 0x39;
    public const uint VK_OEM_4 = 0xDB; // [ key
    public const uint VK_OEM_5 = 0xDC; // \ key
    public const uint VK_OEM_6 = 0xDD; // ] key

    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private int _nextHotkeyId = 1;

    public GlobalHotkey(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public int RegisterHotkey(uint modifiers, uint virtualKey, Action action)
    {
        var hotkeyId = _nextHotkeyId++;
        
        if (RegisterHotKey(_windowHandle, hotkeyId, modifiers, virtualKey))
        {
            _hotkeyActions[hotkeyId] = action;
            return hotkeyId;
        }

        return -1; // Registration failed
    }

    public void UnregisterHotkey(int hotkeyId)
    {
        UnregisterHotKey(_windowHandle, hotkeyId);
        _hotkeyActions.Remove(hotkeyId);
    }

    public void UnregisterAll()
    {
        foreach (var hotkeyId in _hotkeyActions.Keys.ToList())
        {
            UnregisterHotkey(hotkeyId);
        }
    }

    public bool HandleHotkeyMessage(int hotkeyId)
    {
        if (_hotkeyActions.TryGetValue(hotkeyId, out var action))
        {
            action.Invoke();
            return true;
        }
        return false;
    }
}