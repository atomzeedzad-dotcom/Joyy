using System.Diagnostics;
using System.Text;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Cli;

/// <summary>M2 diagnostic: physical DualSense input next to the mapped Xbox 360 report.</summary>
internal static class Viewer
{
    public sealed record Options(double? Seconds, bool Plain)
    {
        public static Options Parse(string[] args)
        {
            double? seconds = null;
            bool plain = Console.IsOutputRedirected;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--plain")
                    plain = true;
                else if (args[i] == "--seconds" && i + 1 < args.Length && double.TryParse(args[++i], out var s))
                    seconds = s;
            }
            return new Options(seconds, plain);
        }
    }

    public static int Run(Options options)
    {
        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

        using var timer = new TimerResolution(1);
        using var pad = new SdlPad();
        var mapper = new Mapper(new Profile());
        var events = new List<string>();

        void Log(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss} {message}";
            if (options.Plain)
                Console.WriteLine(line);
            else
                events.Add(line);
        }

        pad.Connected += d => Log($"Connected: {d.Name} via {d.Connection}, VID {d.VendorId:X4} PID {d.ProductId:X4}, serial {d.Serial ?? "?"}, path {d.DevicePath ?? "?"}");
        pad.Disconnected += d => Log($"Disconnected: {d.Name}");
        pad.Warning += w => Log($"Warning: {w}");

        Console.WriteLine($"joyy-cli view  |  SDL {SdlPad.NativeVersion}  |  Ctrl+C to quit");
        if (!options.Plain)
        {
            Console.CursorVisible = false;
            Console.Clear();
        }

        var clock = Stopwatch.StartNew();
        long drawEvery = Stopwatch.Frequency / (options.Plain ? 4 : 20);
        long nextDraw = 0, rateWindowStart = 0;
        int polls = 0;
        double rate = 0;

        try
        {
            while (!stop && (options.Seconds is null || clock.Elapsed.TotalSeconds < options.Seconds))
            {
                bool connected = pad.Poll(out var state);
                polls++;

                long now = clock.ElapsedTicks;
                if (now - rateWindowStart >= Stopwatch.Frequency)
                {
                    rate = polls * (double)Stopwatch.Frequency / (now - rateWindowStart);
                    polls = 0;
                    rateWindowStart = now;
                }

                if (now >= nextDraw)
                {
                    nextDraw = now + drawEvery;
                    var report = mapper.Map(state);
                    if (options.Plain)
                        Console.WriteLine(connected ? PlainLine(state, report, rate) : $"waiting for DualSense... {rate:F0} Hz");
                    else
                        Redraw(pad.Device, connected, state, report, rate, events);
                }

                Thread.Sleep(1);
            }
        }
        finally
        {
            if (!options.Plain)
                Console.CursorVisible = true;
        }

        Console.WriteLine();
        return 0;
    }

    static string PlainLine(in PadState s, in X360Report r, double rate) =>
        $"{rate,5:F0} Hz | DS: {PhysicalNames(s.Buttons),-28} L({s.LeftX,6},{s.LeftY,6}) R({s.RightX,6},{s.RightY,6}) L2 {s.L2,3} R2 {s.R2,3}" +
        $" | X360: {XboxNames(r.Buttons),-22} LT {r.LeftTrigger,3} RT {r.RightTrigger,3}";

    static void Redraw(PadInfo? device, bool connected, in PadState s, in X360Report r, double rate, List<string> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine(device is null
            ? "Device   : waiting for a DualSense (USB or Bluetooth)..."
            : $"Device   : {device.Name}  [{device.Connection}]  VID {device.VendorId:X4} PID {device.ProductId:X4}  serial {device.Serial ?? "?"}");
        sb.AppendLine($"Poll rate: {rate:F0} Hz");
        sb.AppendLine();
        sb.AppendLine("DualSense (physical)");
        sb.AppendLine($"  Buttons: {(connected ? PhysicalNames(s.Buttons) : "-")}");
        sb.AppendLine($"  Left   : X {s.LeftX,6}  Y {s.LeftY,6}    Right: X {s.RightX,6}  Y {s.RightY,6}");
        sb.AppendLine($"  L2 {Bar(s.L2)} {s.L2,3}    R2 {Bar(s.R2)} {s.R2,3}");
        sb.AppendLine();
        sb.AppendLine("Xbox 360 (what the game will see)");
        sb.AppendLine($"  Buttons: {(connected ? XboxNames(r.Buttons) : "-")}");
        sb.AppendLine($"  Left   : X {r.ThumbLX,6}  Y {r.ThumbLY,6}    Right: X {r.ThumbRX,6}  Y {r.ThumbRY,6}");
        sb.AppendLine($"  LT {Bar(r.LeftTrigger)} {r.LeftTrigger,3}    RT {Bar(r.RightTrigger)} {r.RightTrigger,3}");
        sb.AppendLine();
        sb.AppendLine("Events");
        foreach (var e in events.TakeLast(6))
            sb.AppendLine("  " + e);

        int width = Math.Max(20, Console.WindowWidth - 1);
        var lines = sb.ToString().Split(Environment.NewLine);
        Console.SetCursorPosition(0, 1);
        foreach (var line in lines)
            Console.WriteLine(line.Length > width ? line[..width] : line.PadRight(width));
    }

    static string Bar(byte value)
    {
        int filled = value * 10 / 255;
        return "[" + new string('#', filled) + new string('.', 10 - filled) + "]";
    }

    static string PhysicalNames(uint buttons)
    {
        var names = Enum.GetValues<PhysicalButton>().Where(b => (buttons & PhysicalButtons.Bit(b)) != 0);
        string joined = string.Join(' ', names);
        return joined.Length == 0 ? "(none)" : joined;
    }

    static string XboxNames(X360Buttons buttons)
    {
        var names = Enum.GetValues<X360Buttons>().Where(b => b != X360Buttons.None && (buttons & b) == b);
        string joined = string.Join(' ', names);
        return joined.Length == 0 ? "(none)" : joined;
    }
}
