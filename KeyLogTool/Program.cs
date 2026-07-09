using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLogTool;

// Diagnostic logger for LittleSwitcher's modifier-key issues: prints every
// low-level keyboard event (including the LLKHF_INJECTED flag that Spy++
// cannot show) interleaved with foreground-window changes on one shared
// timeline, so "which window received which key event, in what order" can be
// reconstructed while reproducing a bug.
internal static class Program
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_FOCUS = 0x8005;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint LLKHF_INJECTED = 0x10;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    // Writing to the console from inside the hook proc can block (e.g. console
    // quick-edit text selection pauses output), which would stall keyboard
    // input system-wide — a LL hook must return fast. Queue lines instead and
    // print them from a background thread.
    private static readonly BlockingCollection<string> Lines = new();

    // Kept in fields so the GC never collects the delegates the unmanaged
    // hooks are calling into.
    private static LowLevelKeyboardProc? _keyboardProc;
    private static WinEventDelegate? _winEventProc;
    private static WinEventDelegate? _focusEventProc;

    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var printer = new Thread(() =>
        {
            foreach (var line in Lines.GetConsumingEnumerable())
                Console.WriteLine(line);
        })
        { IsBackground = true };
        printer.Start();

        _keyboardProc = KeyboardHookProc;
        var kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);

        _winEventProc = ForegroundChanged;
        var fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Control-level focus changes: shows how focus settles *inside* the
        // newly foregrounded app (frame → child control), i.e. the danger
        // window during which a bare modifier keyup gets mishandled.
        _focusEventProc = FocusChanged;
        SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _focusEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        Log("---", $"KeyLogTool started (keyboard hook: {(kbHook != IntPtr.Zero ? "ok" : "FAILED")}, " +
                   $"foreground hook: {(fgHook != IntPtr.Zero ? "ok" : "FAILED")}). Ctrl+C to quit.");
        Log("FG ", Describe(GetForegroundWindow()) + "  (initial)");

        while (GetMessage(out _, IntPtr.Zero, 0, 0))
        {
        }
    }

    private static IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32() switch
            {
                0x0100 => "KEYDOWN   ",
                0x0101 => "KEYUP     ",
                0x0104 => "SYSKEYDOWN",
                0x0105 => "SYSKEYUP  ",
                var m => $"0x{m:X4}    "
            };
            var flags = ((info.flags & LLKHF_INJECTED) != 0 ? "  INJECTED" : "") +
                        ((info.flags & LLKHF_EXTENDED) != 0 ? "  ext" : "");
            Log("KEY", $"{msg} {VkName(info.vkCode),-10} scan=0x{info.scanCode:X2}{flags}");
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void ForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Log("FG ", Describe(hwnd));
    }

    private static void FocusChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Log("FOC", Describe(hwnd));
    }

    private static void Log(string tag, string text)
    {
        Lines.Add($"[{Clock.Elapsed.TotalMilliseconds,10:F1}ms] {tag} {text}");
    }

    private static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "0x0 (none)";

        var title = new StringBuilder(128);
        GetWindowText(hwnd, title, title.Capacity);

        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, cls.Capacity);

        GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch
        {
            process = "?";
        }

        return $"0x{hwnd:X} \"{title}\" [{process} / {cls}]";
    }

    private static string VkName(uint vk) => vk switch
    {
        0x10 => "SHIFT",
        0x11 => "CONTROL",
        0x12 => "MENU",
        0x5B => "LWIN",
        0x5C => "RWIN",
        0xA0 => "LSHIFT",
        0xA1 => "RSHIFT",
        0xA2 => "LCONTROL",
        0xA3 => "RCONTROL",
        0xA4 => "LMENU",
        0xA5 => "RMENU",
        0x08 => "BACK",
        0x09 => "TAB",
        0x0D => "RETURN",
        0x1B => "ESCAPE",
        0x20 => "SPACE",
        0xBF => "OEM_2(/)",
        0xDB => "OEM_4([)",
        0xDC => "OEM_5(\\)",
        0xDD => "OEM_6(])",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        _ => $"0x{vk:X2}"
    };
}
