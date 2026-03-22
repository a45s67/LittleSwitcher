using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleSwitcher;

public class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    public HotkeyBinding() { }

    public HotkeyBinding(uint modifiers, uint key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & GlobalHotkey.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & GlobalHotkey.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & GlobalHotkey.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & GlobalHotkey.MOD_WIN) != 0) parts.Add("Win");

            var keyName = KeyToString(Key);
            if (!string.IsNullOrEmpty(keyName)) parts.Add(keyName);

            return string.Join(" + ", parts);
        }
    }

    public static string KeyToString(uint vk)
    {
        return vk switch
        {
            >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
            >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
            >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",  // F1-F24
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xBE => ".",
            0xBC => ",",
            0xBF => "/",
            0xBA => ";",
            0xDE => "'",
            0xBB => "=",
            0xBD => "-",
            0xC0 => "`",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => vk != 0 ? $"0x{vk:X2}" : ""
        };
    }
}

public class HotkeyConfig
{
    public uint Modifier { get; set; } = GlobalHotkey.MOD_ALT;
    public uint CycleWindowsKey { get; set; } = GlobalHotkey.VK_W;
    public uint FocusOtherMonitorKey { get; set; } = GlobalHotkey.VK_OEM_6;
    public uint LastDesktopKey { get; set; } = GlobalHotkey.VK_OEM_5;
    public uint ToggleManagementKey { get; set; } = GlobalHotkey.VK_A;
    public uint PinWindowKey { get; set; } = GlobalHotkey.VK_P;
    public uint ToggleTaskbarKey { get; set; } = GlobalHotkey.VK_T;
    public uint ToggleTitleBarKey { get; set; } = GlobalHotkey.VK_H;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LittleSwitcher", "hotkeys.json");

    public static HotkeyConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<HotkeyConfig>(json) ?? new HotkeyConfig();
            }
        }
        catch { }
        return new HotkeyConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
