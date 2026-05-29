using System.Text.Json;
using System.Text.RegularExpressions;

namespace LittleSwitcher;

public class WindowFilterConfig
{
    public List<string> IncludeTitleRegex { get; set; } = [];
    public List<string> IncludeClassRegex { get; set; } = [];
    public List<string> IncludeProcessRegex { get; set; } = [];

    public List<string> ExcludeTitleRegex { get; set; } =
    [
        "^Program Manager$"
    ];

    public List<string> ExcludeClassRegex { get; set; } =
    [
        "^Shell_TrayWnd$",
        "^Shell_SecondaryTrayWnd$",
        "^Progman$",
        "^WorkerW$",
        "^NotifyIconOverflowWindow$",
        "^Windows.UI.Core.CoreWindow$"
    ];

    public List<string> ExcludeProcessRegex { get; set; } =
    [
        "^LittleSwitcher$"
    ];

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LittleSwitcher", "window_filters.json");

    public static string GetConfigPath() => ConfigPath;

    public static WindowFilterConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<WindowFilterConfig>(json) ?? new WindowFilterConfig();
            }
        }
        catch { }

        var config = new WindowFilterConfig();
        config.Save();
        return config;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var pattern in patterns)
        {
            try
            {
                if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }
            catch (ArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid window filter regex [{pattern}]: {ex.Message}");
            }
        }

        return false;
    }
}
