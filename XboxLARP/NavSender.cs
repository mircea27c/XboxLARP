using System.Runtime.InteropServices;
using MonitorTopology;

namespace XboxLARP;

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

/// <summary>Sends navigation keystrokes via SendInput. Owns the "is Alt currently held down
/// by us" state so repeated Guide+X taps cycle the real Alt-Tab switcher instead of just
/// flipping to the previous window each time.</summary>
public sealed class NavSender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private readonly ILogSink _log;
    private bool _altTabEngaged;

    public NavSender(ILogSink log) => _log = log;

    public void Up() => Tap(VK_UP, extended: true);
    public void Down() => Tap(VK_DOWN, extended: true);
    public void Left() => Tap(VK_LEFT, extended: true);
    public void Right() => Tap(VK_RIGHT, extended: true);
    public void Enter() => Tap(VK_RETURN, extended: false);
    public void Esc() => Tap(VK_ESCAPE, extended: false);

    public void AltTabForward() => AltTabTap(shift: false);
    public void AltTabBackward() => AltTabTap(shift: true);

    private void AltTabTap(bool shift)
    {
        if (!_altTabEngaged)
        {
            KeyDown(VK_MENU, extended: false);
            _altTabEngaged = true;
        }
        if (shift) KeyDown(VK_SHIFT, extended: false);
        Tap(VK_TAB, extended: false);
        if (shift) KeyUp(VK_SHIFT, extended: false);
    }

    /// <summary>Call when the modifier (Guide) button is released - releases Alt if an
    /// Alt-Tab sequence was in progress, so the switcher's selection commits.</summary>
    public void ReleaseAltTabIfEngaged()
    {
        if (!_altTabEngaged) return;
        KeyUp(VK_MENU, extended: false);
        _altTabEngaged = false;
    }

    private void Tap(ushort vk, bool extended)
    {
        KeyDown(vk, extended);
        KeyUp(vk, extended);
    }

    private void KeyDown(ushort vk, bool extended) => Send(vk, extended, up: false);
    private void KeyUp(ushort vk, bool extended) => Send(vk, extended, up: true);

    private void Send(ushort vk, bool extended, bool up)
    {
        uint flags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } },
        };
        uint sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
            _log.Warn($"SendInput failed for vk=0x{vk:X2} up={up}: Win32 error {Marshal.GetLastWin32Error()}");
    }
}
