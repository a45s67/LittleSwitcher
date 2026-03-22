using WindowsDesktop;

namespace LittleSwitcher;

public static class VirtualDesktopInterop
{
    public static int GetCurrentDesktopNumber()
    {
        var desktops = VirtualDesktop.GetDesktops();
        var current = VirtualDesktop.Current;
        for (int i = 0; i < desktops.Length; i++)
        {
            if (desktops[i].Id == current.Id)
                return i;
        }
        return 0;
    }

    public static int GetDesktopCount()
    {
        return VirtualDesktop.GetDesktops().Length;
    }

    public static void GoToDesktopNumber(int desktopNumber)
    {
        var desktops = VirtualDesktop.GetDesktops();
        if (desktopNumber >= 0 && desktopNumber < desktops.Length)
        {
            desktops[desktopNumber].Switch();
        }
    }

    public static void MoveWindowToDesktopNumber(IntPtr hWnd, int desktopNumber)
    {
        var desktops = VirtualDesktop.GetDesktops();
        if (desktopNumber >= 0 && desktopNumber < desktops.Length)
        {
            VirtualDesktop.MoveToDesktop(hWnd, desktops[desktopNumber]);
        }
    }

    public static bool IsWindowOnCurrentVirtualDesktop(IntPtr hWnd)
    {
        return VirtualDesktop.IsCurrentVirtualDesktop(hWnd);
    }

    public static int GetWindowDesktopNumber(IntPtr hWnd)
    {
        try
        {
            var desktop = VirtualDesktop.FromHwnd(hWnd);
            if (desktop == null) return -1;

            var desktops = VirtualDesktop.GetDesktops();
            for (int i = 0; i < desktops.Length; i++)
            {
                if (desktops[i].Id == desktop.Id)
                    return i;
            }
            return -1;
        }
        catch
        {
            return -1;
        }
    }
}
