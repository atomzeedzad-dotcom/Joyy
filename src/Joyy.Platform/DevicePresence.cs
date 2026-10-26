using System.Runtime.InteropServices;
using Joyy.Core;

namespace Joyy.Platform;

/// <summary>
/// Asks Windows (not SDL) whether a DualSense HID interface is present. Used as a watchdog:
/// SDL in a long-running process does not always notice a DualSense that comes back while
/// HidHide is hiding it, but a fresh SDL init finds it.
/// </summary>
public static partial class DevicePresence
{
    static readonly Guid HidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    const uint CrSuccess = 0;
    const uint PresentOnly = 0; // CM_GET_DEVICE_INTERFACE_LIST_PRESENT

    public static bool AnyDualSensePresent()
    {
        var guid = HidInterface;
        if (CM_Get_Device_Interface_List_SizeW(out uint length, ref guid, null, PresentOnly) != CrSuccess || length <= 1)
            return false;

        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, PresentOnly) != CrSuccess)
            return false;

        // Multi-sz list of interface paths, e.g. \\?\hid#vid_054c&pid_0ce6&mi_03#...
        foreach (var path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (DeviceIds.IsDualSenseInstancePath(path))
                return true;
        }
        return false;
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, [Out] char[] buffer, uint length, uint flags);
}
