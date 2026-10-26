using Joyy.Cli;

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "view";

switch (command)
{
    case "view":
        return Viewer.Run(Viewer.Options.Parse(args.Skip(1).ToArray()));
    case "run":
        return RunCommand.Run(Viewer.Options.Parse(args.Skip(1).ToArray()).Seconds, args.Contains("--trace"));
    case "selftest":
        return SelfTest.Run(withSdl: args.Contains("--with-sdl"));
    case "xinput":
        return XInputCommand.Run(args.Skip(1).ToArray());
    case "-h" or "--help" or "help":
        PrintUsage();
        return 0;
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        PrintUsage();
        return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        joyy-cli view [--seconds N] [--plain]
            Show live DualSense input and the Xbox 360 report it maps to.
            --seconds N  stop after N seconds (default: run until Ctrl+C)
            --plain      print one line every 250 ms instead of redrawing in place
                         (automatic when output is redirected)

        joyy-cli run [--seconds N]
            Bridge the DualSense to a virtual Xbox 360 controller until Ctrl+C.

        joyy-cli selftest
            Send scripted input through ViGEmBus and read it back via XInput,
            checking every button, trigger, stick and rumble path. No controller needed.

        joyy-cli xinput [--rumble]
            List controllers as an XInput game sees them; --rumble pulses the first one.
        """);
}
