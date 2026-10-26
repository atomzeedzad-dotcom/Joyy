namespace Joyy.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--restore", StringComparison.OrdinalIgnoreCase)))
            return RestoreVisibility();

        // One Joyy per user session. A second launch just asks the first to show its window.
        using var instance = new Mutex(true, @"Local\Joyy.App.SingleInstance", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Joyy.App.Show");
        if (!first)
        {
            showSignal.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show($"Joyy hit an unexpected error:\n\n{e.Exception.Message}", "Joyy",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        bool hidden = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApp(hidden, showSignal));
        return 0;
    }

    /// <summary>Emergency fix: "Joyy.exe --restore" makes the DualSense visible again without starting the app.</summary>
    static int RestoreVisibility()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (Joyy.Platform.HidHideCli.Locate() is not { } cli)
            {
                MessageBox.Show("HidHide is not installed, so Joyy never hid anything.", "Joyy");
                return 0;
            }
            var hider = new Joyy.Platform.DeviceHider(new Joyy.Platform.HidHideCli(cli), Environment.ProcessPath!);
            bool had = hider.HasPendingRestore;
            hider.Restore();
            MessageBox.Show(had ? "Your DualSense is visible to games again." : "Nothing to restore: Joyy has nothing hidden.", "Joyy");
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not restore: {ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
