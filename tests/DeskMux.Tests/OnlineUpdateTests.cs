using DeskMux.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

static class OnlineUpdateTests
{
    public static int Run()
    {
        object Release(string tag, bool draft = false, bool prerelease = false, bool complete = true, string? host = null) => new
        {
            tag_name = tag, draft, prerelease,
            assets = (complete ? new[] { "DeskMux-update-x64.zip", "SHA256SUMS.txt" } : new[] { "DeskMux-update-x64.zip" })
                .Select(name => new { name, browser_download_url = (host ?? OnlineUpdate.Repository) + "/releases/download/" + tag + "/" + name })
        };
        var tests = new (string, Action)[]
        {
            ("Updates select newest complete stable release", () => {
                var json = JsonSerializer.Serialize(new[] { Release("v0.2.0"), Release("v0.4.0", draft:true), Release("v0.5.0", prerelease:true), Release("v0.6.0", complete:false), Release("v0.3.0") });
                Check.Equal("v0.3.0", OnlineUpdate.Select(json, "0.1.0+revision")!.Version);
                Check.Equal<OnlineRelease?>(null, OnlineUpdate.Select(json, "0.3.0"));
            }),
            ("Updates reject foreign release assets and prerelease tags", () => {
                var json = JsonSerializer.Serialize(new[] { Release("v0.2.0", host:"https://example.com"), Release("v0.3.0-beta.1") });
                Check.Equal<OnlineRelease?>(null, OnlineUpdate.Select(json, "0.1.0"));
            }),
            ("Update checksums reject tampering and missing or duplicate entries", () => {
                var path = Path.GetTempFileName();
                try {
                    File.WriteAllText(path, "package");
                    var sum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + "  DeskMux-update-x64.zip";
                    OnlineUpdate.VerifyArchive(path, sum);
                    Reject(() => OnlineUpdate.VerifyArchive(path, sum + "\n" + sum));
                    Reject(() => OnlineUpdate.VerifyArchive(path, ""));
                    File.AppendAllText(path, "tampered");
                    Reject(() => OnlineUpdate.VerifyArchive(path, sum));
                } finally { File.Delete(path); }
            }),
            ("Update download enforces size and cancellation", () => {
                var path = Path.GetTempFileName();
                try {
                    using var client = new HttpClient(new DownloadHandler());
                    Reject(() => OnlineUpdate.Download(client, new Uri("https://example.com"), path, 2, default).GetAwaiter().GetResult());
                    OnlineUpdate.Download(client, new Uri("https://example.com"), path, 10, default).GetAwaiter().GetResult();
                    Check.Equal("package", File.ReadAllText(path));
                    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                    try { OnlineUpdate.Download(client, new Uri("https://example.com"), path, 10, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                    catch (OperationCanceledException) { }
                } finally { File.Delete(path); }
            })
        };
        int failed = 0;
        foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
        return failed;
    }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected rejected update"); }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("package") });
    }
}
