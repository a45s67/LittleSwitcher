using System.Runtime.InteropServices;
using System.Text;

namespace LittleSwitcher;

public static class WindowHelper
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;
    public const int SM_CMONITORS = 80;
    public const uint MONITOR_DEFAULTTONULL = 0;

    public const int GWL_STYLE = -16;
    public const int WS_CAPTION = 0x00C00000;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int GetMonitorCount()
    {
        return GetSystemMetrics(SM_CMONITORS);
    }

    public static IntPtr GetWindowMonitor(IntPtr hWnd)
    {
        return MonitorFromWindow(hWnd, MONITOR_DEFAULTTONULL);
    }

    public static void ToggleTaskbar()
    {
        var hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero) return;

        int cmd = IsWindowVisible(hwnd) ? SW_HIDE : SW_RESTORE;

        ShowWindow(hwnd, cmd);

        var prev = IntPtr.Zero;
        while (true)
        {
            prev = FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (prev == IntPtr.Zero) break;
            ShowWindow(prev, cmd);
        }
    }

    public static void ShowTaskbar()
    {
        var hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_RESTORE);

        var prev = IntPtr.Zero;
        while (true)
        {
            prev = FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (prev == IntPtr.Zero) break;
            ShowWindow(prev, SW_RESTORE);
        }
    }

    public static bool HasTitleBar(IntPtr hWnd)
    {
        var style = GetWindowLong(hWnd, GWL_STYLE);
        return (style & WS_CAPTION) == WS_CAPTION;
    }

    public static void SetTitleBarVisible(IntPtr hWnd, bool visible)
    {
        var style = GetWindowLong(hWnd, GWL_STYLE);
        if (visible)
            style |= WS_CAPTION;
        else
            style &= ~WS_CAPTION;

        SetWindowLong(hWnd, GWL_STYLE, style);
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
    }

    public static void FocusWindow(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }
        SetForegroundWindow(hWnd);

        if (GetWindowRect(hWnd, out RECT rect))
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
            {
                SetCursorPos(rect.Left + width / 2, rect.Top + height / 2);
            }
        }
    }
}