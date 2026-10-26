using Joyy.Platform;

namespace Joyy.Platform.Tests;

sealed class FakeHidHide : IHidHide
{
    public bool Cloak;
    public readonly List<string> Hidden = new();
    public readonly List<string> Apps = new();
    public readonly List<HidDeviceInfo> Devices = new();
    public Func<string, bool>? FailHide;
    public bool FailUnhide;
    public readonly List<string> Calls = new();

    public bool IsCloakOn() => Cloak;
    public void SetCloak(bool on) { Calls.Add($"cloak {on}"); Cloak = on; }
    public IReadOnlyList<string> HiddenDevices() => Hidden.ToList();
    public void Hide(string p)
    {
        Calls.Add($"hide {p}");
        if (FailHide?.Invoke(p) == true) throw new HidHideException("boom");
        Hidden.Add(p);
    }
    public void Unhide(string p)
    {
        Calls.Add($"unhide {p}");
        if (FailUnhide) throw new HidHideException("boom");
        Hidden.Remove(p);
    }
    public IReadOnlyList<string> RegisteredApps() => Apps.ToList();
    public void RegisterApp(string p) { Calls.Add($"reg {p}"); Apps.Add(p); }
    public void UnregisterApp(string p) { Calls.Add($"unreg {p}"); Apps.Remove(p); }
    public IReadOnlyList<HidDeviceInfo> GamingDevices() => Devices.ToList();
}

public sealed class DeviceHiderTests : IDisposable
{
    const string App = @"C:\Joyy\Joyy.exe";
    const string UsbPad = @"HID\VID_054C&PID_0CE6&MI_03\8&22953b1d&0&0000";
    const string BtPad = @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6\9&1&0&0000";
    const string XboxPad = @"HID\VID_045E&PID_028E&IG_01\3&244e8da3&0&0000";
    const string UsersOwnDevice = @"HID\VID_046D&PID_C52B\7&1";

    readonly string _journal = Path.Combine(Path.GetTempPath(), $"joyy-hider-{Guid.NewGuid():N}.json");
    readonly FakeHidHide _hh = new();

    public DeviceHiderTests()
    {
        _hh.Devices.Add(new HidDeviceInfo(UsbPad, "DualSense", true));
        _hh.Devices.Add(new HidDeviceInfo(XboxPad, "Xbox 360", true));
    }

    public void Dispose() => File.Delete(_journal);

    DeviceHider NewHider() => new(_hh, App, _journal);

    [Fact]
    public void Apply_hides_only_the_DualSense_registers_Joyy_and_cloaks()
    {
        int count = NewHider().Apply();

        Assert.Equal(1, count);
        Assert.Equal(new[] { UsbPad }, _hh.Hidden);
        Assert.Contains(App, _hh.Apps);
        Assert.True(_hh.Cloak);
        Assert.True(File.Exists(_journal));
    }

    [Fact]
    public void Joyy_is_whitelisted_before_any_device_is_hidden()
    {
        NewHider().Apply();
        Assert.True(_hh.Calls.IndexOf($"reg {App}") < _hh.Calls.IndexOf($"hide {UsbPad}"));
    }

    [Fact]
    public void Restore_undoes_everything_and_removes_journal()
    {
        var hider = NewHider();
        hider.Apply();
        hider.Restore();

        Assert.Empty(_hh.Hidden);
        Assert.Empty(_hh.Apps);
        Assert.False(_hh.Cloak);
        Assert.False(File.Exists(_journal));
    }

    [Fact]
    public void Restore_leaves_the_users_own_HidHide_setup_alone()
    {
        _hh.Cloak = true;
        _hh.Hidden.Add(UsersOwnDevice);
        _hh.Apps.Add(App); // user had already whitelisted Joyy

        var hider = NewHider();
        hider.Apply();
        hider.Restore();

        Assert.True(_hh.Cloak);
        Assert.Equal(new[] { UsersOwnDevice }, _hh.Hidden);
        Assert.Equal(new[] { App }, _hh.Apps);
    }

    [Fact]
    public void Device_the_user_already_hid_is_not_claimed_or_unhidden()
    {
        _hh.Hidden.Add(UsbPad);
        var hider = NewHider();
        hider.Apply();
        hider.Restore();

        Assert.Equal(new[] { UsbPad }, _hh.Hidden);
    }

    [Fact]
    public void Apply_is_idempotent_and_picks_up_new_transports()
    {
        var hider = NewHider();
        hider.Apply();
        _hh.Devices.Add(new HidDeviceInfo(BtPad, "DualSense", true));
        int count = hider.Apply();

        Assert.Equal(2, count);
        Assert.Equal(new[] { UsbPad, BtPad }, _hh.Hidden);
        Assert.Single(_hh.Calls, c => c == $"hide {UsbPad}");
    }

    [Fact]
    public void Absent_devices_are_ignored()
    {
        _hh.Devices.Add(new HidDeviceInfo(BtPad, "DualSense", false));
        NewHider().Apply();
        Assert.DoesNotContain(BtPad, _hh.Hidden);
    }

    [Fact]
    public void Crash_after_hiding_is_recovered_on_next_start()
    {
        NewHider().Apply(); // ...and the process dies here, never calling Restore

        var nextRun = NewHider();
        Assert.True(nextRun.HasPendingRestore);
        Assert.True(nextRun.RecoverIfNeeded());

        Assert.Empty(_hh.Hidden);
        Assert.False(_hh.Cloak);
        Assert.False(nextRun.RecoverIfNeeded());
    }

    [Fact]
    public void Journal_is_written_before_the_device_is_hidden()
    {
        // Simulate dying in the middle of the hide call: the device is hidden but the
        // call never returns. The journal must already list it.
        _hh.FailHide = p => { _hh.Hidden.Add(p); return true; };
        Assert.Throws<HidHideException>(() => NewHider().Apply());

        _hh.FailHide = null;
        NewHider().RecoverIfNeeded();
        Assert.Empty(_hh.Hidden);
    }

    [Fact]
    public void Failed_restore_keeps_journal_for_retry()
    {
        var hider = NewHider();
        hider.Apply();
        _hh.FailUnhide = true;

        Assert.Throws<AggregateException>(hider.Restore);
        Assert.True(File.Exists(_journal));

        _hh.FailUnhide = false;
        hider.Restore();
        Assert.Empty(_hh.Hidden);
        Assert.False(File.Exists(_journal));
    }

    [Fact]
    public void Corrupt_journal_still_unhides_every_DualSense()
    {
        _hh.Hidden.Add(UsbPad);
        _hh.Hidden.Add(UsersOwnDevice);
        File.WriteAllText(_journal, "{ corrupt");

        NewHider().RecoverIfNeeded();
        Assert.Equal(new[] { UsersOwnDevice }, _hh.Hidden);
    }

    [Fact]
    public void Parses_cli_list_output()
    {
        string output = "--dev-hide \"HID\\VID_054C&PID_0CE6&MI_03\\8&22953b1d&0&0000\"\r\n--dev-hide \"HID\\X\"\r\n";
        Assert.Equal(new[] { @"HID\VID_054C&PID_0CE6&MI_03\8&22953b1d&0&0000", @"HID\X" },
            HidHideCli.ParseList(output, "--dev-hide"));
        Assert.Empty(HidHideCli.ParseList("--cloak-off\r\n", "--dev-hide"));
    }
}
