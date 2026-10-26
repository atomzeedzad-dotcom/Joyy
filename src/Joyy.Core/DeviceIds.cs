namespace Joyy.Core;

public enum ConnectionKind
{
    Unknown,
    Usb,
    Bluetooth,
}

/// <summary>USB/HID identity of the controllers Joyy supports.</summary>
public static class DeviceIds
{
    public const ushort SonyVendor = 0x054C;
    public const ushort DualSense = 0x0CE6;
    public const ushort DualSenseEdge = 0x0DF2;

    /// <summary>Bluetooth HID service class GUID, present in Bluetooth HID device paths.</summary>
    const string BluetoothHidGuid = "00001124-0000-1000-8000-00805F9B34FB";

    public static bool IsDualSense(ushort vendorId, ushort productId) =>
        vendorId == SonyVendor && (productId == DualSense || productId == DualSenseEdge);

    /// <summary>
    /// Classifies a Windows HID device path, e.g.
    /// <c>\\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#...</c> (USB) or
    /// <c>\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&amp;0002054c_pid&amp;0ce6#...</c> (Bluetooth).
    /// </summary>
    public static ConnectionKind ClassifyConnection(string? devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return ConnectionKind.Unknown;

        if (devicePath.Contains(BluetoothHidGuid, StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("BTHLE", StringComparison.OrdinalIgnoreCase))
            return ConnectionKind.Bluetooth;

        if (devicePath.Contains("VID_", StringComparison.OrdinalIgnoreCase))
            return ConnectionKind.Usb;

        return ConnectionKind.Unknown;
    }
}
