namespace XboxLARP;

/// <summary>Tiny cross-thread status flag the listener thread updates and the GUI thread
/// polls - avoids the GUI needing any direct access to the ControllerPoller itself.</summary>
public sealed class SharedStatus
{
    public volatile bool ControllerConnected;
    public volatile string? ControllerName;
}
