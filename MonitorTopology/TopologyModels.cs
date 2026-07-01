namespace MonitorTopology;

public sealed class CapturedSourceMode
{
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint PixelFormat { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
}

public sealed class CapturedTargetMode
{
    public ulong PixelRate { get; set; }
    public uint HSyncNumerator { get; set; }
    public uint HSyncDenominator { get; set; }
    public uint VSyncNumerator { get; set; }
    public uint VSyncDenominator { get; set; }
    public uint ActiveSizeCx { get; set; }
    public uint ActiveSizeCy { get; set; }
    public uint TotalSizeCx { get; set; }
    public uint TotalSizeCy { get; set; }
    public uint VideoStandardOrCloneGroupId { get; set; }
    public uint ScanLineOrdering { get; set; }
}

/// <summary>One physical monitor's captured state within a named profile.</summary>
public sealed class ProfileMonitorEntry
{
    public string Role { get; set; } = "";
    public uint Rotation { get; set; }
    public uint Scaling { get; set; }
    public uint RefreshNumerator { get; set; }
    public uint RefreshDenominator { get; set; }
    public uint ScanLineOrdering { get; set; }
    public CapturedSourceMode SourceMode { get; set; } = new();
    public CapturedTargetMode TargetMode { get; set; } = new();
}

public sealed class DisplayProfile
{
    public string Name { get; set; } = "";
    public List<ProfileMonitorEntry> ActiveMonitors { get; set; } = new();
}

/// <summary>A user-assigned role name bound to a monitor's persistent (cross-reboot) identity.</summary>
public sealed class MonitorLabel
{
    public string Role { get; set; } = "";
    public bool EdidValid { get; set; }
    public ushort EdidManufactureId { get; set; }
    public ushort EdidProductCodeId { get; set; }
    public string OutputTechnology { get; set; } = "";
    public uint ConnectorInstance { get; set; }
    public string FriendlyName { get; set; } = "";

    public string StableKey => EdidValid
        ? $"edid:{EdidManufactureId:X4}:{EdidProductCodeId:X4}:{OutputTechnology}:{ConnectorInstance}"
        : $"port:{OutputTechnology}:{ConnectorInstance}";
}

public sealed class MonitorConfig
{
    public List<MonitorLabel> Monitors { get; set; } = new();
    public List<DisplayProfile> Profiles { get; set; } = new();
    public string GameMonitorRole { get; set; } = "";

    public MonitorLabel? FindByRole(string role) =>
        Monitors.FirstOrDefault(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase));

    public MonitorLabel? FindByIdentity(DisplayIdentity identity) =>
        Monitors.FirstOrDefault(m => m.StableKey == identity.StableKey);

    public DisplayProfile? FindProfile(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record ApplyResult(bool Success, string Message, int Attempts);
