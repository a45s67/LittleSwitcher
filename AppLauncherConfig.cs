using System.Text.Json;

namespace LittleSwitcher;

public class AppLauncherEntry
{
    public int VirtualDesktop { get; set; } = 1;
    public string AppName { get; set; } = "";
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public class AppLauncherConfig
{
    public List<AppLauncherEntry> Entries { get; set; } = new();

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LittleSwitcher", "launcher.json");

    public static AppLauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppLauncherConfig>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
