using MonitorTopology;

namespace XboxLARP;

static class Commands
{
    public static int Help()
    {
        Console.WriteLine("""
            XboxLARP - config-driven monitor profile switcher

            Normal use: just run the exe with no arguments. It sits in the system tray -
            right-click for Enter/Close Game Mode and manual Normal/GameOnly switching.
            Everything below is for one-time setup and diagnostics.

            Setup (run once, in order):
              --setup-monitors             detect connected monitors, assign role names
              --capture-profile <name>     snapshot the CURRENT live monitor layout into a named profile
                                            (arrange monitors via Windows Display Settings first, then run this)
              --activate-preferred <role>  turn ONLY this monitor on at its EDID-preferred timing, fully
                                            programmatically - useful to bootstrap a monitor that's never
                                            been active before, without touching Windows Display Settings

            Everyday use / diagnostics:
              --query                      print the current live monitor topology (read-only)
              --apply-profile <name>       apply a previously captured profile now
              --stress-test <count>        repeatedly alternate Normal/GameOnly profiles, report pass/fail
              --dry-run                    listen to the controller and log recognized chords only -
                                            no keys sent, no monitors/Playnite touched. Safe to test with.
              --run                        listen for real: sends nav keys, applies monitor profiles,
                                            launches/closes Playnite
              --set-playnite-path <path>   point at your Playnite.FullscreenApp.exe

            Config and logs live under: %LOCALAPPDATA%\XboxLARP
            """);
        return 0;
    }

    public static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run --help for usage.");
        return 1;
    }

    public static int Query(ILogSink log)
    {
        var svc = new TopologyService(log);
        var monitors = svc.QueryTopology();
        if (monitors.Count == 0)
        {
            Console.WriteLine("No monitors detected.");
            return 0;
        }

        Console.WriteLine($"{monitors.Count} monitor(s) detected:\n");
        foreach (var m in monitors)
        {
            Console.WriteLine($"  {m.FriendlyName}");
            Console.WriteLine($"    Active:   {m.Active}{(m.IsPrimary ? "  (PRIMARY - at position 0,0)" : "")}");
            Console.WriteLine($"    Identity: {m.Identity.StableKey}");
            Console.WriteLine($"    Adapter:  {m.AdapterId}  target={m.TargetId} source={m.SourceId}");
            Console.WriteLine();
        }
        return 0;
    }

    public static int SetupMonitors(ILogSink log)
    {
        var svc = new TopologyService(log);
        var config = ConfigStore.Load(Paths.MonitorsConfigPath);
        var live = svc.QueryTopology();

        if (live.Count == 0)
        {
            Console.WriteLine("No monitors detected - nothing to label.");
            return 1;
        }

        Console.WriteLine($"Detected {live.Count} monitor(s). For each one, enter a short role name");
        Console.WriteLine("(letters/numbers, e.g. GameMonitor, LeftMonitor, RightMonitor).");
        Console.WriteLine("Press Enter with no text to keep the existing label, or to skip an unlabeled one.\n");

        var newLabels = new List<MonitorLabel>();
        var usedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in live)
        {
            var existing = config.FindByIdentity(m.Identity);
            string prompt = $"{m.FriendlyName} [{(m.Active ? "active" : "inactive")}, {m.Identity.StableKey}]";
            if (!m.Identity.EdidValid)
                Console.WriteLine($"  ! Warning: no valid EDID for this monitor - it will be matched by output port only, which is less reliable if you move cables.");

            Console.Write($"{prompt}\n  Role" + (existing is not null ? $" [{existing.Role}]" : "") + ": ");
            string? input = Console.ReadLine()?.Trim();

            string role = string.IsNullOrEmpty(input) ? existing?.Role ?? "" : input;
            if (string.IsNullOrEmpty(role))
            {
                Console.WriteLine("  (skipped)\n");
                continue;
            }
            if (!usedRoles.Add(role))
            {
                Console.WriteLine($"  Role '{role}' was already used this run - skipping duplicate.\n");
                continue;
            }

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
            Console.WriteLine();
        }

        if (newLabels.Count == 0)
        {
            Console.WriteLine("No roles assigned - config not changed.");
            return 1;
        }

        string defaultGameRole = config.GameMonitorRole;
        Console.Write($"Which role is the dedicated GAME monitor?" + (defaultGameRole != "" ? $" [{defaultGameRole}]" : "") + ": ");
        string? gameRoleInput = Console.ReadLine()?.Trim();
        string gameRole = string.IsNullOrEmpty(gameRoleInput) ? defaultGameRole : gameRoleInput;

        if (!newLabels.Any(l => string.Equals(l.Role, gameRole, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"'{gameRole}' does not match any role you just assigned. Setup aborted, config not changed.");
            return 1;
        }

        config.Monitors = newLabels;
        config.GameMonitorRole = gameRole;
        ConfigStore.Save(Paths.MonitorsConfigPath, config);

        Console.WriteLine($"\nSaved {newLabels.Count} monitor label(s) to {Paths.MonitorsConfigPath}");
        Console.WriteLine($"Game monitor role: {gameRole}");
        Console.WriteLine("\nNext: arrange your monitors normally (game monitor OFF, others ON) in Windows Display");
        Console.WriteLine($"Settings, then run: XboxLARP --capture-profile {Paths.NormalProfile}");
        Console.WriteLine("Then turn off all but the game monitor and run:");
        Console.WriteLine($"  XboxLARP --capture-profile {Paths.GameOnlyProfile}");
        return 0;
    }

    public static int CaptureProfile(string profileName, ILogSink log)
    {
        var config = ConfigStore.Load(Paths.MonitorsConfigPath);
        if (config.Monitors.Count == 0)
        {
            Console.Error.WriteLine("No monitors labelled yet - run --setup-monitors first.");
            return 1;
        }

        var svc = new TopologyService(log);
        DisplayProfile captured;
        try
        {
            captured = svc.CaptureProfile(config, profileName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Capture failed: {ex.Message}");
            return 1;
        }

        config.Profiles.RemoveAll(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        config.Profiles.Add(captured);
        ConfigStore.Save(Paths.MonitorsConfigPath, config);

        Console.WriteLine($"Captured profile '{profileName}' with {captured.ActiveMonitors.Count} active monitor(s):");
        foreach (var m in captured.ActiveMonitors)
            Console.WriteLine($"  {m.Role}: {m.SourceMode.Width}x{m.SourceMode.Height} @ ({m.SourceMode.PositionX},{m.SourceMode.PositionY}) refresh {m.RefreshNumerator}/{m.RefreshDenominator}");
        return 0;
    }

    public static int ApplyProfile(string profileName, ILogSink log)
    {
        var config = ConfigStore.Load(Paths.MonitorsConfigPath);
        var svc = new TopologyService(log);
        var result = svc.ApplyProfile(config, profileName);

        Console.WriteLine(result.Success
            ? $"Applied '{profileName}' successfully (attempt {result.Attempts})."
            : $"FAILED to apply '{profileName}' after {result.Attempts} attempt(s): {result.Message}");
        return result.Success ? 0 : 1;
    }

    public static int ActivatePreferred(string role, ILogSink log)
    {
        var config = ConfigStore.Load(Paths.MonitorsConfigPath);
        var svc = new TopologyService(log);

        DisplayProfile bootstrap;
        try
        {
            bootstrap = svc.BuildPreferredModeProfile(config, role);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not build a preferred-mode profile for '{role}': {ex.Message}");
            return 1;
        }

        var result = svc.ApplyDisplayProfile(config, bootstrap);
        Console.WriteLine(result.Success
            ? $"Activated '{role}' alone at its preferred mode (attempt {result.Attempts}). You can now arrange it further via Windows Display Settings if needed, then --capture-profile."
            : $"FAILED to activate '{role}' after {result.Attempts} attempt(s): {result.Message}");
        return result.Success ? 0 : 1;
    }

    public static int DryRun(ILogSink log)
    {
        var config = ControllerConfig.LoadOrCreateDefault(Paths.ControllerConfigPath);
        Console.WriteLine($"Dry run: bindings loaded from {Paths.ControllerConfigPath}");
        Console.WriteLine("Chords will be logged only - no keys sent, no monitors/Playnite touched.\n");

        Listener.RunLoop(config, log,
            onAction: (_, _) => { }, // ActionRouter already logs "Chord fired: ..." for every match
            onEveryTick: _ => { });
        return 0;
    }

    public static int Run(ILogSink log)
    {
        var controllerConfig = ControllerConfig.LoadOrCreateDefault(Paths.ControllerConfigPath);
        var appConfig = AppConfig.LoadOrCreateDefault(Paths.AppConfigPath);
        var monitorConfig = ConfigStore.Load(Paths.MonitorsConfigPath);
        var nav = new NavSender(log);
        var topology = new TopologyService(log);
        var orchestrator = new GameModeOrchestrator(topology, monitorConfig, appConfig, log);

        if (string.IsNullOrWhiteSpace(appConfig.PlaynitePath) || !File.Exists(appConfig.PlaynitePath))
            log.Warn($"Playnite path not set/found ('{appConfig.PlaynitePath}') - EnterGameMode will fail until you run --set-playnite-path <path>");

        Console.WriteLine($"Running: bindings loaded from {Paths.ControllerConfigPath}");

        // Startup safety net: whatever state the last session left displays in (crash, direct
        // PC shutdown from Playnite's own menu, etc.), force Normal profile before listening.
        if (monitorConfig.Profiles.Count > 0)
        {
            var startupResult = topology.ApplyProfile(monitorConfig, Paths.NormalProfile);
            log.Info(startupResult.Success
                ? "Startup safety net: applied Normal profile"
                : $"Startup safety net: FAILED to apply Normal profile: {startupResult.Message}");
        }

        Listener.RunLoop(controllerConfig, log,
            onAction: (action, _) => Dispatch(action, nav, orchestrator, log),
            onEveryTick: released =>
            {
                if ((released & XButton.Guide) != 0)
                    nav.ReleaseAltTabIfEngaged();
            });
        return 0;
    }

    private static void Dispatch(ActionType action, NavSender nav, GameModeOrchestrator orchestrator, ILogSink log)
    {
        switch (action)
        {
            case ActionType.NavUp: nav.Up(); break;
            case ActionType.NavDown: nav.Down(); break;
            case ActionType.NavLeft: nav.Left(); break;
            case ActionType.NavRight: nav.Right(); break;
            case ActionType.NavEnter: nav.Enter(); break;
            case ActionType.NavEsc: nav.Esc(); break;
            case ActionType.NavAltTabForward: nav.AltTabForward(); break;
            case ActionType.NavAltTabBackward: nav.AltTabBackward(); break;
            case ActionType.EnterGameMode: orchestrator.EnterGameMode(); break;
            case ActionType.CloseGameMode: orchestrator.CloseGameMode(); break;
        }
    }

    private static Mutex? _singleInstanceMutex;

    public static int Tray(ILogSink log)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\XboxLARP_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("XboxLARP is already running - check your system tray.", "XboxLARP");
            return 1;
        }

        ConsoleWindow.Hide();

        try
        {
            if (!StartupManager.IsEnabled())
            {
                StartupManager.Enable();
                log.Info("Run at Login was not set - enabled it by default");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Could not enable Run at Login automatically: {ex.Message}");
        }

        var controllerConfig = ControllerConfig.LoadOrCreateDefault(Paths.ControllerConfigPath);
        var appConfig = AppConfig.LoadOrCreateDefault(Paths.AppConfigPath);
        var monitorConfig = ConfigStore.Load(Paths.MonitorsConfigPath);
        var nav = new NavSender(log);
        var topology = new TopologyService(log);
        var orchestrator = new GameModeOrchestrator(topology, monitorConfig, appConfig, log);

        if (string.IsNullOrWhiteSpace(appConfig.PlaynitePath) || !File.Exists(appConfig.PlaynitePath))
            log.Warn($"Playnite path not set/found ('{appConfig.PlaynitePath}') - EnterGameMode will fail until --set-playnite-path is run");

        if (monitorConfig.Profiles.Count > 0)
        {
            var startupResult = topology.ApplyProfile(monitorConfig, Paths.NormalProfile);
            log.Info(startupResult.Success
                ? "Startup safety net: applied Normal profile"
                : $"Startup safety net: FAILED to apply Normal profile: {startupResult.Message}");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var status = new SharedStatus();
        using var tray = new TrayApp(orchestrator, topology, monitorConfig, appConfig, log, status);
        var cts = new CancellationTokenSource();
        var listenerThread = new Thread(() => Listener.RunLoop(controllerConfig, log,
            onAction: (action, _) => Dispatch(action, nav, orchestrator, log),
            onEveryTick: released =>
            {
                if ((released & XButton.Guide) != 0)
                    nav.ReleaseAltTabIfEngaged();
            },
            cts.Token,
            onConnectionChanged: connected =>
            {
                status.ControllerConnected = connected;
                status.ControllerName = connected ? ControllerIdentity.GetConnectedControllerName() : null;
                if (connected)
                    log.Info($"Controller model: {status.ControllerName ?? "could not be determined"}");
            }))
        { IsBackground = true, Name = "XboxLARP" };
        listenerThread.Start();

        tray.ExitRequested += () =>
        {
            cts.Cancel();
            Application.Exit();
        };

        Application.Run();
        _singleInstanceMutex.ReleaseMutex();
        return 0;
    }

    public static int TestGameModeCycle(int holdSeconds, ILogSink log)
    {
        var appConfig = AppConfig.LoadOrCreateDefault(Paths.AppConfigPath);
        var monitorConfig = ConfigStore.Load(Paths.MonitorsConfigPath);
        var topology = new TopologyService(log);
        var orchestrator = new GameModeOrchestrator(topology, monitorConfig, appConfig, log);

        Console.WriteLine("--- EnterGameMode() ---");
        orchestrator.EnterGameMode();
        Console.WriteLine($"InGameMode = {orchestrator.InGameMode}");

        Console.WriteLine($"--- holding {holdSeconds}s ---");
        Thread.Sleep(holdSeconds * 1000);

        Console.WriteLine("--- CloseGameMode() ---");
        orchestrator.CloseGameMode();

        Console.WriteLine("--- waiting for exit-restore to complete ---");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (orchestrator.InGameMode && DateTime.UtcNow < deadline)
            Thread.Sleep(200);
        Thread.Sleep(1500); // grace period: InGameMode flips false slightly before the restore ApplyProfile call finishes

        Console.WriteLine($"Done. InGameMode = {orchestrator.InGameMode}");
        return 0;
    }

    public static int SetPlaynitePath(string path, ILogSink log)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }
        var appConfig = AppConfig.LoadOrCreateDefault(Paths.AppConfigPath);
        appConfig.PlaynitePath = path;
        AppConfig.Save(Paths.AppConfigPath, appConfig);
        Console.WriteLine($"Playnite path set to: {path}");
        return 0;
    }

    public static int StressTest(int count, ILogSink log)
    {
        if (count <= 0)
        {
            Console.Error.WriteLine("Count must be positive.");
            return 1;
        }

        var config = ConfigStore.Load(Paths.MonitorsConfigPath);
        if (config.FindProfile(Paths.NormalProfile) is null || config.FindProfile(Paths.GameOnlyProfile) is null)
        {
            Console.Error.WriteLine($"Both '{Paths.NormalProfile}' and '{Paths.GameOnlyProfile}' profiles must be captured first.");
            return 1;
        }

        var svc = new TopologyService(log);
        int successes = 0, failures = 0;
        var attemptCounts = new List<int>();

        for (int i = 1; i <= count; i++)
        {
            Console.WriteLine($"\n--- Cycle {i}/{count}: applying {Paths.GameOnlyProfile} ---");
            var r1 = svc.ApplyProfile(config, Paths.GameOnlyProfile);
            Tally(r1);

            Console.WriteLine($"--- Cycle {i}/{count}: applying {Paths.NormalProfile} ---");
            var r2 = svc.ApplyProfile(config, Paths.NormalProfile);
            Tally(r2);

            void Tally(ApplyResult r)
            {
                attemptCounts.Add(r.Attempts);
                if (r.Success) successes++; else failures++;
                Console.WriteLine(r.Success ? $"  OK (attempt {r.Attempts})" : $"  FAILED: {r.Message}");
            }
        }

        Console.WriteLine($"\n=== Stress test complete: {successes} succeeded, {failures} failed, out of {count * 2} applies ===");
        Console.WriteLine($"Average attempts per apply: {attemptCounts.Average():F2} (max {attemptCounts.Max()})");
        return failures == 0 ? 0 : 1;
    }
}
