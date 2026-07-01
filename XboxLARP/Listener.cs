using MonitorTopology;

namespace XboxLARP;

public static class Listener
{
    /// <summary>Shared poll loop. The action handler is the only thing that differs between
    /// --dry-run (log only) and real use (actually do things) - keeps the one risky bit (real
    /// side effects) isolated from the polling/chord-detection logic being exercised either way.
    /// Stops on Ctrl+C (console modes) or when cancellationToken is cancelled (tray mode, where
    /// there's no console to Ctrl+C).</summary>
    public static void RunLoop(ControllerConfig config, ILogSink log, Action<ActionType, string> onAction, Action<XButton> onEveryTick, CancellationToken cancellationToken = default, Action<bool>? onConnectionChanged = null)
    {
        var poller = new ControllerPoller();
        var router = new ActionRouter(config, log, onAction);
        bool ctrlCPressed = false;
        bool wasConnected = false;

        try { Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctrlCPressed = true; }; }
        catch { /* no console attached (tray mode) - cancellationToken is the only stop signal */ }

        int intervalMs = Math.Max(1, 1000 / Math.Max(1, config.PollHz));
        while (!ctrlCPressed && !cancellationToken.IsCancellationRequested)
        {
            bool ok = poller.Poll();
            if (ok && !wasConnected)
            {
                log.Info("Controller connected");
                wasConnected = true;
                onConnectionChanged?.Invoke(true);
            }
            else if (!ok && wasConnected)
            {
                log.Warn("Controller disconnected");
                wasConnected = false;
                onConnectionChanged?.Invoke(false);
            }

            if (ok)
            {
                router.Process(poller.CurrentHeld, poller.PressedThisTick);
                onEveryTick(poller.ReleasedThisTick);
            }

            Thread.Sleep(intervalMs);
        }

        log.Info("Listener stopped");
    }
}
