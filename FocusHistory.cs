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
    private int _lastFocusedDesktop = -1;

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
            if (desktop == -1)
            {
                return;
            }
            var monitor = WindowHelper.GetWindowMonitor(hWnd);
            var key = (desktop, monitor);

            // Update last focused desktop if different
            if (!_contextLists.TryGetValue(key, out var list))
            {
                list = new CircularLinkedList();
                _contextLists[key] = list;
            }

            list.AddOrMoveToFront(hWnd);
            
            // Log the window context and linked list info
            var windowTitle = WindowHelper.GetWindowTitle(hWnd);
            System.Diagnostics.Debug.WriteLine($"AddOrMoveToFront: [{windowTitle}] -> Desktop:{desktop}, Monitor:{monitor}");
            
            if (list.Head != null)
            {
                var prevTitle = list.Head.Previous != null ? WindowHelper.GetWindowTitle(list.Head.Previous.WindowHandle) : "null";
                var nextTitle = list.Head.Next != null ? WindowHelper.GetWindowTitle(list.Head.Next.WindowHandle) : "null";
                System.Diagnostics.Debug.WriteLine($"  Linked List: Prev=[{prevTitle}] <- Current=[{windowTitle}] -> Next=[{nextTitle}]");
            }
            
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

    public int GetLastFocusedDesktop()
    {
        lock (_lock)
        {
            return _lastFocusedDesktop;
        }
    }
    public void SetLastFocusedDesktop(int desktop)
    {
        lock (_lock)
        {
            _lastFocusedDesktop = desktop;
        }
    }

    public IntPtr? GetLastFocusedWindowOnDesktop(int desktop)
    {
        lock (_lock)
        {
            IntPtr? bestWindow = null;
            
            // Look through all contexts on the specified desktop
            foreach (var kvp in _contextLists)
            {
                var (contextDesktop, monitor) = kvp.Key;
                if (contextDesktop == desktop)
                {
                    var list = kvp.Value;
                    if (list.Head != null && WindowHelper.IsWindowVisible(list.Head.WindowHandle))
                    {
                        bestWindow = list.Head.WindowHandle;
                        break; // Return the first valid window found (most recently focused in that context)
                    }
                }
            }

            return bestWindow;
        }
    }

    public void HandleWindowLocationChange(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !WindowHelper.IsWindowVisible(hWnd))
            return;

        lock (_lock)
        {
            var newDesktop = VirtualDesktopInterop.GetWindowDesktopNumber(hWnd);
            var newMonitor = WindowHelper.GetWindowMonitor(hWnd);
            var newKey = (newDesktop, newMonitor);

            // Find and remove the window from its old context
            var oldKey = FindWindowContext(hWnd);
            if (oldKey.HasValue && oldKey.Value != newKey)
            {
                if (_contextLists.TryGetValue(oldKey.Value, out var oldList))
                {
                    oldList.RemoveWindow(hWnd);
                    
                    // Remove empty list
                    if (oldList.IsEmpty)
                    {
                        _contextLists.Remove(oldKey.Value);
                    }
                }

                // Add to new context (only if monitor actually changed)
                if (oldKey.Value.monitor != newMonitor && newMonitor != IntPtr.Zero)
                {
                    if (!_contextLists.TryGetValue(newKey, out var newList))
                    {
                        newList = new CircularLinkedList();
                        _contextLists[newKey] = newList;
                    }
                    newList.AddOrMoveToFront(hWnd);
                }
            }
        }
    }

    private (int desktop, IntPtr monitor)? FindWindowContext(IntPtr hWnd)
    {
        foreach (var kvp in _contextLists)
        {
            var list = kvp.Value;
            var current = list.Head;
            if (current != null)
            {
                do
                {
                    if (current.WindowHandle == hWnd)
                    {
                        return kvp.Key;
                    }
                    current = current.Next;
                } while (current != null && current != list.Head);
            }
        }
        return null;
    }

    public void ToggleWindowManagement(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !WindowHelper.IsWindowVisible(hWnd))
            return;

        var title = WindowHelper.GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(title) || title.Length < 2)
            return;

        lock (_lock)
        {
            var desktop = VirtualDesktopInterop.GetWindowDesktopNumber(hWnd);
            if (desktop == -1)
            {
                return;
            }
            var monitor = WindowHelper.GetWindowMonitor(hWnd);
            var key = (desktop, monitor);

            // Check if window is already being managed
            var existingContext = FindWindowContext(hWnd);
            
            if (existingContext.HasValue)
            {
                // Remove from management
                if (_contextLists.TryGetValue(existingContext.Value, out var existingList))
                {
                    existingList.RemoveWindow(hWnd);
                    System.Diagnostics.Debug.WriteLine($"Removed from management: [{title}]");
                    
                    // Remove empty list
                    if (existingList.IsEmpty)
                    {
                        _contextLists.Remove(existingContext.Value);
                    }
                }
            }
            else
            {
                // Add to management
                if (!_contextLists.TryGetValue(key, out var list))
                {
                    list = new CircularLinkedList();
                    _contextLists[key] = list;
                }

                list.AddOrMoveToFront(hWnd);
                System.Diagnostics.Debug.WriteLine($"Added to management: [{title}] -> Desktop:{desktop}, Monitor:{monitor}");
            }
            
            CleanupInvalidWindows();
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