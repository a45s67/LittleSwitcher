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

    // Combo mode: after ReleaseModifierBeforeSwitch clears the async modifier
    // state, RegisterHotKey no longer matches modifier+key while the user
    // keeps holding the modifier. While it is physically held we register the
    // trigger keys as bare hotkeys (same actions), and drop them the moment
    // the watcher sees the physical release.
    private ModifierReleaseWatcher _releaseWatcher = null!;
    private readonly Dictionary<uint, Action> _hotkeyBindings = new();
    private readonly List<int> _comboHotkeyIds = new();
    private System.Windows.Forms.Timer? _comboTimeout;

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
        // MOUSEINPUT must be present even though only ki is used: it is the
        // union's largest member, and without it Marshal.SizeOf<INPUT>() is 32
        // instead of 40 on x64 — SendInput validates cbSize and silently
        // rejects the whole batch.
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    // Left/right-specific VKs — the hardware reports these (e.g. RMENU for
    // right Alt), so physical-hold detection must check both sides.
    private ushort[] ConfiguredModifierSideKeys => _hotkeyConfig.Modifier switch
    {
        GlobalHotkey.MOD_ALT => [0xA4, 0xA5],     // VK_LMENU, VK_RMENU
        GlobalHotkey.MOD_CONTROL => [0xA2, 0xA3], // VK_LCONTROL, VK_RCONTROL
        GlobalHotkey.MOD_SHIFT => [0xA0, 0xA1],   // VK_LSHIFT, VK_RSHIFT
        GlobalHotkey.MOD_WIN => [0x5B, 0x5C],     // VK_LWIN, VK_RWIN
        _ => [0xA4, 0xA5]
    };

    public AppHost()
    {
        InitializeComponent();

        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Visible = false;

        _globalHotkey = new GlobalHotkey(this.Handle);
        _hotkeyConfig = HotkeyConfig.Load();
        _releaseWatcher = new ModifierReleaseWatcher(_ => BeginInvoke(ExitComboMode));
        _windowFilterConfig = WindowFilterConfig.Load();
        _windowSwitcher = new ZOrderWindowSwitcher(this.Handle, _windowFilterConfig);

        SetupHotkeys();
        SetupTrayIcon();

        BeginInvoke(() => ShowMainWindow());
    }

    // A window activated while the modifier is physically held has its thread
    // key state synced from the async table as "modifier down"; the user's
    // physical keyup then completes a clean Alt tap and arms menu/mnemonic
    // mode (RDCMan measured: stuck when released +14ms after activation, safe
    // at +75ms). Intervening dummy events do not help — the arming comes from
    // state sync, not the message sequence — so the async state must read
    // "up" BEFORE the foreground changes: inject the release synchronously in
    // the hotkey handler. The Ctrl tap masks the injected keyup for the old
    // window, which saw the real Alt keydown and would otherwise fire
    // SC_KEYMENU on an unaccompanied down→up pair (AutoHotkey's mask trick).
    private void ReleaseModifierBeforeSwitch()
    {
        var held = ConfiguredModifierSideKeys
            .Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)
            .ToArray();
        if (held.Length == 0)
        {
            // A combo fire (bare hotkey while the modifier is still physically
            // held): the release was already injected, just extend the window.
            if (_comboHotkeyIds.Count > 0)
                RestartComboTimeout();
            return;
        }

        var inputs = new List<INPUT>();

        if (_hotkeyConfig.Modifier is GlobalHotkey.MOD_ALT or GlobalHotkey.MOD_WIN)
        {
            inputs.Add(MakeKeyInput(VK_LCONTROL, 0));
            inputs.Add(MakeKeyInput(VK_LCONTROL, KEYEVENTF_KEYUP));
        }

        foreach (var vk in held)
            inputs.Add(MakeKeyInput(vk, KEYEVENTF_KEYUP));

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        EnterComboMode();
    }

    private void EnterComboMode()
    {
        if (_comboHotkeyIds.Count == 0)
        {
            foreach (var (key, action) in _hotkeyBindings)
            {
                var id = _globalHotkey.RegisterHotkey(0, key, action);
                if (id >= 0)
                    _comboHotkeyIds.Add(id);
            }

            _releaseWatcher.Watch(ConfiguredModifierSideKeys);
        }

        RestartComboTimeout();
    }

    // Safety valve: if the physical release is never seen (e.g. it happened on
    // the UAC secure desktop where LL hooks don't run), don't leave bare
    // hotkeys registered forever.
    private void RestartComboTimeout()
    {
        if (_comboTimeout == null)
        {
            _comboTimeout = new System.Windows.Forms.Timer { Interval = 10000 };
            _comboTimeout.Tick += (_, _) => ExitComboMode();
        }

        _comboTimeout.Stop();
        _comboTimeout.Start();
    }

    private void ExitComboMode()
    {
        foreach (var id in _comboHotkeyIds)
            _globalHotkey.UnregisterHotkey(id);
        _comboHotkeyIds.Clear();
        _releaseWatcher.Unwatch();
        _comboTimeout?.Stop();
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
                    // Real scan codes so RDP clients (which forward by scan
                    // code) see the events too.
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    dwFlags = flags
                }
            }
        };
    }

    private void FocusWindowWithOverlay(IntPtr hwnd)
    {
        ReleaseModifierBeforeSwitch();
        WindowHelper.FocusWindow(hwnd);
        if (_titleBarHiddenWindows.Contains(hwnd))
        {
            var title = WindowHelper.GetWindowTitle(hwnd);
            TitleOverlay.ShowOverlay(title, hwnd);
        }
    }

    // Registers modifier+key and records key→action so EnterComboMode can
    // re-register the same actions as bare hotkeys.
    private void RegisterBoundHotkey(uint mod, uint key, Action action)
    {
        _globalHotkey.RegisterHotkey(mod, key, action);
        _hotkeyBindings[key] = action;
    }

    private void SetupHotkeys()
    {
        ExitComboMode();
        _globalHotkey.UnregisterAll();
        _hotkeyBindings.Clear();

        var cfg = _hotkeyConfig;
        var mod = cfg.Modifier;

        RegisterBoundHotkey(mod, cfg.CycleWindowsKey, () =>
        {
            var nextWindow = _windowSwitcher.CycleCurrentWindowToBottom();
            if (nextWindow.HasValue && nextWindow.Value != IntPtr.Zero)
                FocusWindowWithOverlay(nextWindow.Value);
        });

        RegisterBoundHotkey(mod, cfg.FocusOtherMonitorKey, () =>
        {
            var window = _windowSwitcher.GetTopWindowOnDifferentMonitor();
            if (window.HasValue && window.Value != IntPtr.Zero)
                FocusWindowWithOverlay(window.Value);
        });

        RegisterBoundHotkey(mod, cfg.LastDesktopKey, () =>
        {
            ReleaseModifierBeforeSwitch();

            var lastDesktop = _lastDesktop;
            if (lastDesktop < 0)
                return;

            _lastDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            VirtualDesktopInterop.GoToDesktopNumber(lastDesktop);
            FocusTopWindowAfterDesktopSwitch();
        });

        RegisterBoundHotkey(mod, cfg.PreviousDesktopKey, () =>
        {
            var count = VirtualDesktopInterop.GetDesktopCount();
            if (count <= 0)
                return;

            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            SwitchToDesktop((currentDesktop - 1 + count) % count);
        });

        RegisterBoundHotkey(mod, cfg.NextDesktopKey, () =>
        {
            var count = VirtualDesktopInterop.GetDesktopCount();
            if (count <= 0)
                return;

            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            SwitchToDesktop((currentDesktop + 1) % count);
        });

        RegisterBoundHotkey(mod, cfg.PinWindowKey, () =>
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

        RegisterBoundHotkey(mod, cfg.ToggleTaskbarKey, () => WindowHelper.ToggleTaskbar());

        RegisterBoundHotkey(mod, cfg.ToggleTitleBarKey, () =>
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

            RegisterBoundHotkey(mod, virtualKey, () =>
            {
                SwitchToDesktop(capturedDesktopNumber);
            });
        }
    }

    private void SwitchToDesktop(int desktopNumber)
    {
        ReleaseModifierBeforeSwitch();

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
        _releaseWatcher.Dispose();
        notifyIcon.Visible = false;
        base.OnFormClosing(e);
    }
}
