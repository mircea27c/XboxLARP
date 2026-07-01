using Microsoft.Win32;

namespace XboxLARP;

/// <summary>Registers/unregisters the app in the per-user Run key so it starts at login.
/// No admin rights needed (unlike a Task Scheduler "run whether logged on or not" task),
/// which matters since this has to work unattended on a normal login.</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XboxLARP";

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the running executable's path");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return value is not null && value.Trim('"') == ExePath;
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{ExePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
