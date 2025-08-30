using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class Form1 : Form
{
    private FocusHistory _focusHistory;
    private GlobalHotkey _globalHotkey;

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

    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private WinEventDelegate? _foregroundDelegate;
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

        SetupHotkeys();
        SetupFocusTracking();
        SetupLocationTracking();
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
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_OEM_4, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
            {
                WindowHelper.FocusWindow(window.Value);
            }
        });

        // Alt+\: Last focused window in last virtual desktop
        _globalHotkey.RegisterHotkey(GlobalHotkey.MOD_ALT, GlobalHotkey.VK_OEM_5, () =>
        {
            var window = _focusHistory.GetLastFocusedOnDifferentDesktop();
            if (window.HasValue && window.Value != IntPtr.Zero)
            {
                VirtualDesktopInterop.GoToDesktopNumber(VirtualDesktopInterop.GetWindowDesktopNumber(window.Value));
                WindowHelper.FocusWindow(window.Value);
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
                VirtualDesktopInterop.GoToDesktopNumber(capturedDesktopNumber);
            });
        }
    }

    private void SetupFocusTracking()
    {
        // Set up Windows event hook to track foreground window changes
        _foregroundDelegate = new WinEventDelegate(ForegroundChangedHandler);
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Also track current window immediately
        var currentWindow = WindowHelper.GetForegroundWindow();
        if (currentWindow != IntPtr.Zero)
        {
            _focusHistory.AddOrMoveToFront(currentWindow);
        }
    }

    private void SetupLocationTracking()
    {
        // Set up Windows event hook to track window location changes
        _locationDelegate = new WinEventDelegate(LocationChangedHandler);
        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void ForegroundChangedHandler(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero && hwnd != this.Handle)
        {
            // Add the newly focused window to our focus history
            _focusHistory.AddOrMoveToFront(hwnd);
        }
    }

    private void LocationChangedHandler(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && hwnd != IntPtr.Zero && hwnd != this.Handle)
        {
            // Handle window location change
            // You can add your custom logic here
            System.Diagnostics.Debug.WriteLine($"Window location changed: {hwnd}");
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


}
