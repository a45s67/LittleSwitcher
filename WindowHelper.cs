using System.Runtime.InteropServices;
using System.Text;

namespace LittleSwitcher;

public static class WindowHelper
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        out TOKEN_ELEVATION tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;
    public const int SM_CMONITORS = 80;
    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint GW_HWNDNEXT = 2;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int DWMWA_CLOAKED = 14;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    public static string GetWindowProcessName(IntPtr hWnd)
    {
        try
        {
            var processId = GetWindowProcessId(hWnd);
            if (processId == 0)
                return string.Empty;

            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static int GetWindowProcessId(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var processId);
        return (int)processId;
    }

    public static bool IsCurrentProcessElevated() => IsProcessElevated(Environment.ProcessId, out var elevated) && elevated;

    public static bool IsProcessElevated(int processId, out bool elevated)
    {
        elevated = false;
        var processHandle = IntPtr.Zero;
        var tokenHandle = IntPtr.Zero;

        try
        {
            processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (processHandle == IntPtr.Zero)
                return false;

            if (!OpenProcessToken(processHandle, TOKEN_QUERY, out tokenHandle))
                return false;

            if (!GetTokenInformation(tokenHandle, TokenElevation, out var tokenElevation,
                    Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                return false;

            elevated = tokenElevation.TokenIsElevated != 0;
            return true;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                CloseHandle(tokenHandle);

            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }
    }

    public static int GetMonitorCount()
    {
        return GetSystemMetrics(SM_CMONITORS);
    }

    public static IntPtr GetWindowMonitor(IntPtr hWnd)
    {
        return MonitorFromWindow(hWnd, MONITOR_DEFAULTTONULL);
    }

    public static bool IsCloaked(IntPtr hWnd)
    {
        try
        {
            return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasUsableRect(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out var rect))
            return false;

        return rect.Right > rect.Left && rect.Bottom > rect.Top;
    }

    public static IEnumerable<IntPtr> EnumerateTopLevelWindowsByZOrder()
    {
        var hwnd = GetTopWindow(IntPtr.Zero);
        while (hwnd != IntPtr.Zero)
        {
            yield return hwnd;
            hwnd = GetWindow(hwnd, GW_HWNDNEXT);
        }
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

    public static bool IsTopMost(IntPtr hWnd)
    {
        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        return (exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST;
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

    public static void SendWindowToBottom(IntPtr hWnd)
    {
        SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
