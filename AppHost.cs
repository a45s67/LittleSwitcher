using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class AppHost : Form
{
    private ZOrderWindowSwitcher _windowSwitcher;
    private GlobalHotkey _globalHotkey;
    private HotkeyConfig _hotkeyConfig;
    private WindowFilterConfig _windowFilterConfig;
    private HashSet<IntPtr> _titleBarHiddenWindows = new();
    private int _lastDesktop = -1;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const int VK_MENU = 0x12;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYUP = 0x0105;

    private int ConfiguredModifierVirtualKey => _hotkeyConfig.Modifier switch
    {
        GlobalHotkey.MOD_ALT => 0x12,      // VK_MENU
        GlobalHotkey.MOD_CONTROL => 0x11,  // VK_CONTROL
        GlobalHotkey.MOD_SHIFT => 0x10,    // VK_SHIFT
        GlobalHotkey.MOD_WIN => 0x5B,      // VK_LWIN
        _ => 0x12
    };

    public AppHost()
    {
        InitializeComponent();

        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _globalHotkey = new GlobalHotkey(this.Handle);
        _hotkeyConfig = HotkeyConfig.Load();
        _windowFilterConfig = WindowFilterConfig.Load();
        _windowSwitcher = new ZOrderWindowSwitcher(this.Handle, _windowFilterConfig);

        SetupHotkeys();
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
            var nextWindow = _windowSwitcher.CycleCurrentWindowToBottom();
            if (nextWindow.HasValue && nextWindow.Value != IntPtr.Zero)
                FocusWindowWithOverlay(nextWindow.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.FocusOtherMonitorKey, () =>
        {
            var window = _windowSwitcher.GetTopWindowOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
                FocusWindowWithOverlay(window.Value);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.LastDesktopKey, () =>
        {
            ReleaseModifierForForegroundApp();

            var lastDesktop = _lastDesktop;
            if (lastDesktop < 0)
                return;

            _lastDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
            FocusTopWindowAfterDesktopSwitch();
        });

        _globalHotkey.RegisterHotkey(mod, cfg.PreviousDesktopKey, () =>
        {
            var count = VirtualDesktopInterop.GetDesktopCount();
            if (count <= 0)
                return;

            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            SwitchToDesktop((currentDesktop - 1 + count) % count);
        });

        _globalHotkey.RegisterHotkey(mod, cfg.NextDesktopKey, () =>
        {
            var count = VirtualDesktopInterop.GetDesktopCount();
            if (count <= 0)
                return;

            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            SwitchToDesktop((currentDesktop + 1) % count);
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
                SwitchToDesktop(capturedDesktopNumber);
            });
        }
    }

    private void SwitchToDesktop(int desktopNumber)
    {
        ReleaseModifierForForegroundApp();

        var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
        _lastDesktop = currentDesktop;
        VirtualDesktopInterop.GoToDesktopNumber(desktopNumber);
        FocusTopWindowAfterDesktopSwitch();
    }

    private void FocusTopWindowAfterDesktopSwitch()
    {
        var attempts = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 80 };
        timer.Tick += (_, _) =>
        {
            attempts++;
            var window = _windowSwitcher.GetTopWindowInCurrentContext();
            if ((window.HasValue && window.Value != IntPtr.Zero) || attempts >= 10)
            {
                timer.Stop();
                timer.Dispose();

                if (window.HasValue && window.Value != IntPtr.Zero)
                    FocusWindowWithOverlay(window.Value);
            }
        };
        timer.Start();
    }

    private void ReleaseModifierForForegroundApp()
    {
        var hwnd = WindowHelper.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == this.Handle)
            return;

        var modifierKey = ConfiguredModifierVirtualKey;
        var scanCode = MapVirtualKey((uint)modifierKey, MAPVK_VK_TO_VSC);
        var message = modifierKey == VK_MENU ? WM_SYSKEYUP : WM_KEYUP;
        var lParam = 1 | ((int)scanCode << 16) | (1 << 30) | unchecked((int)0x80000000);

        if (modifierKey == VK_MENU)
            lParam |= 1 << 29;

        PostMessage(hwnd, message, (IntPtr)modifierKey, (IntPtr)lParam);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkey.WM_HOTKEY)
        {
            _globalHotkey.HandleHotkeyMessage(m.WParam.ToInt32());
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
        var form = new MainWindow(_hotkeyConfig, _windowSwitcher, config =>
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
        foreach (var hwnd in _titleBarHiddenWindows)
            WindowHelper.SetTitleBarVisible(hwnd, true);
        _titleBarHiddenWindows.Clear();

        WindowHelper.ShowTaskbar();
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
