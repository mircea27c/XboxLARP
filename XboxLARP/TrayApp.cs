using System.Diagnostics;
using MonitorTopology;

namespace XboxLARP;

/// <summary>System tray icon + context menu - the GUI surface for everything that used to
/// require typing a CLI command. Runs on the WinForms UI thread; the controller listener runs
/// on a separate background thread and both drive the same GameModeOrchestrator/TopologyService
/// instances, so a tray click and a controller chord behave identically.</summary>
public sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly GameModeOrchestrator _orchestrator;
    private readonly TopologyService _topology;
    private readonly MonitorConfig _monitorConfig;
    private readonly AppConfig _appConfig;
    private readonly ILogSink _log;
    private readonly SharedStatus _status;
    private MainForm? _mainForm;

    public event Action? ExitRequested;

    public TrayApp(GameModeOrchestrator orchestrator, TopologyService topology, MonitorConfig monitorConfig, AppConfig appConfig, ILogSink log, SharedStatus status)
    {
        _orchestrator = orchestrator;
        _topology = topology;
        _monitorConfig = monitorConfig;
        _appConfig = appConfig;
        _log = log;
        _status = status;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open XboxLARP", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Enter Game Mode (switch + launch Playnite)", null, (_, _) => RunAsync(() => _orchestrator.EnterGameMode()));
        menu.Items.Add("Close Game Mode (close Playnite + restore)", null, (_, _) => RunAsync(() => _orchestrator.CloseGameMode()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Switch to Game Monitor Only (no Playnite)", null, (_, _) => RunAsync(() => ApplyProfileOnly(Paths.GameOnlyProfile)));
        menu.Items.Add("Switch to Normal (no Playnite)", null, (_, _) => RunAsync(() => ApplyProfileOnly(Paths.NormalProfile)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("View Controls...", null, (_, _) => ShowBindingsForm());
        menu.Items.Add("Open Logs Folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());

        var runAtLoginItem = new ToolStripMenuItem("Run at Login") { Checked = SafeIsStartupEnabled() };
        runAtLoginItem.Click += (_, _) =>
        {
            try
            {
                if (runAtLoginItem.Checked) StartupManager.Disable();
                else StartupManager.Enable();
                runAtLoginItem.Checked = StartupManager.IsEnabled();
                _log.Info($"Run at Login set to {runAtLoginItem.Checked}");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to change Run at Login: {ex.Message}");
                ShowBalloon("Run at Login", $"Failed to change: {ex.Message}", ToolTipIcon.Error);
            }
        };
        menu.Items.Add(runAtLoginItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "XboxLARP",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMainForm();
        };
    }

    private void ShowMainForm()
    {
        if (_mainForm is null || _mainForm.IsDisposed)
            _mainForm = new MainForm(_orchestrator, _topology, _monitorConfig, _appConfig, _log, _status);

        if (!_mainForm.Visible)
            _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void ShowBindingsForm()
    {
        var controllerConfig = ControllerConfig.LoadOrCreateDefault(Paths.ControllerConfigPath);
        var form = new BindingsForm(controllerConfig);
        form.Show(); // non-modal - Form disposes itself on Close by default, no `using` here
    }

    private void ApplyProfileOnly(string profileName)
    {
        var result = _topology.ApplyProfile(_monitorConfig, profileName);
        _log.Info(result.Success ? $"Tray: applied '{profileName}'" : $"Tray: FAILED to apply '{profileName}': {result.Message}");
        if (!result.Success)
            ShowBalloon($"Failed to apply '{profileName}'", result.Message, ToolTipIcon.Error);
    }

    private static void RunAsync(Action action) => Task.Run(action);

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupManager.IsEnabled(); }
        catch { return false; }
    }

    private void OpenLogsFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(Paths.LogPath)}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Error($"Could not open logs folder: {ex.Message}"); }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try { _notifyIcon.ShowBalloonTip(4000, title, text, icon); }
        catch { /* balloon tips are best-effort */ }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_mainForm is { IsDisposed: false })
            _mainForm.Dispose();
    }
}
