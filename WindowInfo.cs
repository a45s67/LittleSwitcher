namespace LittleSwitcher;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public int VirtualDesktop { get; set; }
    public IntPtr Monitor { get; set; }
    public DateTime LastFocused { get; set; }
    
    public override string ToString()
    {
        return $"{Title} (Desktop: {VirtualDesktop})";
    }
}