namespace Joyy.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
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
}
