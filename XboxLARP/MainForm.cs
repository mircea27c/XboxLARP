using MonitorTopology;

namespace XboxLARP;

public sealed class MainForm : Form
{
    private const int Pad = 20;
    private const int GroupContentWidth = 560 - 2 * Pad;

    private readonly GameModeOrchestrator _orchestrator;
    private readonly TopologyService _topology;
    private readonly MonitorConfig _monitorConfig;
    private readonly AppConfig _appConfig;
    private readonly ILogSink _log;
    private readonly SharedStatus _status;

    private Label _gameModeLabel = null!;
    private Label _controllerLabel = null!;
    private CheckBox _runAtLoginBox = null!;
    private TextBox _playnitePathBox = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    public MainForm(GameModeOrchestrator orchestrator, TopologyService topology, MonitorConfig monitorConfig, AppConfig appConfig, ILogSink log, SharedStatus status)
    {
        _orchestrator = orchestrator;
        _topology = topology;
        _monitorConfig = monitorConfig;
        _appConfig = appConfig;
        _log = log;
        _status = status;

        Text = "XboxLARP";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        BuildUi();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();
        RefreshStatus();

        FormClosing += (_, e) =>
        {
            // Closing the window just hides it - the tray icon keeps running.
            e.Cancel = true;
            Hide();
        };
    }

    private void BuildUi()
    {
        int y = Pad;

        // ---- Status ----
        var statusGroup = new GroupBox { Text = "Status", Left = Pad, Top = y, Width = GroupContentWidth, Height = 100 };
        _gameModeLabel = new Label { Left = 18, Top = 30, Width = GroupContentWidth - 36, Height = 26, Text = "Game Mode: ?", AutoEllipsis = true };
        _controllerLabel = new Label { Left = 18, Top = 60, Width = GroupContentWidth - 36, Height = 26, Text = "Controller: ?", AutoEllipsis = true };
        statusGroup.Controls.Add(_gameModeLabel);
        statusGroup.Controls.Add(_controllerLabel);
        Controls.Add(statusGroup);
        y += statusGroup.Height + Pad;

        // ---- Actions ----
        const int buttonHeight = 40;
        const int buttonGap = 16;
        int buttonWidth = (GroupContentWidth - 36 - buttonGap) / 2;

        var actionsGroup = new GroupBox { Text = "Actions", Left = Pad, Top = y, Width = GroupContentWidth, Height = 150 };
        var enterBtn = new Button { Text = "Enter Game Mode", Left = 18, Top = 30, Width = buttonWidth, Height = buttonHeight };
        enterBtn.Click += (_, _) => RunAsync(() => _orchestrator.EnterGameMode());
        var closeBtn = new Button { Text = "Close Game Mode", Left = 18 + buttonWidth + buttonGap, Top = 30, Width = buttonWidth, Height = buttonHeight };
        closeBtn.Click += (_, _) => RunAsync(() => _orchestrator.CloseGameMode());
        var gameOnlyBtn = new Button { Text = "Game Monitor Only", Left = 18, Top = 30 + buttonHeight + 14, Width = buttonWidth, Height = buttonHeight };
        gameOnlyBtn.Click += (_, _) => RunAsync(() => ApplyProfileOnly(Paths.GameOnlyProfile));
        var normalBtn = new Button { Text = "Switch to Normal", Left = 18 + buttonWidth + buttonGap, Top = 30 + buttonHeight + 14, Width = buttonWidth, Height = buttonHeight };
        normalBtn.Click += (_, _) => RunAsync(() => ApplyProfileOnly(Paths.NormalProfile));
        actionsGroup.Controls.Add(enterBtn);
        actionsGroup.Controls.Add(closeBtn);
        actionsGroup.Controls.Add(gameOnlyBtn);
        actionsGroup.Controls.Add(normalBtn);
        Controls.Add(actionsGroup);
        y += actionsGroup.Height + Pad;

        // ---- Manage Monitors / Controller Layout ----
        int halfWidth = (GroupContentWidth - 16) / 2;
        var manageMonitorsBtn = new Button { Text = "Manage Monitors...", Left = Pad, Top = y, Width = halfWidth, Height = 40 };
        manageMonitorsBtn.Click += (_, _) => OpenMonitorsForm();
        Controls.Add(manageMonitorsBtn);
        var viewControlsBtn = new Button { Text = "View Controls...", Left = Pad + halfWidth + 16, Top = y, Width = halfWidth, Height = 40 };
        viewControlsBtn.Click += (_, _) => OpenBindingsForm();
        Controls.Add(viewControlsBtn);
        y += manageMonitorsBtn.Height + Pad;

        // ---- Settings ----
        var settingsGroup = new GroupBox { Text = "Settings", Left = Pad, Top = y, Width = GroupContentWidth, Height = 160 };
        var playniteLabel = new Label { Left = 18, Top = 30, Width = GroupContentWidth - 36, Height = 22, Text = "Playnite Fullscreen path:" };
        _playnitePathBox = new TextBox { Left = 18, Top = 56, Width = GroupContentWidth - 36 - 110, Height = 28, Text = _appConfig.PlaynitePath };
        var browseBtn = new Button { Left = 18 + (GroupContentWidth - 36 - 110) + 10, Top = 55, Width = 100, Height = 30, Text = "Browse..." };
        browseBtn.Click += (_, _) => BrowsePlaynitePath();
        _runAtLoginBox = new CheckBox { Left = 18, Top = 100, AutoSize = true, Text = "Run at Login", Checked = SafeIsStartupEnabled() };
        _runAtLoginBox.CheckedChanged += (_, _) => ToggleRunAtLogin();
        settingsGroup.Controls.Add(playniteLabel);
        settingsGroup.Controls.Add(_playnitePathBox);
        settingsGroup.Controls.Add(browseBtn);
        settingsGroup.Controls.Add(_runAtLoginBox);
        Controls.Add(settingsGroup);
        y += settingsGroup.Height + Pad;

        ClientSize = new Size(560, y);
    }

    private void RefreshStatus()
    {
        _gameModeLabel.Text = $"Game Mode: {(_orchestrator.InGameMode ? "Active" : "Inactive")}";
        _controllerLabel.Text = _status.ControllerConnected
            ? $"Controller: Connected ({_status.ControllerName ?? "unknown model"})"
            : "Controller: Not connected";
    }

    private void ApplyProfileOnly(string profileName)
    {
        var result = _topology.ApplyProfile(_monitorConfig, profileName);
        _log.Info(result.Success ? $"GUI: applied '{profileName}'" : $"GUI: FAILED to apply '{profileName}': {result.Message}");
        if (!result.Success)
            Invoke(() => MessageBox.Show(this, result.Message, "Apply Profile Failed", MessageBoxButtons.OK, MessageBoxIcon.Error));
    }

    private static void RunAsync(Action action) => Task.Run(action);

    private void OpenMonitorsForm()
    {
        using var form = new MonitorsForm(_topology, _monitorConfig, _log);
        form.ShowDialog(this);
    }

    private void OpenBindingsForm()
    {
        var controllerConfig = ControllerConfig.LoadOrCreateDefault(Paths.ControllerConfigPath);
        using var form = new BindingsForm(controllerConfig);
        form.ShowDialog(this);
    }

    private void BrowsePlaynitePath()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Playnite Fullscreen (Playnite.FullscreenApp.exe)|Playnite.FullscreenApp.exe|All executables (*.exe)|*.exe",
            Title = "Locate Playnite.FullscreenApp.exe",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _playnitePathBox.Text = dialog.FileName;
            _appConfig.PlaynitePath = dialog.FileName;
            AppConfig.Save(Paths.AppConfigPath, _appConfig);
            _log.Info($"Playnite path set via GUI: {dialog.FileName}");
        }
    }

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupManager.IsEnabled(); }
        catch { return false; }
    }

    private void ToggleRunAtLogin()
    {
        try
        {
            if (_runAtLoginBox.Checked) StartupManager.Enable();
            else StartupManager.Disable();
            _log.Info($"Run at Login set to {_runAtLoginBox.Checked} via GUI");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to change Run at Login: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Run at Login", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
