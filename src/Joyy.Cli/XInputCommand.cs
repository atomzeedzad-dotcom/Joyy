using Joyy.Platform;

namespace Joyy.Cli;

/// <summary>Diagnostic: list controllers exactly as an XInput game sees them; optionally pulse rumble.</summary>
internal static class XInputCommand
{
    public static int Run(string[] args)
    {
        int samples = 1;
        int at = Array.IndexOf(args, "--samples");
        if (at >= 0 && at + 1 < args.Length)
            int.TryParse(args[at + 1], out samples);

        var slots = XInputProbe.ConnectedSlots().ToList();
        Console.WriteLine($"XInput controllers: {slots.Count}");
        for (int n = 0; n < Math.Max(1, samples); n++)
        {
            if (n > 0)
                Thread.Sleep(250);
            foreach (int s in slots)
            {
                if (XInputProbe.Read(s) is not { } r)
                    continue;
                Console.WriteLine($"  slot {s + 1}: buttons [{r.Buttons}] LT {r.LeftTrigger} RT {r.RightTrigger} " +
                    $"L({r.ThumbLX},{r.ThumbLY}) R({r.ThumbRX},{r.ThumbRY})");
            }
        }

        if (args.Contains("--rumble") && slots.Count > 0)
        {
            Console.WriteLine($"Rumbling slot {slots[0] + 1} for 0.5 s...");
            XInputProbe.SetRumble(slots[0], 30000, 30000);
            Thread.Sleep(500);
            XInputProbe.SetRumble(slots[0], 0, 0);
        }
        return 0;
    }
}
