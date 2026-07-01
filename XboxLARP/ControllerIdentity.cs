using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XboxLARP;

/// <summary>XInput itself has no concept of a controller's product name (only a generic
/// subtype like "gamepad"). The real name ("Xbox 360 Wireless Controller" etc, same as Steam
/// shows) lives on the underlying HID device's USB product string descriptor, found by
/// enumerating raw input devices and matching the XInput-class HID collection ("IG_" in its
/// device path is Microsoft's standard marker for XInput-compatible HID devices).</summary>
public static class ControllerIdentity
{
    private const uint RIM_TYPEHID = 2;
    private const uint RIDI_DEVICENAME = 0x20000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, StringBuilder Buffer, uint BufferLength);

    /// <summary>Best-effort; returns null if it can't be determined (never throws - this is
    /// a "nice to show" detail, not something that should ever break the listener).</summary>
    public static string? GetConnectedControllerName()
    {
        try
        {
            uint count = 0;
            uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            GetRawInputDeviceList(null, ref count, itemSize);
            if (count == 0) return null;

            var list = new RAWINPUTDEVICELIST[count];
            uint got = GetRawInputDeviceList(list, ref count, itemSize);
            if (got == unchecked((uint)-1)) return null;

            foreach (var dev in list)
            {
                if (dev.dwType != RIM_TYPEHID) continue;

                string? devicePath = GetDeviceName(dev.hDevice);
                if (devicePath is null || devicePath.IndexOf("IG_", StringComparison.OrdinalIgnoreCase) < 0)
                    continue; // not an XInput-class HID collection

                string? product = GetProductString(devicePath);
                if (!string.IsNullOrWhiteSpace(product))
                    return product.Trim();
            }
        }
        catch
        {
            // best-effort only
        }
        return null;
    }

    private static string? GetDeviceName(IntPtr hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref size);
            return result == unchecked((uint)-1) ? null : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetProductString(string devicePath)
    {
        using var handle = CreateFile(devicePath, 0, 3 /* FILE_SHARE_READ|WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var sb = new StringBuilder(256);
        return HidD_GetProductString(handle, sb, (uint)(sb.Capacity * 2)) ? sb.ToString() : null;
    }
}
