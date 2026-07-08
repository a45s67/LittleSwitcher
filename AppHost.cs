using System.Runtime.InteropServices;

namespace LittleSwitcher;

public partial class AppHost : Form
{
    private ZOrderWindowSwitcher _windowSwitcher;
    private GlobalHotkey _globalHotkey;
    private HotkeyConfig _hotkeyConfig;
    private WindowFilterConfig _windowFilterConfig;
    private ModifierReleaseGuard _releaseGuard;
    private HashSet<IntPtr> _titleBarHiddenWindows = new();
    private int _lastDesktop = -1;

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const ushort VK_LCONTROL = 0xA2;

    // Left/right-specific VKs: injecting a keyup clears the async state of the
    // key we send, so detection must use the side-specific keys the hardware
    // actually reports — the generic VK reads "up" after our own injection
    // even while the key is still physically held.
    private ushort[] ConfiguredModifierSideKeys => _hotkeyConfig.Modifier switch
    {
        GlobalHotkey.MOD_ALT => [0xA4, 0xA5],     // VK_LMENU, VK_RMENU
        GlobalHotkey.MOD_CONTROL => [0xA2, 0xA3], // VK_LCONTROL, VK_RCONTROL
        GlobalHotkey.MOD_SHIFT => [0xA0, 0xA1],   // VK_LSHIFT, VK_RSHIFT
        GlobalHotkey.MOD_WIN => [0x5B, 0x5C],     // VK_LWIN, VK_RWIN
        _ => [0xA4, 0xA5]
    };

    // Side keys physically held at the last hotkey fire; consumed by
    // ReassertModifierReleaseAfterFocus once focus lands on the new window.
    private ushort[] _heldModifierKeys = [];

    public AppHost()
    {
        InitializeComponent();

        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _globalHotkey = new GlobalHotkey(this.Handle);
        _hotkeyConfig = HotkeyConfig.Load();
        _releaseGuard = new ModifierReleaseGuard(vk => SendMaskedKeyUp([vk]));
        _windowFilterConfig = WindowFilterConfig.Load();
        _windowSwitcher = new ZOrderWindowSwitcher(this.Handle, _windowFilterConfig);

        SetupHotkeys();
        SetupTrayIcon();

        BeginInvoke(() => ShowMainWindow());
    }

    private void InjectModifierKeyUp()
    {
        var held = ConfiguredModifierSideKeys
            .Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)
            .ToArray();
        _heldModifierKeys = _heldModifierKeys.Union(held).ToArray();

        if (held.Length > 0)
        {
            // From here until the physical release, the guard eats the
            // modifier's hardware events and substitutes a masked keyup at
            // release time — injection alone loses the race when the user
            // lets go the instant the desktop switches.
            _releaseGuard.Arm(held);
            SendMaskedKeyUp(held);
        }
    }

    // The physical modifier keyup is delivered to whichever window is
    // foreground when the user lets go — after a desktop switch that is the
    // newly focused window, which never saw the keydown. A bare modifier keyup
    // reads as an Alt/Win tap (menu activation) there, and an RDP client that
    // synced "modifier down" into its session on focus gain never receives the
    // release. Re-sending the masked release after focus covers both.
    private void ReassertModifierReleaseAfterFocus()
    {
        if (_heldModifierKeys.Length == 0)
            return;

        SendMaskedKeyUp(_heldModifierKeys);
        _heldModifierKeys = [];
    }

    private void SendMaskedKeyUp(ushort[] keys)
    {
        var inputs = new List<INPUT>();

        // A lone Alt (or Win) keyup makes DefWindowProc fire SC_KEYMENU (or open
        // the Start menu) on whichever window saw the keydown. Mask it with a
        // Ctrl tap in between so the keyup is no longer "unaccompanied" —
        // same trick AutoHotkey uses.
        if (_hotkeyConfig.Modifier is GlobalHotkey.MOD_ALT or GlobalHotkey.MOD_WIN)
        {
            inputs.Add(MakeKeyInput(VK_LCONTROL, 0));
            inputs.Add(MakeKeyInput(VK_LCONTROL, KEYEVENTF_KEYUP));
        }

        foreach (var vk in keys)
            inputs.Add(MakeKeyInput(vk, KEYEVENTF_KEYUP));

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags)
    {
        if (vk is 0xA5 or 0xA3 or 0x5B or 0x5C) // VK_RMENU, VK_RCONTROL, VK_LWIN, VK_RWIN
            flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    // RDP clients forward keys by scan code; wScan = 0 events
                    // never reach the remote session.
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    dwFlags = flags
                }
            }
        };
    }

    private void FocusWindowWithOverlay(IntPtr hwnd)
    {
        InjectModifierKeyUp();
        WindowHelper.FocusWindow(hwnd);
        ReassertModifierReleaseAfterFocus();
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
        // Windows on the target desktop are usually still cloaked at this
        // point (GoToDesktopNumber is async), but when they aren't, focusing
        // immediately shortens the gap in which the user's physical modifier
        // keyup can land on a transient window.
        var immediate = _windowSwitcher.GetTopWindowInCurrentContext();
        if (immediate.HasValue && immediate.Value != IntPtr.Zero)
        {
            FocusWindowWithOverlay(immediate.Value);
            return;
        }

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
        _releaseGuard.Dispose();
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
