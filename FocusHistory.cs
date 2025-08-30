namespace LittleSwitcher;

public class WindowNode
{
    public IntPtr WindowHandle { get; set; }
    public WindowNode? Next { get; set; }
    public WindowNode? Previous { get; set; }

    public WindowNode(IntPtr windowHandle)
    {
        WindowHandle = windowHandle;
    }
}

public class CircularLinkedList
{
    public WindowNode? Head { get; set; }
    public WindowNode? Current { get; set; }
    private readonly Dictionary<IntPtr, WindowNode> _handleToNode = new();

    public void AddOrMoveToFront(IntPtr hWnd)
    {
        if (_handleToNode.TryGetValue(hWnd, out var existingNode))
        {
            // Move existing window to front
            MoveToFront(existingNode);
        }
        else
        {
            // Add new window to front
            var newNode = new WindowNode(hWnd);
            _handleToNode[hWnd] = newNode;

            if (Head == null)
            {
                // First node - create circular list
                Head = newNode;
                newNode.Next = newNode;
                newNode.Previous = newNode;
            }
            else
            {
                // Insert at front (after head)
                InsertAfter(Head, newNode);
                Head = newNode;
            }
        }
        Current = Head;
    }

    public IntPtr? GetNextInCycle()
    {
        if (Head == null || Current == null) return null;

        // Move to next window in cycle
        Current = Current.Next;
        
        // Skip the current focused window (head) if there are other windows
        if (Current == Head && Head.Next != Head)
        {
            Current = Current.Next;
        }

        return Current?.WindowHandle;
    }

    public void RemoveWindow(IntPtr hWnd)
    {
        if (!_handleToNode.TryGetValue(hWnd, out var node)) return;

        RemoveNode(node);
    }

    private void MoveToFront(WindowNode node)
    {
        if (node == Head) return; // Already at front
        if (Head?.Next == Head) return; // Only one node

        // Remove from current position and insert after head
        if (Head != null)
        {
            RemoveFromPosition(node);
            InsertAfter(Head, node);
            Head = node;
        }
    }

    private void InsertAfter(WindowNode existingNode, WindowNode newNode)
    {
        newNode.Next = existingNode.Next;
        newNode.Previous = existingNode;
        
        if (existingNode.Next != null)
            existingNode.Next.Previous = newNode;
        
        existingNode.Next = newNode;
    }

    private void RemoveFromPosition(WindowNode node)
    {
        if (node.Previous != null) node.Previous.Next = node.Next;
        if (node.Next != null) node.Next.Previous = node.Previous;
    }

    private void RemoveNode(WindowNode node)
    {
        RemoveFromPosition(node);

        if (Head == node)
        {
            Head = node.Next == node ? null : node.Next;
        }

        if (Current == node)
        {
            Current = Head;
        }

        _handleToNode.Remove(node.WindowHandle);
    }

    public bool IsEmpty => Head == null;
    public int Count => _handleToNode.Count;
}

public class FocusHistory
{
    private readonly Dictionary<(int desktop, IntPtr monitor), CircularLinkedList> _contextLists = new();
    private readonly object _lock = new();

    public void AddOrMoveToFront(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !WindowHelper.IsWindowVisible(hWnd))
            return;

        var title = WindowHelper.GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(title) || title.Length < 2)
            return;

        lock (_lock)
        {
            var desktop = VirtualDesktopInterop.GetWindowDesktopNumber(hWnd);
            var monitor = WindowHelper.GetWindowMonitor(hWnd);
            var key = (desktop, monitor);

            if (!_contextLists.TryGetValue(key, out var list))
            {
                list = new CircularLinkedList();
                _contextLists[key] = list;
            }

            list.AddOrMoveToFront(hWnd);
            CleanupInvalidWindows();
        }
    }

    public IntPtr? GetNextInCurrentContext()
    {
        lock (_lock)
        {
            var currentWindow = WindowHelper.GetForegroundWindow();
            if (currentWindow == IntPtr.Zero) return null;

            var desktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            var monitor = WindowHelper.GetWindowMonitor(currentWindow);
            var key = (desktop, monitor);

            if (_contextLists.TryGetValue(key, out var list))
            {
                return list.GetNextInCycle();
            }

            return null;
        }
    }

    public IntPtr? GetLastFocusedOnDifferentMonitor()
    {
        lock (_lock)
        {
            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();
            var currentWindow = WindowHelper.GetForegroundWindow();
            var currentMonitor = WindowHelper.GetWindowMonitor(currentWindow);

            // Look for windows on same desktop but different monitor
            foreach (var kvp in _contextLists)
            {
                var (desktop, monitor) = kvp.Key;
                if (desktop == currentDesktop && monitor != currentMonitor && monitor != IntPtr.Zero)
                {
                    var list = kvp.Value;
                    if (list.Head != null && WindowHelper.IsWindowVisible(list.Head.WindowHandle))
                    {
                        return list.Head.WindowHandle;
                    }
                }
            }

            return null;
        }
    }

    public IntPtr? GetLastFocusedOnDifferentDesktop()
    {
        lock (_lock)
        {
            var currentDesktop = VirtualDesktopInterop.GetCurrentDesktopNumber();

            // Look for windows on different desktop
            foreach (var kvp in _contextLists)
            {
                var (desktop, monitor) = kvp.Key;
                if (desktop != currentDesktop)
                {
                    var list = kvp.Value;
                    if (list.Head != null && WindowHelper.IsWindowVisible(list.Head.WindowHandle))
                    {
                        return list.Head.WindowHandle;
                    }
                }
            }

            return null;
        }
    }

    private void CleanupInvalidWindows()
    {
        var keysToRemove = new List<(int, IntPtr)>();

        foreach (var kvp in _contextLists)
        {
            var list = kvp.Value;
            var windowsToRemove = new List<IntPtr>();

            // Check each window in the list
            var current = list.Head;
            if (current != null)
            {
                do
                {
                    if (!WindowHelper.IsWindowVisible(current.WindowHandle))
                    {
                        windowsToRemove.Add(current.WindowHandle);
                    }
                    current = current.Next;
                } while (current != null && current != list.Head);
            }

            // Remove invalid windows
            foreach (var handle in windowsToRemove)
            {
                list.RemoveWindow(handle);
            }

            // Remove empty lists
            if (list.IsEmpty)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _contextLists.Remove(key);
        }
    }
}