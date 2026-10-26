using Joyy.Platform;

namespace Joyy.Cli;

/// <summary>hide / restore / hidestatus: device hiding without the tray app (and the emergency restore).</summary>
internal static class HideCommands
{
    public static int Run(string command)
    {
        string? cli = HidHideCli.Locate();
        if (cli is null)
        {
            Console.Error.WriteLine($"HidHide is not installed ({DependencyChecker.HidHideUrl}).");
            return command == "restore" ? 0 : 1; // nothing can be hidden without it
        }

        var hidHide = new HidHideCli(cli, m => Console.WriteLine("  " + m));
        var hider = new DeviceHider(hidHide, Environment.ProcessPath!);

        switch (command)
        {
            case "hide":
                hider.RecoverIfNeeded();
                int n = hider.Apply();
                Console.WriteLine($"Hidden {n} DualSense device(s) from other programs. Cloak: {(hidHide.IsCloakOn() ? "on" : "off")}.");
                return 0;

            case "restore":
                bool had = hider.HasPendingRestore;
                hider.Restore();
                Console.WriteLine(had ? "DualSense is visible to games again." : "Nothing to restore: Joyy has not hidden anything.");
                return 0;

            default: // hidestatus
                Console.WriteLine($"Cloak: {(hidHide.IsCloakOn() ? "on" : "off")}");
                Console.WriteLine("Hidden devices:");
                foreach (var d in hidHide.HiddenDevices())
                    Console.WriteLine($"  {d}");
                Console.WriteLine("Apps allowed to see hidden devices:");
                foreach (var a in hidHide.RegisteredApps())
                    Console.WriteLine($"  {a}");
                Console.WriteLine($"Joyy journal: {(hider.HasPendingRestore ? string.Join(", ", hider.HiddenByJoyy) : "none")}");
                Console.WriteLine("DualSense driver stacks (HidHide must be listed for hiding to work):");
                foreach (var dev in hidHide.GamingDevices().Where(d => d.Present && Joyy.Core.DeviceIds.IsDualSenseInstancePath(d.InstancePath)))
                    Console.WriteLine($"  {dev.InstancePath}: {string.Join(" > ", DeviceStack.GetStack(dev.InstancePath) ?? new[] { "?" })}");
                return 0;
        }
    }
}
