using System.Runtime.InteropServices;

namespace Joyy.Platform;

/// <summary>
/// Reads a device's driver stack (DEVPKEY_Device_Stack). HidHide only filters a device once its
/// filter driver is in that stack, which happens when the device is (re)plugged or Windows
/// restarts. A controller that was connected while HidHide was being installed is not hidden
/// until then, so the UI asks the user to replug it.
/// </summary>
public static partial class DeviceStack
{
    const uint CrSuccess = 0;
    const uint CrBufferSmall = 0x1A;

    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    static readonly DevPropKey StackKey = new()
    {
        FormatId = new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"),
        PropertyId = 14,
    };

    /// <summary>True/false when the stack could be read; null if the device is not present.</summary>
    public static bool? HasHidHideFilter(string instancePath)
    {
        var drivers = GetStack(instancePath);
        return drivers?.Any(d => d.EndsWith(@"\HidHide", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string>? GetStack(string instancePath)
    {
        if (CM_Locate_DevNodeW(out uint devInst, instancePath, 0) != CrSuccess)
            return null;

        var key = StackKey;
        uint size = 0;
        uint cr = CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
        if (cr != CrBufferSmall || size == 0)
            return null;

        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) != CrSuccess)
            return null;

        // DEVPROP_TYPE_STRING_LIST: UTF-16 strings, each NUL-terminated, list ends with an extra NUL.
        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint propertyType,
        [Out] byte[]? buffer, ref uint bufferSize, uint flags);
}
