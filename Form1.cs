using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class Form1 : Form
{
    private FocusHistory _focusHistory;
    private GlobalHotkey _globalHotkey;
    private StatusWindow _statusWindow;

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

    public Form1()
    {
        InitializeComponent();
        
        // Hide the form - this will be a background service
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _focusHistory = new FocusHistory();
        _globalHotkey = new GlobalHotkey(this.Handle);
        _statusWindow = new StatusWindow(_focusHistory);

        SetupHotkeys();
        SetupLocationTracking();
        SetupTrayIcon();
    }

    private void SetupHotkeys()
    {
        // Alt+W: Cycle through focus history in current context
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_W, () =>
        {
            var nextWindow = _focusHistory.GetNextInCurrentContext();
            if (nextWindow.HasValue && nextWindow.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(nextWindow.Value);
            }
        });

        // Alt+]: Last focused window on another screen
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_OEM_6, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(window.Value);
            }
        });

        // Alt+\: Go to last focused desktop then focus the last focused window on that desktop
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_OEM_5, () =>
        {
            var lastDesktop = _focusHistory.GetLastFocusedDesktop();
            _focusHistory.SetLastFocusedDesktop(VirtualDesktopInterop.GetCurrentDesktopNumber());
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
        });

        // Alt+A: Toggle current window in management system
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_A, () =>
        {
            var currentWindow = WindowHelper.GetForegroundWindow();
            if (currentWindow != IntPtr.Zero && currentWindow != this.Handle)
            {
                _focusHistory.ToggleWindowManagement(currentWindow);
            }
        });

        // Alt+1 through Alt+9: Switch to virtual desktops
        for (uint i = 1; i <= 9; i++)
        {
            var desktopNumber = (int)(i - 1); // Desktop numbers are 0-based
            var virtualKey = GlobalHotkey.VK_1 + (i - 1);
            var capturedDesktopNumber = desktopNumber; // Capture the variable for the closure

            _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, virtualKey, () =>
            {
                var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
                _focusHistory.SetLastFocusedDesktop(currentDesktop);
                VirtualDesktopInterop.GoToDesktopNumber(capturedDesktopNumber);
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
