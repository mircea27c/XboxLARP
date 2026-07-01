using System.Text.Json;

namespace XboxLARP;

public enum ActionType
{
    EnterGameMode,
    CloseGameMode,
    NavUp,
    NavDown,
    NavLeft,
    NavRight,
    NavEnter,
    NavEsc,
    NavAltTabForward,
    NavAltTabBackward,
}

public sealed class Binding
{
    public string Name { get; set; } = "";
    public List<string> Modifiers { get; set; } = new();
    public string Trigger { get; set; } = "";
    public string Action { get; set; } = "";
}

public sealed class ControllerConfig
{
    public int PollHz { get; set; } = 125;
    public int CooldownMs { get; set; } = 250;
    public List<Binding> Bindings { get; set; } = new();

    public static ControllerConfig Default() => new()
    {
        PollHz = 125,
        CooldownMs = 250,
        Bindings =
        [
            new Binding { Name = "EnterGameMode", Modifiers = ["Guide"], Trigger = "Start", Action = "EnterGameMode" },
            new Binding { Name = "CloseGameMode", Modifiers = ["Guide"], Trigger = "Back", Action = "CloseGameMode" },
            new Binding { Name = "NavUp", Modifiers = ["Guide"], Trigger = "DPadUp", Action = "NavUp" },
            new Binding { Name = "NavDown", Modifiers = ["Guide"], Trigger = "DPadDown", Action = "NavDown" },
            new Binding { Name = "NavLeft", Modifiers = ["Guide"], Trigger = "DPadLeft", Action = "NavLeft" },
            new Binding { Name = "NavRight", Modifiers = ["Guide"], Trigger = "DPadRight", Action = "NavRight" },
            new Binding { Name = "NavEnter", Modifiers = ["Guide"], Trigger = "A", Action = "NavEnter" },
            new Binding { Name = "NavEsc", Modifiers = ["Guide"], Trigger = "B", Action = "NavEsc" },
            new Binding { Name = "NavAltTabForward", Modifiers = ["Guide"], Trigger = "X", Action = "NavAltTabForward" },
            new Binding { Name = "NavAltTabBackward", Modifiers = ["Guide"], Trigger = "Y", Action = "NavAltTabBackward" },
        ],
    };

    public static ControllerConfig LoadOrCreateDefault(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ControllerConfig>(json) ?? Default();
        }

        var config = Default();
        Save(path, config);
        return config;
    }

    public static void Save(string path, ControllerConfig config)
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
