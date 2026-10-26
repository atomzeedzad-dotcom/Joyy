using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Joyy.Platform;

public sealed record HidDeviceInfo(string InstancePath, string Name, bool Present);

/// <summary>The HidHide operations Joyy needs. Implemented by <see cref="HidHideCli"/>; faked in tests.</summary>
public interface IHidHide
{
    bool IsCloakOn();
    void SetCloak(bool on);
    IReadOnlyList<string> HiddenDevices();
    void Hide(string instancePath);
    void Unhide(string instancePath);
    IReadOnlyList<string> RegisteredApps();
    void RegisterApp(string path);
    void UnregisterApp(string path);
    IReadOnlyList<HidDeviceInfo> GamingDevices();
}

public sealed class HidHideException(string message) : Exception(message);

/// <summary>
/// Drives HidHideCLI.exe. Flags verified against HidHide 1.5.230 (<c>HidHideCLI --help</c>):
/// --cloak-on/--cloak-off/--cloak-state, --dev-hide/--dev-unhide/--dev-list/--dev-gaming,
/// --app-reg/--app-unreg/--app-list. Every call is logged with its arguments and exit code.
/// </summary>
public sealed partial class HidHideCli : IHidHide
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    readonly string _exe;
    readonly Action<string>? _log;

    public HidHideCli(string exe, Action<string>? log = null)
    {
        _exe = exe;
        _log = log;
    }

    /// <summary>
    /// The CLI from the standard install folder, or null. Only Program Files is searched: the
    /// app may run elevated later, and must never launch an executable from a user-writable path.
    /// </summary>
    public static string? Locate()
    {
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            if (string.IsNullOrEmpty(root))
                continue;
            string path = Path.Combine(root, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public bool IsCloakOn() => Run("--cloak-state").Trim() == "--cloak-on";
    public void SetCloak(bool on) => Run(on ? "--cloak-on" : "--cloak-off");
    public IReadOnlyList<string> HiddenDevices() => ParseList(Run("--dev-list"), "--dev-hide");
    public void Hide(string instancePath) => Run("--dev-hide", instancePath);
    public void Unhide(string instancePath) => Run("--dev-unhide", instancePath);
    public IReadOnlyList<string> RegisteredApps() => ParseList(Run("--app-list"), "--app-reg");
    public void RegisterApp(string path) => Run("--app-reg", path);
    public void UnregisterApp(string path) => Run("--app-unreg", path);

    public IReadOnlyList<HidDeviceInfo> GamingDevices()
    {
        string json = Run("--dev-gaming");
        var result = new List<HidDeviceInfo>();
        using var doc = JsonDocument.Parse(json);
        foreach (var group in doc.RootElement.EnumerateArray())
        {
            string name = group.TryGetProperty("friendlyName", out var n) ? n.GetString() ?? "" : "";
            if (!group.TryGetProperty("devices", out var devices))
                continue;
            foreach (var d in devices.EnumerateArray())
            {
                string? path = d.TryGetProperty("deviceInstancePath", out var p) ? p.GetString() : null;
                bool present = d.TryGetProperty("present", out var pr) && pr.ValueKind == JsonValueKind.True;
                if (!string.IsNullOrEmpty(path))
                    result.Add(new HidDeviceInfo(path, name, present));
            }
        }
        return result;
    }

    /// <summary>Lines look like: --dev-hide "HID\VID_054C&amp;PID_0CE6&amp;MI_03\8&amp;..."</summary>
    public static IReadOnlyList<string> ParseList(string output, string prefix)
    {
        var items = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var m = ListLine().Match(line.Trim());
            if (m.Success && m.Groups[1].Value == prefix)
                items.Add(m.Groups[2].Value);
        }
        return items;
    }

    [GeneratedRegex("^(--[a-z-]+)\\s+\"(.*)\"$")]
    private static partial Regex ListLine();

    string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new HidHideException($"Could not start {_exe}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(Timeout))
        {
            try { p.Kill(); } catch (InvalidOperationException) { }
            throw new HidHideException($"HidHideCLI {string.Join(' ', args)} timed out");
        }

        string output = stdout.Result;
        string error = stderr.Result.Trim();
        _log?.Invoke($"HidHideCLI {string.Join(' ', args.Select(Quote))} -> exit {p.ExitCode}{(error.Length > 0 ? $" ({error})" : "")}");
        if (p.ExitCode != 0)
            throw new HidHideException($"HidHideCLI {args[0]} failed with exit code {p.ExitCode}{(error.Length > 0 ? $": {error}" : "")}");
        return output;
    }

    static string Quote(string a) => a.StartsWith("--", StringComparison.Ordinal) ? a : $"\"{a}\"";
}
