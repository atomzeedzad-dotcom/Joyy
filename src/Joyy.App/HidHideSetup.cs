using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Joyy.App;

/// <summary>
/// The HidHide installer bundled inside Joyy.exe (release builds; see tools\fetch-deps.ps1), so
/// nobody has to download it separately. Runs it silently; Windows still asks for admin (UAC).
/// </summary>
internal static class HidHideSetup
{
    public const string Version = "1.5.230";
    const string ResourceName = "HidHideSetup.exe";
    const string Sha256 = "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6";

    public static bool Bundled => typeof(HidHideSetup).Assembly.GetManifestResourceInfo(ResourceName) is not null;

    public readonly record struct Result(bool Installed, bool RestartNeeded, string? Error);

    public static async Task<Result> InstallAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Joyy");
        string exe = Path.Combine(dir, $"HidHide_{Version}_x64.exe");
        try
        {
            Directory.CreateDirectory(dir);
            await using (var source = typeof(HidHideSetup).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("this copy of Joyy does not include the HidHide installer"))
            await using (var file = File.Create(exe))
                await source.CopyToAsync(file);

            await using (var check = File.OpenRead(exe))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(check)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, false, "the bundled installer is damaged (checksum mismatch)");

            int code;
            try
            {
                using var process = Process.Start(new ProcessStartInfo(exe, "/exenoui /qn /norestart") { UseShellExecute = true, Verb = "runas" })
                    ?? throw new InvalidOperationException("the installer did not start");
                await process.WaitForExitAsync();
                code = process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(false, false, "cancelled at the Windows admin prompt");
            }

            return code switch
            {
                0 => new(true, false, null),
                3010 or 1641 => new(true, true, null),
                1602 => new(false, false, "cancelled"),
                _ => new(false, false, $"the HidHide installer failed (exit code {code})"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(false, false, ex.Message);
        }
        finally
        {
            try { File.Delete(exe); } catch (Exception) { /* still in use or already gone */ }
        }
    }
}
