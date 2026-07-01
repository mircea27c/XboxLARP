using System.Text.Json;

namespace XboxLARP;

public sealed class AppConfig
{
    public string PlaynitePath { get; set; } = "";
    public int GracefulCloseTimeoutMs { get; set; } = 5000;
    public int PreLaunchSettleDelayMs { get; set; } = 800;

    public static AppConfig LoadOrCreateDefault(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        var config = new AppConfig();
        Save(path, config);
        return config;
    }

    public static void Save(string path, AppConfig config)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
