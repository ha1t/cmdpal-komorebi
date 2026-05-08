using System;
using System.Runtime.InteropServices;

namespace KomorebiWindows;

internal static class Win32
{
    public const int SW_RESTORE = 9;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess, uint flags, [Out] char[] buf, ref uint size);

    public static string? GetExePathFromHwnd(long hwnd)
    {
        _ = GetWindowThreadProcessId(checked((IntPtr)hwnd), out var pid);
        if (pid == 0)
        {
            return null;
        }

        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buf = new char[1024];
            var size = (uint)buf.Length;
            return QueryFullProcessImageNameW(h, 0, buf, ref size)
                ? new string(buf, 0, (int)size)
                : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
