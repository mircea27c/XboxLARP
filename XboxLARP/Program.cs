using MonitorTopology;

namespace XboxLARP;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // No args = the normal way this app runs: tray icon + controller listener, no console.
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "--tray";

        using var log = new FileLogSink(Paths.LogPath);

        try
        {
            return command switch
            {
                "--tray" => Commands.Tray(log),
                "--query" => Commands.Query(log),
                "--setup-monitors" => Commands.SetupMonitors(log),
                "--capture-profile" => Commands.CaptureProfile(RequireArg(args, 1, "--capture-profile <ProfileName>"), log),
                "--apply-profile" => Commands.ApplyProfile(RequireArg(args, 1, "--apply-profile <ProfileName>"), log),
                "--activate-preferred" => Commands.ActivatePreferred(RequireArg(args, 1, "--activate-preferred <Role>"), log),
                "--dry-run" => Commands.DryRun(log),
                "--run" => Commands.Run(log),
                "--set-playnite-path" => Commands.SetPlaynitePath(RequireArg(args, 1, "--set-playnite-path <path>"), log),
                "--test-game-mode-cycle" => Commands.TestGameModeCycle(int.Parse(RequireArg(args, 1, "--test-game-mode-cycle <holdSeconds>")), log),
                "--stress-test" => Commands.StressTest(int.Parse(RequireArg(args, 1, "--stress-test <count>")), log),
                "--help" or "-h" or "/?" => Commands.Help(),
                _ => Commands.Unknown(command),
            };
        }
        catch (Exception ex)
        {
            log.Error($"Unhandled exception: {ex}");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string RequireArg(string[] args, int index, string usage)
    {
        if (index >= args.Length)
            throw new ArgumentException($"Missing argument. Usage: XboxLARP {usage}");
        return args[index];
    }
}
