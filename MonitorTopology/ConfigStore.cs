using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorTopology;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static MonitorConfig Load(string path)
    {
        if (!File.Exists(path))
            return new MonitorConfig();
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MonitorConfig>(json, Options) ?? new MonitorConfig();
    }

    public static void Save(string path, MonitorConfig config)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(config, Options);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true); // atomic-ish swap so a crash mid-write can't corrupt the config
    }
}
