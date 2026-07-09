using System.Runtime.InteropServices;

namespace LittleSwitcher;

// Observe-only WH_KEYBOARD_LL hook: reports when a watched modifier side key
// is physically released. It never swallows or alters any event (always
// CallNextHookEx), so unlike an intercepting hook it cannot cause stuck or
// dead keys — it exists only because GetAsyncKeyState cannot answer "is the
// key still physically down" after we inject our own keyup.
//
// The hook lives on its own always-pumping thread: LL hook callbacks are
// dispatched through the installing thread's message loop, so if it lived on
// the UI thread every keystroke system-wide would stall while a hotkey
// handler runs (~100ms for the Alt+W z-order walk), and hooks that keep
// missing the LowLevelHooksTimeout get silently removed by Windows.
public class ModifierReleaseWatcher : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out NATIVEMSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVEMSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelKeyboardProc _proc; // keeps the delegate alive for the unmanaged hook
    private readonly Action<uint> _onPhysicalKeyUp;
    private IntPtr _hook;

    // Written by the UI thread, read by the hook thread; whole-array swaps
    // keep the access race-free without locking in the hook proc.
    private volatile uint[] _watchedKeys = [];

    public ModifierReleaseWatcher(Action<uint> onPhysicalKeyUp)
    {
        _onPhysicalKeyUp = onPhysicalKeyUp;
        _proc = HookProc;

        var thread = new Thread(() =>
        {
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            while (GetMessage(out _, IntPtr.Zero, 0, 0))
            {
            }
        })
        {
            IsBackground = true,
            Name = "ModifierReleaseWatcher"
        };
        thread.Start();
    }

    public void Watch(IEnumerable<ushort> vkCodes) => _watchedKeys = vkCodes.Select(vk => (uint)vk).ToArray();

    public void Unwatch() => _watchedKeys = [];

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var watched = _watchedKeys;
        if (nCode >= 0 && watched.Length > 0)
        {
            var msg = wParam.ToInt32();
            if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLKHF_INJECTED) == 0 && Array.IndexOf(watched, info.vkCode) >= 0)
                    _onPhysicalKeyUp(info.vkCode);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }
}
