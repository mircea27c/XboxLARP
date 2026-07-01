namespace XboxLARP;

/// <summary>Single source of truth for the app's icon everywhere it appears (exe/taskbar,
/// tray, window title bars). Pulls it straight back out of the running exe's own embedded
/// resource (set via &lt;ApplicationIcon&gt; in the csproj) rather than shipping/loading a
/// separate icon file at runtime - one asset, one place it's set.</summary>
public static class AppIcon
{
    private static Icon? _cached;

    public static Icon Get()
    {
        if (_cached is not null)
            return _cached;

        try
        {
            var path = Environment.ProcessPath;
            _cached = path is not null ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch
        {
            _cached = null;
        }

        return _cached ??= SystemIcons.Application;
    }
}
