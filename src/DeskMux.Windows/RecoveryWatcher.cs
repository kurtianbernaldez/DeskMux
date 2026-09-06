using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskMux.Core;

namespace DeskMux.Windows;

/// <summary>Runs in an independent instance of the executable, with no dependency on the tray dispatcher.</summary>
public static class RecoveryWatcher
{
    public static string ReadyFile(int pid, long ticks, string dataDirectory) =>
        Path.Combine(dataDirectory, $"watchdog-ready-{pid}-{ticks}.txt");

    public static void Run(int parentPid, long parentStartTicks, string dataDirectory)
    {
        var log = new RecoveryLog(dataDirectory);
        var ready = ReadyFile(parentPid, parentStartTicks, dataDirectory);
        try
        {
            Process? parent = null;
            try { parent = Process.GetProcessById(parentPid); } catch (ArgumentException) { }
            if (parent is not null)
            {
                using (parent)
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != parentStartTicks) return;
                    // Opening the process and validating creation time precedes the handshake.
                    // WaitForExit waits on this process handle, not on a reusable numeric PID.
                    _ = parent.Handle;
                    Directory.CreateDirectory(dataDirectory);
                    File.WriteAllText(ready, Environment.ProcessId.ToString());
                    parent.WaitForExit();
                }
            }
            // Let any already-posted asynchronous hide requests drain before restoring.
            Thread.Sleep(120);
            RecoverInternal(dataDirectory, log, parentPid, parentStartTicks);
        }
        catch (Exception exception) { log.Write("recovery.watchdog.failed", exception.Message); }
        finally
        {
            try { File.Delete(ready); } catch { }
        }
    }

    /// <summary>Restores a previous owner's journal only when that exact process is no longer alive.</summary>
    public static int Recover(string dataDirectory, ILog log) => RecoverInternal(dataDirectory, log, null, null);

    private static int RecoverInternal(string directory, ILog log, int? expectedPid, long? expectedTicks)
    {
        try
        {
            var journal = new RecoveryJournal(directory);
            return journal.Recover(log, expectedPid, expectedTicks);
        }
        catch (Exception exception)
        {
            SafeLog(log, "recovery.failed", exception.Message);
            return 0;
        }
    }

    internal static bool OwnerAlive(int pid, long ticks)
    {
        if (pid <= 0 || ticks <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // If access is denied, be conservative: never recover another potentially live manager.
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    internal static void SafeLog(ILog log, string name, string message, object? data = null)
    {
        try { log.Write(name, message, data); } catch { }
    }

    private sealed class RecoveryLog(string directory) : ILog
    {
        public void Write(string eventName, string message, object? data = null)
        {
            try
            {
                var logs = Path.Combine(directory, "logs");
                Directory.CreateDirectory(logs);
                File.AppendAllText(Path.Combine(logs, "recovery.jsonl"),
                    JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow, eventName, message, data }) + Environment.NewLine);
            }
            catch { }
        }
    }
}

internal sealed class RecoveryDocument
{
    public int Version { get; set; } = 1;
    public int OwnerProcessId { get; set; }
    public long OwnerStartTimeUtcTicks { get; set; }
    public List<ManagedWindow> Windows { get; set; } = [];
}

/// <summary>Cross-process serialized, atomically replaced, write-through hide ownership journal.</summary>
internal sealed class RecoveryJournal
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly string _path;
    private readonly string _mutexName;
    private readonly long _ownerTicks;

    internal RecoveryJournal(string directory)
    {
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "recovery.json");
        _mutexName = "Local\\DeskMux.Recovery." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_directory.ToUpperInvariant())))[..24];
        using var owner = Process.GetCurrentProcess();
        _ownerTicks = owner.StartTime.ToUniversalTime().Ticks;
    }

    internal bool Contains(ManagedWindow window) => Locked(() => Read().Windows.Any(item => SameEntry(item, window)));

    internal void Add(ManagedWindow window) => Locked(() =>
    {
        var document = Read();
        if (document.OwnerProcessId != Environment.ProcessId &&
            RecoveryWatcher.OwnerAlive(document.OwnerProcessId, document.OwnerStartTimeUtcTicks))
            throw new IOException("Another DeskMux process owns the window recovery journal.");
        document.OwnerProcessId = Environment.ProcessId;
        document.OwnerStartTimeUtcTicks = _ownerTicks;
        // Clone before saving so later session/layout mutations cannot change recovery ownership.
        var snapshot = JsonSerializer.Deserialize<ManagedWindow>(JsonSerializer.Serialize(window, Json), Json)!;
        snapshot.HiddenByDeskMux = true;
        document.Windows.RemoveAll(item => SameEntry(item, window));
        document.Windows.Add(snapshot);
        Write(document);
        return true;
    });

    internal void Remove(ManagedWindow window) => Locked(() =>
    {
        var document = Read();
        if (document.OwnerProcessId != Environment.ProcessId &&
            RecoveryWatcher.OwnerAlive(document.OwnerProcessId, document.OwnerStartTimeUtcTicks)) return false;
        if (document.Windows.RemoveAll(item => SameEntry(item, window)) > 0) Write(document);
        return true;
    });

    internal int Recover(ILog log, int? expectedPid, long? expectedTicks) => Locked(() =>
    {
        var document = Read();
        if (document.Windows.Count == 0) return 0;
        if (expectedPid.HasValue && (document.OwnerProcessId != expectedPid || document.OwnerStartTimeUtcTicks != expectedTicks))
            return 0;
        if (RecoveryWatcher.OwnerAlive(document.OwnerProcessId, document.OwnerStartTimeUtcTicks)) return 0;

        var restored = 0;
        foreach (var window in document.Windows.ToArray())
        {
            if (!NativeDesktop.IsSameWindow(window))
            {
                document.Windows.Remove(window);
                Write(document);
                continue;
            }
            var result = WindowOperationResult.Fail("Recovery has not yet completed.");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                result = NativeDesktop.Restore(window);
                if (result.Success) break;
                Thread.Sleep(100);
            }
            if (result.Success)
            {
                document.Windows.Remove(window);
                Write(document);
                restored++;
                RecoveryWatcher.SafeLog(log, "recovery.window.restored", window.DisplayTitle, new { window.Handle });
            }
            else RecoveryWatcher.SafeLog(log, "recovery.window.failed", result.Error ?? "Unknown Windows error", new { window.Handle });
        }
        return restored;
    });

    private static bool SameEntry(ManagedWindow first, ManagedWindow second) =>
        first.Handle == second.Handle && first.Fingerprint.ProcessId == second.Fingerprint.ProcessId &&
        first.Fingerprint.ProcessStartTimeUtcTicks == second.Fingerprint.ProcessStartTimeUtcTicks &&
        string.Equals(first.Fingerprint.WindowClass, second.Fingerprint.WindowClass, StringComparison.Ordinal);

    private RecoveryDocument Read()
    {
        if (!File.Exists(_path)) return new RecoveryDocument();
        try { return ReadFile(_path); }
        catch (JsonException)
        {
            if (File.Exists(_path + ".bak")) return ReadFile(_path + ".bak");
            throw new IOException("The recovery journal is unreadable. No additional windows will be hidden.");
        }
    }

    private static RecoveryDocument ReadFile(string path)
    {
        var result = JsonSerializer.Deserialize<RecoveryDocument>(File.ReadAllText(path), Json)
            ?? throw new JsonException("Empty recovery document.");
        if (result.Version != 1 || result.Windows is null || result.Windows.Any(window => window.Fingerprint is null || window.Layout is null))
            throw new JsonException("Invalid recovery document.");
        return result;
    }

    private void Write(RecoveryDocument document)
    {
        Directory.CreateDirectory(_directory);
        var temporary = _path + $".{Environment.ProcessId}.tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, _path);
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Window recovery is busy; no additional windows will be hidden.");
            return action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
