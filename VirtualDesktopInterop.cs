using System.Runtime.InteropServices;

namespace LittleSwitcher;

public static class VirtualDesktopInterop
{
    private const string DllName = "VirtualDesktopAccessor.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetCurrentDesktopNumber();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetDesktopCount();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void GoToDesktopNumber(int desktopNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void MoveWindowToDesktopNumber(IntPtr hWnd, int desktopNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool IsWindowOnCurrentVirtualDesktop(IntPtr hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetWindowDesktopNumber(IntPtr hWnd);
}