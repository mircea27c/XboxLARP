using System.Runtime.InteropServices;

namespace MonitorTopology;

// Raw P/Invoke surface for the Windows CCD (Connecting and Configuring Displays) API.
// Struct layouts mirror wingdi.h / winuser.h exactly - do not reorder fields.

[StructLayout(LayoutKind.Sequential)]
public struct LUID
{
    public uint LowPart;
    public int HighPart;

    public readonly bool Equals(LUID other) => LowPart == other.LowPart && HighPart == other.HighPart;
    public override readonly string ToString() => $"{HighPart:X8}:{LowPart:X8}";
}

public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
{
    OTHER = 0xFFFFFFFF,
    HD15 = 0,
    SVIDEO = 1,
    COMPOSITE_VIDEO = 2,
    COMPONENT_VIDEO = 3,
    DVI = 4,
    HDMI = 5,
    LVDS = 6,
    D_JPN = 8,
    SDI = 9,
    DISPLAYPORT_EXTERNAL = 10,
    DISPLAYPORT_EMBEDDED = 11,
    UDI_EXTERNAL = 12,
    UDI_EMBEDDED = 13,
    SDTVDONGLE = 14,
    MIRACAST = 15,
    INDIRECT_WIRED = 16,
    INDIRECT_VIRTUAL = 17,
    INTERNAL = 0x80000000,
}

public enum DISPLAYCONFIG_ROTATION : uint
{
    IDENTITY = 1,
    ROTATE90 = 2,
    ROTATE180 = 3,
    ROTATE270 = 4,
}

public enum DISPLAYCONFIG_SCALING : uint
{
    IDENTITY = 1,
    CENTERED = 2,
    STRETCHED = 3,
    ASPECTRATIOCENTEREDMAX = 4,
    CUSTOM = 5,
    PREFERRED = 128,
}

public enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
{
    UNSPECIFIED = 0,
    PROGRESSIVE = 1,
    INTERLACED = 2,
}

public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
{
    SOURCE = 1,
    TARGET = 2,
    DESKTOP_IMAGE = 3,
}

public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
{
    GET_SOURCE_NAME = 1,
    GET_TARGET_NAME = 2,
    GET_TARGET_PREFERRED_MODE = 3,
    GET_ADAPTER_NAME = 4,
    SET_TARGET_PERSISTENCE = 5,
    GET_TARGET_BASE_TYPE = 6,
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx; // union { UINT32 modeInfoIdx; struct { cloneGroupId:16; sourceModeInfoIdx:16; } } - read/written as one uint
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx; // union { UINT32 modeInfoIdx; struct { desktopModeInfoIdx:16; targetModeInfoIdx:16; } }
    public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
    public DISPLAYCONFIG_ROTATION rotation;
    public DISPLAYCONFIG_SCALING scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate;
    public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)]
    public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_2DREGION
{
    public uint cx;
    public uint cy;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
{
    public ulong pixelRate;
    public DISPLAYCONFIG_RATIONAL hSyncFreq;
    public DISPLAYCONFIG_RATIONAL vSyncFreq;
    public DISPLAYCONFIG_2DREGION activeSize;
    public DISPLAYCONFIG_2DREGION totalSize;
    public uint videoStandardOrCloneGroupId;
    public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_TARGET_MODE
{
    public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINTL
{
    public int x;
    public int y;
}

public enum DISPLAYCONFIG_PIXELFORMAT : uint
{
    PIXELFORMAT_8BPP = 1,
    PIXELFORMAT_16BPP = 2,
    PIXELFORMAT_24BPP = 3,
    PIXELFORMAT_32BPP = 4,
    PIXELFORMAT_NONGDI = 5,
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_SOURCE_MODE
{
    public uint width;
    public uint height;
    public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
    public POINTL position;
}

// DISPLAYCONFIG_MODE_INFO is a tagged union (targetMode / sourceMode / desktopImageInfo).
// We only ever populate targetMode or sourceMode, so desktopImageInfo is omitted; the
// union region is still large enough (48 bytes) to match the real struct's total size (64 bytes).
[StructLayout(LayoutKind.Explicit)]
public struct DISPLAYCONFIG_MODE_INFO
{
    [FieldOffset(0)] public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
    [FieldOffset(4)] public uint id;
    [FieldOffset(8)] public LUID adapterId;
    [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
    [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_TARGET_PREFERRED_MODE
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint width;
    public uint height;
    public DISPLAYCONFIG_TARGET_MODE targetMode;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint flags; // bit0 friendlyNameFromEdid, bit1 friendlyNameForced, bit2 edidIdsValid
    public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;

    public readonly bool EdidIdsValid => (flags & 0x4) != 0;
}

public static class NativeMethods
{
    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    public const uint SDC_VALIDATE = 0x00000040;
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_NO_OPTIMIZATION = 0x00000100;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;

    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
        uint numModeInfoArrayElements,
        [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int DisplayConfigGetDeviceInfoPreferredMode(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE requestPacket);
}
