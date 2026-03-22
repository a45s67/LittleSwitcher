using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class AppHost : Form
{
    private FocusHistory _focusHistory;
    private GlobalHotkey _globalHotkey;
    private StatusWindow _statusWindow;
    private HotkeyConfig _hotkeyConfig;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private IntPtr _locationHook;
    private WinEventDelegate? _locationDelegate;

    public AppHost()
    {
        InitializeComponent();
        
        // Hide the form - this will be a background service
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _focusHistory = new FocusHistory();
        _globalHotkey = new GlobalHotkey(this.Handle);
        _statusWindow = new StatusWindow(_focusHistory);
        _hotkeyConfig = HotkeyConfig.Load();

        SetupHotkeys();
        SetupLocationTracking();
        SetupTrayIcon();

        // Show settings on launch so user sees the app is running
        BeginInvoke(() => settingsToolStripMenuItem_Click(this, EventArgs.Empty));
    }

    private void SetupHotkeys()
    {
        _globalHotkey.UnregisterAll();

        var cfg = _hotkeyConfig;

        _globalHotkey.RegisterHotkey(cfg.CycleWindows.Modifiers, cfg.CycleWindows.Key, () =>
        {
            var nextWindow = _focusHistory.GetNextInCurrentContext();
            if (nextWindow.HasValue && nextWindow.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(nextWindow.Value);
            }
        });

        _globalHotkey.RegisterHotkey(cfg.FocusOtherMonitor.Modifiers, cfg.FocusOtherMonitor.Key, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(window.Value);
            }
        });

        _globalHotkey.RegisterHotkey(cfg.LastDesktop.Modifiers, cfg.LastDesktop.Key, () =>
        {
            var lastDesktop = _focusHistory.GetLastFocusedDesktop();
            _focusHistory.SetLastFocusedDesktop(VirtualDesktopInterop.GetCurrentDesktopNumber());
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
            var window = _focusHistory.GetLastFocusedWindowOnDesktop(lastDesktop);
            if (window.HasValue && window.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(window.Value);
            }
        });

        _globalHotkey.RegisterHotkey(cfg.ToggleManagement.Modifiers, cfg.ToggleManagement.Key, () =>
        {
            var currentWindow = WindowHelper.GetForegroundWindow();
            if (currentWindow != IntPtr.Zero && currentWindow != this.Handle)
            {
                _focusHistory.ToggleWindowManagement(currentWindow);
            }
        });

        // 1-9: Switch to virtual desktops
        for (uint i = 1; i <= 9; i++)
        {
            var capturedDesktopNumber = (int)(i - 1);
            var virtualKey = GlobalHotkey.VK_1 + (i - 1);

            _globalHotkey.RegisterHotkey(cfg.SwitchDesktopModifier, virtualKey, () =>
            {
                var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
                _focusHistory.SetLastFocusedDesktop(currentDesktop);
                VirtualDesktopInterop.GoToDesktopNumber(capturedDesktopNumber);
                var window = _focusHistory.GetLastFocusedWindowOnDesktop(capturedDesktopNumber);
                if (window.HasValue && window.Value != IntPtr.Zero)
                {
                    WindowHelper.FocusWindow(window.Value);
                }
            });
        }
    }


    private void SetupLocationTracking()
    {
        // Set up Windows event hook to track window location changes
        _locationDelegate = new WinEventDelegate(LocationChangedHandler);
        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }


    private void LocationChangedHandler(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && hwnd != IntPtr.Zero && hwnd != this.Handle)
        {
            // Handle window location change - update context lists if window moved to different monitor
            _focusHistory.HandleWindowLocationChange(hwnd);
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Handle hotkey messages
        if (m.Msg == GlobalHotkey.WM_HOTKEY)
        {
            var hotkeyId = m.WParam.ToInt32();
            _globalHotkey.HandleHotkeyMessage(hotkeyId);
        }
        else
        {
            base.WndProc(ref m);
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        // Prevent the form from being visible
        base.SetVisibleCore(false);
    }

    private void SetupTrayIcon()
    {
        // Use default system application icon for now
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "LittleSwitcher - Desktop Switcher";
    }

    private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
    {
        // Unregister hotkeys so they don't intercept keypresses in the settings form
        _globalHotkey.UnregisterAll();

        var form = new SettingsForm(_hotkeyConfig, config =>
        {
            _hotkeyConfig = config;
        });
        var result = form.ShowDialog();

        if (result == DialogResult.Abort)
        {
            // User clicked "Exit App"
            notifyIcon.Visible = false;
            Application.Exit();
            return;
        }

        // Re-register hotkeys after settings form closes
        SetupHotkeys();
    }

    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        // Hide tray icon first
        notifyIcon.Visible = false;
        
        // Exit the application
        Application.Exit();
    }

    private void showStatusToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_statusWindow.Visible)
        {
            _statusWindow.Hide();
        }
        else
        {
            _statusWindow.Show();
            _statusWindow.BringToFront();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Ensure proper cleanup when form is closing
        _statusWindow?.Close();
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
