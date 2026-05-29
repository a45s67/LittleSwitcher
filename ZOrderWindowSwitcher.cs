using System.Text;

namespace LittleSwitcher;

public record WindowCandidate(
    IntPtr Handle,
    string Title,
    string ClassName,
    string ProcessName,
    IntPtr Monitor,
    bool Included,
    string Reason);

public class ZOrderWindowSwitcher
{
    private readonly IntPtr _ownerHandle;
    private readonly WindowFilterConfig _filterConfig;
    private readonly int _currentProcessId = Environment.ProcessId;
    private readonly bool _currentProcessElevated = WindowHelper.IsCurrentProcessElevated();

    public ZOrderWindowSwitcher(IntPtr ownerHandle, WindowFilterConfig filterConfig)
    {
        _ownerHandle = ownerHandle;
        _filterConfig = filterConfig;
    }

    public IntPtr? GetTopWindowInCurrentContext()
    {
        var monitor = GetCurrentMonitor();
        return GetTopWindowOnMonitor(monitor);
    }

    public IntPtr? GetTopWindowOnMonitor(IntPtr monitor)
    {
        return GetTopWindowOnMonitor(monitor, excludedHandle: null);
    }

    private IntPtr? GetTopWindowOnMonitor(IntPtr monitor, IntPtr? excludedHandle)
    {
        return GetCandidates(monitor)
            .FirstOrDefault(candidate => candidate.Included && candidate.Handle != excludedHandle)?.Handle;
    }

    public IntPtr? CycleCurrentWindowToBottom()
    {
        var foreground = WindowHelper.GetForegroundWindow();
        var monitor = GetCurrentMonitor();

        if (foreground != IntPtr.Zero && foreground != _ownerHandle)
        {
            var foregroundCandidate = BuildCandidate(foreground, monitor);
            if (foregroundCandidate.Included)
                WindowHelper.SendWindowToBottom(foreground);
        }

        return GetTopWindowOnMonitor(monitor, foreground != IntPtr.Zero ? foreground : null);
    }

    public IntPtr? GetTopWindowOnDifferentMonitor()
    {
        var currentMonitor = GetCurrentMonitor();
        return GetCandidates(null)
            .Where(candidate => candidate.Included && candidate.Monitor != IntPtr.Zero && candidate.Monitor != currentMonitor)
            .Select(candidate => (IntPtr?)candidate.Handle)
            .FirstOrDefault();
    }

    public string GetStatusReport()
    {
        var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
        var currentMonitor = GetCurrentMonitor();
        var candidates = GetCandidates(null)
            .Where(candidate => candidate.Reason is not ("not visible" or "cloaked window"))
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("Z-order window switcher status:");
        report.AppendLine($"Current desktop: {currentDesktop + 1}");
        report.AppendLine($"Current monitor: {currentMonitor.ToInt64()}");
        report.AppendLine($"Filter config: {WindowFilterConfig.GetConfigPath()}");
        report.AppendLine();

        if (candidates.Count == 0)
        {
            report.AppendLine("No top-level windows found.");
            return report.ToString();
        }

        foreach (var candidate in candidates)
        {
            var marker = candidate.Included ? "IN " : "OUT";
            var title = string.IsNullOrEmpty(candidate.Title) ? "(no title)" : candidate.Title;
            report.AppendLine($"{marker} 0x{candidate.Handle.ToInt64():X} monitor={candidate.Monitor.ToInt64()} process={candidate.ProcessName} class={candidate.ClassName}");
            report.AppendLine($"    {title}");
            if (!candidate.Included)
                report.AppendLine($"    reason: {candidate.Reason}");
        }

        return report.ToString();
    }

    private IntPtr GetCurrentMonitor()
    {
        var foreground = WindowHelper.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var monitor = WindowHelper.GetWindowMonitor(foreground);
            if (monitor != IntPtr.Zero)
                return monitor;
        }

        var cursorPosition = Cursor.Position;
        return WindowHelper.MonitorFromPoint(
            new WindowHelper.POINT { X = cursorPosition.X, Y = cursorPosition.Y },
            WindowHelper.MONITOR_DEFAULTTONEAREST);
    }

    private IEnumerable<WindowCandidate> GetCandidates(IntPtr? monitor)
    {
        foreach (var hwnd in WindowHelper.EnumerateTopLevelWindowsByZOrder())
        {
            var candidate = BuildCandidate(hwnd, monitor);
            if (monitor.HasValue && candidate.Monitor != monitor.Value)
                continue;

            yield return candidate;
        }
    }

    private WindowCandidate BuildCandidate(IntPtr hwnd, IntPtr? targetMonitor)
    {
        var title = WindowHelper.GetWindowTitle(hwnd);
        var className = WindowHelper.GetWindowClassName(hwnd);
        var processName = WindowHelper.GetWindowProcessName(hwnd);
        var monitor = WindowHelper.GetWindowMonitor(hwnd);

        var includeMatched =
            WindowFilterConfig.MatchesAny(_filterConfig.IncludeTitleRegex, title) ||
            WindowFilterConfig.MatchesAny(_filterConfig.IncludeClassRegex, className) ||
            WindowFilterConfig.MatchesAny(_filterConfig.IncludeProcessRegex, processName);

        if (hwnd == IntPtr.Zero)
            return Excluded("zero handle");

        if (hwnd == _ownerHandle)
            return Excluded("LittleSwitcher host window");

        if (WindowHelper.GetWindowProcessId(hwnd) == _currentProcessId)
            return Excluded("LittleSwitcher process");

        if (!WindowHelper.IsWindowVisible(hwnd))
            return Excluded("not visible");

        if (VirtualDesktopInterop.IsPinnedWindow(hwnd))
            return Excluded("pinned window");

        if (WindowHelper.IsTopMost(hwnd))
            return Excluded("topmost window");

        var processId = WindowHelper.GetWindowProcessId(hwnd);
        if (!_currentProcessElevated && processId != 0)
        {
            if (!WindowHelper.IsProcessElevated(processId, out var elevated))
                return Excluded("process elevation unknown");

            if (elevated)
                return Excluded("elevated process");
        }

        if (!VirtualDesktopInterop.IsWindowOnCurrentVirtualDesktop(hwnd))
            return Excluded("not on current virtual desktop");

        if (targetMonitor.HasValue && monitor != targetMonitor.Value)
            return Excluded("different monitor");

        if (!WindowHelper.HasUsableRect(hwnd))
            return Excluded("empty window rect");

        if (WindowHelper.IsCloaked(hwnd))
            return Excluded("cloaked window");

        if (!includeMatched && string.IsNullOrWhiteSpace(title))
            return Excluded("empty title");

        if (!includeMatched && WindowFilterConfig.MatchesAny(_filterConfig.ExcludeTitleRegex, title))
            return Excluded("excluded by title regex");

        if (!includeMatched && WindowFilterConfig.MatchesAny(_filterConfig.ExcludeClassRegex, className))
            return Excluded("excluded by class regex");

        if (!includeMatched && WindowFilterConfig.MatchesAny(_filterConfig.ExcludeProcessRegex, processName))
            return Excluded("excluded by process regex");

        return new WindowCandidate(hwnd, title, className, processName, monitor, true, includeMatched ? "included by regex" : "eligible");

        WindowCandidate Excluded(string reason) => new(hwnd, title, className, processName, monitor, false, reason);
    }
}
