using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class AppHost : Form
{
    private FocusHistory _focusHistory;
    private GlobalHotkey _globalHotkey;
    private HotkeyConfig _hotkeyConfig;
    private HashSet<IntPtr> _titleBarHiddenWindows = new();

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private IntPtr _locationHook;
    private WinEventDelegate? _locationDelegate;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const uint WM_APP_TOGGLE_MANAGEMENT = 0x8001; // WM_APP + 1

    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseProc;

    public AppHost()
    {
        InitializeComponent();

        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _focusHistory = new FocusHistory();
        _globalHotkey = new GlobalHotkey(this.Handle);
        _hotkeyConfig = HotkeyConfig.Load();

        SetupHotkeys();
        SetupMouseHook();
        SetupLocationTracking();
        SetupTrayIcon();

        BeginInvoke(() => ShowMainWindow());
    }

    private void FocusWindowWithOverlay(IntPtr hwnd)
    {
        WindowHelper.FocusWindow(hwnd);
        if (_titleBarHiddenWindows.Contains(hwnd))
        {
            var title = WindowHelper.GetWindowTitle(hwnd);
            TitleOverlay.ShowOverlay(title, hwnd);
        }
    }

    private void SetupHotkeys()
    {
        _globalHotkey.UnregisterAll();

        var cfg = _hotkeyConfig;
        var mod = cfg.Modifier;

        _globalHotkey.RegisterHotkey(mod, cfg.CycleWindowsKey, () =>
        {
            var nextWindow = _focusHistory.GetNextInCurrentContext();
            if (nextWindow.HasValue && nextWindow.Value != IntPtr.Zero)
                FocusWindowWithOverlay(nextWindow.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.FocusOtherMonitorKey, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
                FocusWindowWithOverlay(window.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.LastDesktopKey, () =>
        {
            var lastDesktop = _focusHistory.GetLastFocusedDesktop();
            _focusHistory.SetLastFocusedDesktop(VirtualDesktopInterop.GetCurrentDesktopNumber());
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
            var window = _focusHistory.GetLastFocusedWindowOnDesktop(lastDesktop);
            if (window.HasValue && window.Value != IntPtr.Zero)
                FocusWindowWithOverlay(window.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.PinWindowKey, () =>
        {
            var hwnd = WindowHelper.GetForegroundWindow();
            if (hwnd != IntPtr.Zero && hwnd != this.Handle)
            {
                if (VirtualDesktopInterop.IsPinnedWindow(hwnd))
                    VirtualDesktopInterop.UnpinWindow(hwnd);
                else
                    VirtualDesktopInterop.PinWindow(hwnd);
            }
        });

        _globalHotkey.RegisterHotkey(mod, cfg.ToggleTaskbarKey, () => WindowHelper.ToggleTaskbar());

        _globalHotkey.RegisterHotkey(mod, cfg.ToggleTitleBarKey, () =>
        {
            var hwnd = WindowHelper.GetForegroundWindow();
            if (hwnd != IntPtr.Zero && hwnd != this.Handle)
            {
                if (_titleBarHiddenWindows.Contains(hwnd))
                {
                    WindowHelper.SetTitleBarVisible(hwnd, true);
                    _titleBarHiddenWindows.Remove(hwnd);
                }
                else
                {
                    WindowHelper.SetTitleBarVisible(hwnd, false);
                    _titleBarHiddenWindows.Add(hwnd);
                    var title = WindowHelper.GetWindowTitle(hwnd);
                    TitleOverlay.ShowOverlay(title, hwnd);
                }
            }
        });

        for (uint i = 1; i <= 9; i++)
        {
            var capturedDesktopNumber = (int)(i - 1);
            var virtualKey = GlobalHotkey.VK_1 + (i - 1);

            _globalHotkey.RegisterHotkey(mod, virtualKey, () =>
            {
                var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
                _focusHistory.SetLastFocusedDesktop(currentDesktop);
                VirtualDesktopInterop.GoToDesktopNumber(capturedDesktopNumber);
                var window = _focusHistory.GetLastFocusedWindowOnDesktop(capturedDesktopNumber);
                if (window.HasValue && window.Value != IntPtr.Zero)
                    FocusWindowWithOverlay(window.Value);
            });
        }
    }

    private void SetupMouseHook()
    {
        _mouseProc = MouseHookCallback;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        System.Diagnostics.Debug.WriteLine($"[MouseHook] SetupMouseHook: hook=0x{_mouseHook:X}, lastError={Marshal.GetLastWin32Error()}");
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
        {
            int vkModifier = _hotkeyConfig.Modifier switch
            {
                GlobalHotkey.MOD_ALT => 0x12,     // VK_MENU
                GlobalHotkey.MOD_CONTROL => 0x11,  // VK_CONTROL
                GlobalHotkey.MOD_SHIFT => 0x10,    // VK_SHIFT
                GlobalHotkey.MOD_WIN => 0x5B,      // VK_LWIN
                _ => 0x12
            };

            if ((GetAsyncKeyState(vkModifier) & 0x8000) != 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var childWindow = WindowFromPoint(hookStruct.pt);
                var targetWindow = childWindow != IntPtr.Zero ? GetAncestor(childWindow, GA_ROOT) : IntPtr.Zero;

                if (targetWindow != IntPtr.Zero && targetWindow != this.Handle)
                {
                    System.Diagnostics.Debug.WriteLine($"[MouseHook] Alt+Click at ({hookStruct.pt.X},{hookStruct.pt.Y}), target=0x{targetWindow:X}. Posting message.");
                    PostMessage(this.Handle, WM_APP_TOGGLE_MANAGEMENT, targetWindow, IntPtr.Zero);
                    return (IntPtr)1; // Suppress the click
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void SetupLocationTracking()
    {
        _locationDelegate = new WinEventDelegate(LocationChangedHandler);
        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void LocationChangedHandler(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && hwnd != IntPtr.Zero && hwnd != this.Handle)
            _focusHistory.HandleWindowLocationChange(hwnd);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkey.WM_HOTKEY)
        {
            _globalHotkey.HandleHotkeyMessage(m.WParam.ToInt32());
        }
        else if (m.Msg == (int)WM_APP_TOGGLE_MANAGEMENT)
        {
            var hwnd = m.WParam;
            var title = WindowHelper.GetWindowTitle(hwnd);
            System.Diagnostics.Debug.WriteLine($"[WndProc] ToggleManagement for 0x{hwnd:X} [{title}]");
            _focusHistory.ToggleWindowManagement(hwnd);
        }
        else
        {
            base.WndProc(ref m);
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(false);
    }

    private void SetupTrayIcon()
    {
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "LittleSwitcher";
    }

    private void ShowMainWindow()
    {
        var form = new MainWindow(_hotkeyConfig, _focusHistory, config =>
        {
            _hotkeyConfig = config;
            SetupHotkeys();
        });
        var result = form.ShowDialog();

        if (result == DialogResult.Abort)
        {
            notifyIcon.Visible = false;
            Application.Exit();
        }
    }

    private void openToolStripMenuItem_Click(object sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        notifyIcon.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);

        foreach (var hwnd in _titleBarHiddenWindows)
            WindowHelper.SetTitleBarVisible(hwnd, true);
        _titleBarHiddenWindows.Clear();

        WindowHelper.ShowTaskbar();
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
