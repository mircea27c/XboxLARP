namespace MonitorTopology;

/// <summary>
/// Persistent, cross-reboot identity for a physical monitor.
/// Adapter LUIDs and target IDs are only valid for the current boot session (Microsoft
/// documents LUIDs as unstable across reboots), so they are never used as the stored key -
/// only as a live "handle" re-resolved by matching this identity against a fresh query.
/// </summary>
public sealed record DisplayIdentity(
    bool EdidValid,
    ushort EdidManufactureId,
    ushort EdidProductCodeId,
    DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY OutputTechnology,
    uint ConnectorInstance)
{
    public string StableKey => EdidValid
        ? $"edid:{EdidManufactureId:X4}:{EdidProductCodeId:X4}:{OutputTechnology}:{ConnectorInstance}"
        : $"port:{OutputTechnology}:{ConnectorInstance}";
}

/// <summary>A monitor as observed in the current live topology query.</summary>
public sealed class LiveMonitor
{
    public required DisplayIdentity Identity { get; init; }
    public required string FriendlyName { get; init; }
    public required LUID AdapterId { get; init; }
    public required uint TargetId { get; init; }
    public required uint SourceId { get; init; }
    public required bool Active { get; init; }
    public required bool IsPrimary { get; init; }
    public required int PathIndex { get; init; }
}
