using System.Diagnostics;
using MonitorTopology;

namespace XboxLARP;

/// <summary>
/// The single source of truth for entering/leaving game mode. Both the controller's
/// Close-Game-Mode chord and Playnite exiting on its own (normal close, crash, or the user
/// picking Shutdown from Playnite's own menu) end up going through the same
/// WaitForExitAndRestore path - so "close via combo" and "just close it" behave identically,
/// and the Normal-profile restore only ever has one code path to get right.
/// </summary>
public sealed class GameModeOrchestrator
{
    private readonly TopologyService _topology;
    private readonly MonitorConfig _monitorConfig;
    private readonly AppConfig _appConfig;
    private readonly ILogSink _log;
    private readonly object _gate = new();
    private Process? _playniteProcess;

    public GameModeOrchestrator(TopologyService topology, MonitorConfig monitorConfig, AppConfig appConfig, ILogSink log)
    {
        _topology = topology;
        _monitorConfig = monitorConfig;
        _appConfig = appConfig;
        _log = log;
    }

    public bool InGameMode
    {
        get { lock (_gate) return _playniteProcess is { HasExited: false }; }
    }

    public void EnterGameMode()
    {
        lock (_gate)
        {
            if (_playniteProcess is { HasExited: false })
            {
                _log.Info("EnterGameMode: already in game mode, ignoring");
                return;
            }

            if (string.IsNullOrWhiteSpace(_appConfig.PlaynitePath) || !File.Exists(_appConfig.PlaynitePath))
            {
                _log.Error($"EnterGameMode: Playnite path not configured or not found ('{_appConfig.PlaynitePath}'). Run --set-playnite-path <path-to-Playnite.FullscreenApp.exe>.");
                return;
            }

            var result = _topology.ApplyProfile(_monitorConfig, Paths.GameOnlyProfile);
            if (!result.Success)
            {
                _log.Error($"EnterGameMode: aborting, failed to switch to '{Paths.GameOnlyProfile}' profile: {result.Message}");
                return;
            }

            Thread.Sleep(_appConfig.PreLaunchSettleDelayMs);

            Process proc;
            try
            {
                proc = Process.Start(new ProcessStartInfo(_appConfig.PlaynitePath)
                {
                    WorkingDirectory = Path.GetDirectoryName(_appConfig.PlaynitePath),
                    UseShellExecute = true,
                })!;
            }
            catch (Exception ex)
            {
                _log.Error($"EnterGameMode: failed to launch Playnite ({ex.Message}) - restoring '{Paths.NormalProfile}' profile");
                _topology.ApplyProfile(_monitorConfig, Paths.NormalProfile);
                return;
            }

            _playniteProcess = proc;
            _log.Info($"EnterGameMode: launched Playnite (pid {proc.Id}), watching for exit");
            _ = Task.Run(() => WaitForExitAndRestore(proc));
        }
    }

    public void CloseGameMode()
    {
        Process? proc;
        lock (_gate) proc = _playniteProcess;

        if (proc is null || proc.HasExited)
        {
            _log.Info("CloseGameMode: not currently in game mode, ignoring");
            return;
        }

        _log.Info("CloseGameMode: requesting graceful close");
        bool closedWindow = false;
        try { closedWindow = proc.CloseMainWindow(); }
        catch (Exception ex) { _log.Warn($"CloseGameMode: CloseMainWindow failed: {ex.Message}"); }

        bool exited = closedWindow && proc.WaitForExit(_appConfig.GracefulCloseTimeoutMs);
        if (!exited)
        {
            _log.Warn("CloseGameMode: graceful close did not complete in time, killing process tree");
            try { proc.Kill(entireProcessTree: true); }
            catch (Exception ex) { _log.Error($"CloseGameMode: Kill failed: {ex.Message}"); }
        }
        // The restore to Normal profile happens in WaitForExitAndRestore, already watching
        // this process since EnterGameMode - not duplicated here.
    }

    private static readonly string[] PlayniteProcessNames = ["Playnite.FullscreenApp", "Playnite.DesktopApp"];

    private void WaitForExitAndRestore(Process proc)
    {
        Process current = proc;
        while (true)
        {
            try { current.WaitForExit(); }
            catch (Exception ex) { _log.Warn($"WaitForExitAndRestore: WaitForExit error: {ex.Message}"); break; }

            // Playnite.FullscreenApp.exe is a launcher stub: on a machine with Playnite already
            // installed it can hand off to a relaunched/existing process and exit itself within
            // well under a second. If a real Playnite process is still around right after ours
            // exits, that's the hand-off target, not a real close - keep watching it instead of
            // restoring the monitors out from under a Playnite session that's still running.
            Thread.Sleep(750);
            var replacement = FindRunningPlayniteProcess(excludePid: current.Id);
            if (replacement is null)
                break;

            _log.Info($"Playnite process {current.Id} exited but {replacement.ProcessName} (pid {replacement.Id}) is still running - continuing to watch it instead of restoring yet");
            lock (_gate) _playniteProcess = replacement;
            current = replacement;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_playniteProcess, current))
                _playniteProcess = null;
        }

        _log.Info("Playnite exited - restoring Normal profile");
        var result = _topology.ApplyProfile(_monitorConfig, Paths.NormalProfile);
        if (!result.Success)
            _log.Error($"Restore to '{Paths.NormalProfile}' profile FAILED after Playnite exit: {result.Message}");
    }

    private static Process? FindRunningPlayniteProcess(int excludePid) =>
        PlayniteProcessNames
            .SelectMany(Process.GetProcessesByName)
            .FirstOrDefault(p => p.Id != excludePid && !p.HasExited);
}
