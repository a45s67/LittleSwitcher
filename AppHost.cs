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
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

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

    private void InjectModifierKeyUp()
    {
        if ((GetAsyncKeyState(ConfiguredModifierVirtualKey) & 0x8000) == 0)
            return;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)ConfiguredModifierVirtualKey, dwFlags = KEYEVENTF_KEYUP }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void FocusWindowWithOverlay(IntPtr hwnd)
    {
        InjectModifierKeyUp();
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
            InjectModifierKeyUp();

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
        InjectModifierKeyUp();

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
