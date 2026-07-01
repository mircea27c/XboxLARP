using MonitorTopology;

namespace XboxLARP;

/// <summary>GUI replacement for --setup-monitors / --capture-profile: label detected
/// monitors, pick the game monitor, and capture the two profiles - all without a
/// terminal. Mutates the same MonitorConfig instance the rest of the app holds, so changes
/// take effect immediately without restarting.</summary>
public sealed class MonitorsForm : Form
{
    private const int Pad = 20;
    private const int FormWidth = 560;
    private const int RowHeight = 48;

    private readonly TopologyService _topology;
    private readonly MonitorConfig _monitorConfig;
    private readonly ILogSink _log;
    private readonly List<LiveMonitor> _live;
    private readonly List<RadioButton> _gameRadios = new();

    public MonitorsForm(TopologyService topology, MonitorConfig monitorConfig, ILogSink log)
    {
        _topology = topology;
        _monitorConfig = monitorConfig;
        _log = log;
        _live = _topology.QueryTopology();

        Text = "Manage Monitors";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        BuildUi();
    }

    private void BuildUi()
    {
        int contentWidth = FormWidth - 2 * Pad;
        int y = Pad;

        var info = new Label
        {
            Text = "Pick which single monitor is the dedicated game monitor. Every other detected monitor is treated as a normal monitor automatically.",
            Left = Pad,
            Top = y,
            Width = contentWidth,
            Height = 40,
        };
        Controls.Add(info);
        y += info.Height + 10;

        foreach (var m in _live)
        {
            var existing = _monitorConfig.FindByIdentity(m.Identity);

            var nameLabel = new Label
            {
                Text = $"{m.FriendlyName} ({(m.Active ? "active" : "inactive")}, {m.Identity.OutputTechnology})",
                Left = Pad,
                Top = y + 4,
                Width = 340,
                Height = 24,
                AutoEllipsis = true,
            };
            Controls.Add(nameLabel);

            var gameRadio = new RadioButton
            {
                Text = "Game monitor",
                AutoSize = true,
                Left = Pad + 350,
                Top = y + 2,
                Checked = existing is not null && existing.Role == _monitorConfig.GameMonitorRole,
                Tag = m,
            };
            Controls.Add(gameRadio);
            _gameRadios.Add(gameRadio);

            y += RowHeight;
        }

        y += 10;
        var saveButton = new Button { Text = "Save", Left = Pad, Top = y, Width = 180, Height = 38 };
        saveButton.Click += (_, _) => SaveRoles();
        Controls.Add(saveButton);
        y += saveButton.Height + 26;

        var captureLabel = new Label
        {
            Text = "After saving roles: arrange monitors as desired via Windows Display Settings, then capture that layout.",
            Left = Pad,
            Top = y,
            Width = contentWidth,
            Height = 40,
        };
        Controls.Add(captureLabel);
        y += captureLabel.Height + 6;

        int captureButtonWidth = (contentWidth - 20) / 2;
        var captureNormal = new Button { Text = $"Capture Current Layout as '{Paths.NormalProfile}'", Left = Pad, Top = y, Width = captureButtonWidth, Height = 40 };
        captureNormal.Click += (_, _) => CaptureProfile(Paths.NormalProfile);
        Controls.Add(captureNormal);

        var captureGameOnly = new Button { Text = $"Capture Current Layout as '{Paths.GameOnlyProfile}'", Left = Pad + captureButtonWidth + 20, Top = y, Width = captureButtonWidth, Height = 40 };
        captureGameOnly.Click += (_, _) => CaptureProfile(Paths.GameOnlyProfile);
        Controls.Add(captureGameOnly);
        y += captureGameOnly.Height + 26;

        var activateLabel = new Label
        {
            Text = "Or turn ONE monitor on alone at its default timing (no need to touch Display Settings):",
            Left = Pad,
            Top = y,
            Width = contentWidth,
            Height = 26,
        };
        Controls.Add(activateLabel);
        y += activateLabel.Height + 8;

        foreach (var m in _live)
        {
            var existing = _monitorConfig.FindByIdentity(m.Identity);
            if (existing is null) continue;
            var btn = new Button { Text = $"Activate '{existing.Role}' Alone", Left = Pad, Top = y, Width = 260, Height = 36, Tag = existing.Role };
            btn.Click += (_, _) => ActivatePreferred((string)btn.Tag!);
            Controls.Add(btn);
            y += btn.Height + 12;
        }

        y += Pad;
        ClientSize = new Size(FormWidth, y);
    }

    private void SaveRoles()
    {
        if (!_gameRadios.Any(r => r.Checked))
        {
            MessageBox.Show(this, "Pick which monitor is the game monitor.", "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Roles are just a stable internal identifier per monitor now (derived from the
        // EDID friendly name, deduplicated if two monitors happen to share one) - the user
        // only ever needs to choose ONE thing: which monitor is the game monitor.
        string? gameRole = null;
        var newLabels = new List<MonitorLabel>();
        var usedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _live.Count; i++)
        {
            var m = _live[i];
            string baseRole = string.IsNullOrWhiteSpace(m.FriendlyName) ? $"Monitor{i + 1}" : m.FriendlyName.Trim();
            string role = baseRole;
            int suffix = 2;
            while (!usedRoles.Add(role))
                role = $"{baseRole} ({suffix++})";

            newLabels.Add(new MonitorLabel
            {
                Role = role,
                EdidValid = m.Identity.EdidValid,
                EdidManufactureId = m.Identity.EdidManufactureId,
                EdidProductCodeId = m.Identity.EdidProductCodeId,
                OutputTechnology = m.Identity.OutputTechnology.ToString(),
                ConnectorInstance = m.Identity.ConnectorInstance,
                FriendlyName = m.FriendlyName,
            });
            if (_gameRadios[i].Checked)
                gameRole = role;
        }

        _monitorConfig.Monitors = newLabels;
        _monitorConfig.GameMonitorRole = gameRole!;
        ConfigStore.Save(Paths.MonitorsConfigPath, _monitorConfig);
        _log.Info($"Monitor roles saved via GUI. Game monitor: {gameRole}");
        MessageBox.Show(this, "Saved.", "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CaptureProfile(string profileName)
    {
        try
        {
            var captured = _topology.CaptureProfile(_monitorConfig, profileName);
            _monitorConfig.Profiles.RemoveAll(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
            _monitorConfig.Profiles.Add(captured);
            ConfigStore.Save(Paths.MonitorsConfigPath, _monitorConfig);
            _log.Info($"Captured profile '{profileName}' via GUI with {captured.ActiveMonitors.Count} monitor(s)");
            MessageBox.Show(this, $"Captured '{profileName}' with {captured.ActiveMonitors.Count} active monitor(s).", "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Error($"Capture '{profileName}' via GUI failed: {ex.Message}");
            MessageBox.Show(this, $"Failed: {ex.Message}", "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ActivatePreferred(string role)
    {
        try
        {
            var profile = _topology.BuildPreferredModeProfile(_monitorConfig, role);
            var result = _topology.ApplyDisplayProfile(_monitorConfig, profile);
            _log.Info(result.Success ? $"Activated '{role}' alone via GUI" : $"Failed to activate '{role}' via GUI: {result.Message}");
            if (!result.Success)
                MessageBox.Show(this, result.Message, "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Manage Monitors", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
