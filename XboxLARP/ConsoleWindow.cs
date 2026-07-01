using System.Runtime.InteropServices;

namespace XboxLARP;

/// <summary>The exe is built with the console subsystem so CLI diagnostic commands
/// (--query, --apply-profile, etc.) print immediately without needing AllocConsole tricks.
/// Tray mode hides that console window so it behaves like a normal background GUI app.</summary>
public static class ConsoleWindow
{
    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void Hide()
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
            ShowWindow(handle, SW_HIDE);
    }
}
