using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class AppHost : Form
{
    private FocusHistory _focusHistory;
    private GlobalHotkey _globalHotkey;
    private HotkeyConfig _hotkeyConfig;

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
        SetupLocationTracking();
        SetupTrayIcon();

        BeginInvoke(() => ShowMainWindow());
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
                WindowHelper.FocusWindow(nextWindow.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.FocusOtherMonitorKey, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
                WindowHelper.FocusWindow(window.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.LastDesktopKey, () =>
        {
            var lastDesktop = _focusHistory.GetLastFocusedDesktop();
            _focusHistory.SetLastFocusedDesktop(VirtualDesktopInterop.GetCurrentDesktopNumber());
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
            var window = _focusHistory.GetLastFocusedWindowOnDesktop(lastDesktop);
            if (window.HasValue && window.Value != IntPtr.Zero)
                WindowHelper.FocusWindow(window.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.ToggleManagementKey, () =>
        {
            var currentWindow = WindowHelper.GetForegroundWindow();
            if (currentWindow != IntPtr.Zero && currentWindow != this.Handle)
                _focusHistory.ToggleWindowManagement(currentWindow);
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
                    WindowHelper.FocusWindow(window.Value);
            });
        }
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
            _globalHotkey.HandleHotkeyMessage(m.WParam.ToInt32());
        else
            base.WndProc(ref m);
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
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
