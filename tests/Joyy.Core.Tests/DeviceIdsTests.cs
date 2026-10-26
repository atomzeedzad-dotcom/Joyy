using Joyy.Core;

namespace Joyy.Core.Tests;

public class DeviceIdsTests
{
    [Theory]
    [InlineData(0x054C, 0x0CE6, true)]
    [InlineData(0x054C, 0x0DF2, true)]
    [InlineData(0x054C, 0x09CC, false)] // DualShock 4
    [InlineData(0x045E, 0x028E, false)] // Xbox 360 (our own virtual pad)
    public void Recognizes_DualSense_and_Edge_only(int vid, int pid, bool expected)
    {
        Assert.Equal(expected, DeviceIds.IsDualSense((ushort)vid, (ushort)pid));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&22953b1d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionKind.Usb)]
    [InlineData(@"\\?\HID#VID_054C&PID_0DF2&MI_03#7&ABC&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionKind.Usb)]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1a2b3c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionKind.Bluetooth)]
    [InlineData(@"\\?\BTHENUM#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6", ConnectionKind.Bluetooth)]
    [InlineData("", ConnectionKind.Unknown)]
    [InlineData(null, ConnectionKind.Unknown)]
    [InlineData("something-else", ConnectionKind.Unknown)]
    public void Classifies_device_paths(string? path, ConnectionKind expected)
    {
        Assert.Equal(expected, DeviceIds.ClassifyConnection(path));
    }
}
