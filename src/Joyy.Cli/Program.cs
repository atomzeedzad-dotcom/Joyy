using Joyy.Cli;

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "view";

switch (command)
{
    case "view":
        return Viewer.Run(Viewer.Options.Parse(args.Skip(1).ToArray()));
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
        """);
}
