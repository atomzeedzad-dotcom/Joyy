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
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\8&22953b1d&0&0000", true)]          // this PC, USB (from HidHideCLI)
    [InlineData(@"HID\VID_054C&PID_0DF2&MI_03\8&1&0&0000", true)]                 // Edge
    [InlineData(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6\9&1a2b&0&0000", true)] // Bluetooth
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&22953b1d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", true)] // interface path
    [InlineData(@"HID\VID_045E&PID_028E&IG_01\3&244e8da3&0&0000", false)]         // our virtual Xbox pad
    [InlineData(@"HID\VID_054C&PID_09CC&MI_03\1&2", false)]                       // DualShock 4
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Recognizes_DualSense_instance_paths(string? path, bool expected)
    {
        Assert.Equal(expected, DeviceIds.IsDualSenseInstancePath(path));
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
