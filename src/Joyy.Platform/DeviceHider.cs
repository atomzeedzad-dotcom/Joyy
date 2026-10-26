using System.Text.Json;
using Joyy.Core;

namespace Joyy.Platform;

/// <summary>
/// Hides the physical DualSense from games with HidHide while letting Joyy itself keep reading it.
/// </summary>
/// <remarks>
/// Safety invariant: the user's controller must always be recoverable. Every change is written to
/// a journal BEFORE it is made, and <see cref="Restore"/> undoes exactly what the journal lists:
/// devices someone else hid, a cloak that was already on, or an app someone else registered are
/// left alone. If Joyy dies without restoring, <see cref="RecoverIfNeeded"/> (run at the next
/// start, or via the restore command) finishes the job.
/// </remarks>
public sealed class DeviceHider
{
    /// <param name="OwnerPid">The Joyy process that hid the devices; its guardian restores them if it dies.</param>
    public sealed record Journal(bool CloakWasOn, string? RegisteredApp, List<string> HiddenDevices, int OwnerPid = 0);

    public static string DefaultJournalPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Joyy", "hidden-devices.json");

    readonly IHidHide _hidHide;
    readonly string _appPath;
    readonly string _journalPath;
    readonly int _ownerPid;
    readonly object _gate = new();
    readonly Mutex _mutex;

    public DeviceHider(IHidHide hidHide, string appPath, string? journalPath = null, int? ownerPid = null)
    {
        _hidHide = hidHide;
        _appPath = appPath;
        _journalPath = journalPath ?? DefaultJournalPath;
        _ownerPid = ownerPid ?? Environment.ProcessId;

        // Joyy, its guardian process and a restarted Joyy all touch the same journal and HidHide
        // state, so serialize them across processes (one mutex per journal file).
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(_journalPath).ToLowerInvariant())))[..16];
        _mutex = new Mutex(false, @"Local\Joyy.Journal." + key);
    }

    /// <summary>In-process lock plus the cross-process mutex; dispose on the same thread that entered.</summary>
    Hold Enter() => new(_gate, _mutex);

    sealed class Hold : IDisposable
    {
        readonly object _gate;
        readonly Mutex _mutex;
        bool _owned;

        public Hold(object gate, Mutex mutex)
        {
            _gate = gate;
            _mutex = mutex;
            Monitor.Enter(_gate);
            try
            {
                try { _owned = _mutex.WaitOne(TimeSpan.FromSeconds(20)); }
                catch (AbandonedMutexException) { _owned = true; } // the previous owner died; the journal is what matters
            }
            catch
            {
                Monitor.Exit(_gate);
                throw;
            }
        }

        public void Dispose()
        {
            if (_owned)
                _mutex.ReleaseMutex();
            Monitor.Exit(_gate);
        }
    }

    /// <summary>Devices Joyy currently has hidden (empty when nothing is hidden by Joyy).</summary>
    public IReadOnlyList<string> HiddenByJoyy
    {
        get { using var _ = Enter(); return ReadJournal()?.HiddenDevices ?? new List<string>(); }
    }

    public bool HasPendingRestore
    {
        get { using var _ = Enter(); return File.Exists(_journalPath); }
    }

    /// <summary>Undoes a previous run that ended without restoring. Returns true if there was something to undo.</summary>
    public bool RecoverIfNeeded()
    {
        using var _ = Enter();
        if (!File.Exists(_journalPath))
            return false;
        RestoreLocked();
        return true;
    }

    /// <summary>
    /// For the guardian: restores only if the journal belongs to the (now dead) process
    /// <paramref name="pid"/>. A newer Joyy that already took over the journal is left alone.
    /// </summary>
    public bool RestoreIfOwnedBy(int pid)
    {
        using var _ = Enter();
        var journal = ReadJournal();
        if (journal is null || (journal.OwnerPid != pid && journal.OwnerPid != 0))
            return false;
        RestoreLocked();
        return true;
    }

    /// <summary>
    /// Registers Joyy with HidHide, hides every connected DualSense and turns the cloak on.
    /// Idempotent: call again when a controller (re)connects. Returns the number of devices Joyy hides.
    /// </summary>
    public int Apply()
    {
        using var _ = Enter();
        var journal = ReadJournal() ?? new Journal(_hidHide.IsCloakOn(), null, new List<string>());
        journal = journal with { OwnerPid = _ownerPid };
        WriteJournal(journal);

        // Joyy must stay able to open the device it hides, or it would lose the controller too.
        if (!_hidHide.RegisteredApps().Contains(_appPath, StringComparer.OrdinalIgnoreCase))
        {
            journal = journal with { RegisteredApp = _appPath };
            WriteJournal(journal);
            _hidHide.RegisterApp(_appPath);
        }

        var alreadyHidden = new HashSet<string>(_hidHide.HiddenDevices(), StringComparer.OrdinalIgnoreCase);
        foreach (var device in _hidHide.GamingDevices())
        {
            if (!device.Present || !DeviceIds.IsDualSenseInstancePath(device.InstancePath) || alreadyHidden.Contains(device.InstancePath))
                continue;
            if (!journal.HiddenDevices.Contains(device.InstancePath, StringComparer.OrdinalIgnoreCase))
                journal.HiddenDevices.Add(device.InstancePath);
            WriteJournal(journal); // write-ahead: recorded before it happens
            _hidHide.Hide(device.InstancePath);
        }

        if (!_hidHide.IsCloakOn())
            _hidHide.SetCloak(true);

        return journal.HiddenDevices.Count;
    }

    /// <summary>Makes the DualSense visible again and puts HidHide back the way Joyy found it.</summary>
    public void Restore()
    {
        using var _ = Enter();
        RestoreLocked();
    }

    void RestoreLocked()
    {
        var journal = ReadJournal();
        if (journal is null)
            return;

        var errors = new List<Exception>();
        void Try(Action action)
        {
            try { action(); }
            catch (Exception ex) { errors.Add(ex); }
        }

        foreach (var device in journal.HiddenDevices)
            Try(() => _hidHide.Unhide(device));
        if (!journal.CloakWasOn)
            Try(() => _hidHide.SetCloak(false));
        if (journal.RegisteredApp is { } app)
            Try(() => _hidHide.UnregisterApp(app));

        if (errors.Count > 0)
            throw new AggregateException("Could not fully restore controller visibility; will retry on next start.", errors);

        File.Delete(_journalPath);
    }

    Journal? ReadJournal()
    {
        if (!File.Exists(_journalPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Journal>(File.ReadAllText(_journalPath));
        }
        catch (JsonException)
        {
            // Corrupt journal: assume the worst so Restore still unhides every DualSense we can see.
            var dualSenses = _hidHide.HiddenDevices().Where(DeviceIds.IsDualSenseInstancePath).ToList();
            return new Journal(CloakWasOn: false, RegisteredApp: null, dualSenses);
        }
    }

    void WriteJournal(Journal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        string temp = _journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _journalPath, overwrite: true);
    }
}
