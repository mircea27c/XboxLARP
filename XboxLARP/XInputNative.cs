using System.Runtime.InteropServices;

namespace XboxLARP;

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

[Flags]
public enum XButton : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    // Guide (Xbox logo) is undocumented: plain XInputGetState never reports it. It's only
    // visible via XInputGetStateEx (ordinal 100), which is what remap tools use to read it.
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public static class XInputNative
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    // XInputGetStateEx has no public name export - it's exported by ordinal 100 only.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    public static extern int XInputGetStateEx(int dwUserIndex, out XINPUT_STATE pState);
}
