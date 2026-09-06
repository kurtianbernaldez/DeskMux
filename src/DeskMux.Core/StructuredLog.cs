using System.Text.Json;

namespace DeskMux.Core;

public sealed class StructuredLog : ILog, IDisposable
{
    private readonly object _gate = new();
    public string DirectoryPath { get; }
    public StructuredLog(string directory)
    {
        DirectoryPath = directory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, "deskmux-????????.jsonl")
                         .Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-14)))
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Write(string eventName, string message, object? data = null)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, $"deskmux-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                // Bound one day's log in pathological repeated-failure situations.
                if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) return;
                var record = JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow, eventName, message, data });
                File.AppendAllText(path, record + Environment.NewLine);
            }
        }
        catch (Exception) { /* Diagnostics must never break desktop recovery. */ }
    }

    public void Dispose() { }
}
