namespace XboxLARP;

public static class Paths
{
    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XboxLARP");

    public static string MonitorsConfigPath => Path.Combine(RootDir, "config", "monitors.config.json");
    public static string ControllerConfigPath => Path.Combine(RootDir, "config", "controller.config.json");
    public static string AppConfigPath => Path.Combine(RootDir, "config", "app.config.json");
    public static string LogPath => Path.Combine(RootDir, "logs", "controller.log");

    public const string NormalProfile = "Normal";
    public const string GameOnlyProfile = "GameOnly";
}
