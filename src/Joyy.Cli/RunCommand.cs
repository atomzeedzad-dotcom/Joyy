using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Cli;

/// <summary>Headless bridge: DualSense in, virtual Xbox 360 pad out, until Ctrl+C.</summary>
internal static class RunCommand
{
    public static int Run(double? seconds, bool trace = false)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        if (seconds is not null)
            cancel.CancelAfter(TimeSpan.FromSeconds(seconds.Value));

        using var timer = new TimerResolution(1);
        using var physical = new SdlPad();
        using var bridge = new Bridge(physical, () => new ViGEmPad(), new Mapper(new Profile()));

        static void Log(string m) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {m}");
        physical.Connected += d => Log($"DualSense connected via {d.Connection} (serial {d.Serial ?? "?"})");
        physical.Disconnected += _ => Log("DualSense disconnected");
        physical.Warning += Log;
        bridge.StatusChanged += (status, detail) =>
        {
            string slot = bridge.VirtualPad?.UserIndex is int i ? $" (XInput slot {i + 1})" : "";
            Log(status switch
            {
                BridgeStatus.Running => "Virtual Xbox 360 controller plugged in" + slot,
                BridgeStatus.WaitingForController => "Waiting for a DualSense (virtual controller unplugged)",
                _ => $"Virtual controller unavailable: {detail}",
            });
        };

        Log($"joyy-cli run  |  SDL {SdlPad.NativeVersion}  |  Ctrl+C to stop");
        Log("Note: until device hiding (M5) exists, games can still see the DualSense itself as well.");
        if (trace)
        {
            // Same loop as Bridge.Run, printing input/output twice a second.
            long next = 0;
            while (!cancel.IsCancellationRequested)
            {
                bridge.Tick();
                if (Environment.TickCount64 >= next)
                {
                    next = Environment.TickCount64 + 500;
                    var i = bridge.LastInput;
                    var o = bridge.LastOutput;
                    string seen = $"userIndex={bridge.VirtualPad?.UserIndex?.ToString() ?? "-"} xinput: " + string.Join("; ",
                        Enumerable.Range(0, 4).Select(s => XInputProbe.Read(s) is { } x
                            ? $"slot {s + 1} L({x.ThumbLX},{x.ThumbLY}) R({x.ThumbRX},{x.ThumbRY}) LT {x.LeftTrigger} [{x.Buttons}]"
                            : $"slot {s + 1} -"));
                    Log($"in L({i.LeftX},{i.LeftY}) R({i.RightX},{i.RightY}) L2 {i.L2} R2 {i.R2} | " +
                        $"out L({o.ThumbLX},{o.ThumbLY}) R({o.ThumbRX},{o.ThumbRY}) LT {o.LeftTrigger} RT {o.RightTrigger} [{o.Buttons}] | {seen}");
                }
                Thread.Sleep(1);
            }
        }
        else
        {
            bridge.Run(cancel.Token);
        }
        Log("Stopped; virtual controller unplugged.");
        return 0;
    }
}
