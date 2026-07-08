using System.Runtime.InteropServices;

namespace LittleSwitcher;

// WH_KEYBOARD_LL guard, armed at each hotkey fire while the modifier is
// physically held. Injection-based masking cannot win the race against a
// modifier released the instant the desktop switches — the shell foregrounds
// the target window within tens of ms and the bare physical keyup lands there
// before any post-focus re-injection. The guard intercepts the physical keyup
// before any window sees it and replaces it with the masked, scan-coded
// release sequence, so ordering is guaranteed regardless of timing.
public class ModifierReleaseGuard : IDisposable
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

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    // Safety valve: if the release is never seen (e.g. it happened on the UAC
    // secure desktop where LL hooks don't run), disarm instead of eating the
    // modifier's keydowns forever.
    private const int GuardTimeoutMs = 5000;

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
    private readonly IntPtr _hook;
    private readonly Action<ushort> _sendMaskedKeyUp;
    private readonly HashSet<uint> _guardedKeys = new();
    private long _armedAt;

    public ModifierReleaseGuard(Action<ushort> sendMaskedKeyUp)
    {
        _sendMaskedKeyUp = sendMaskedKeyUp;
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Arm(IEnumerable<ushort> vkCodes)
    {
        foreach (var vk in vkCodes)
            _guardedKeys.Add(vk);
        _armedAt = Environment.TickCount64;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _guardedKeys.Count > 0)
        {
            if (Environment.TickCount64 - _armedAt > GuardTimeoutMs)
            {
                _guardedKeys.Clear();
            }
            else
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLKHF_INJECTED) == 0 && _guardedKeys.Contains(info.vkCode))
                {
                    var msg = wParam.ToInt32();
                    if (msg is WM_KEYUP or WM_SYSKEYUP)
                    {
                        _guardedKeys.Remove(info.vkCode);
                        _sendMaskedKeyUp((ushort)info.vkCode);
                    }
                    // Eaten keydowns are hardware repeats of a key whose release
                    // was already injected at hotkey-fire time; eaten keyups are
                    // replaced by the masked injection above, so the system key
                    // state stays consistent.
                    return 1;
                }
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
