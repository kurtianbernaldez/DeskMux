using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeskMux.Core;

public sealed record OnlineRelease(string Version, Uri Package, Uri Checksums);

public static class OnlineUpdate
{
    public const string Repository = "https://github.com/kurtianbernaldez/DeskMux";
    public static OnlineRelease? Select(string json, string installed)
    {
        if (!ReleaseVersion.TryParse(installed, out var current)) throw new InvalidDataException("Invalid installed version.");
        using var document = JsonDocument.Parse(json);
        OnlineRelease? result = null;
        var latest = current;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString();
            if (!ReleaseVersion.TryParse(tag, out var version) || version.Prerelease.Length != 0 || version.CompareTo(latest) <= 0) continue;
            Uri? package = null, checksums = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name is not ("DeskMux-update-x64.zip" or "SHA256SUMS.txt")) continue;
                if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
                    || !url.AbsoluteUri.StartsWith(Repository + "/releases/download/" + Uri.EscapeDataString(tag!) + "/", StringComparison.Ordinal)) continue;
                if (name == "DeskMux-update-x64.zip") package = url; else checksums = url;
            }
            if (package == null || checksums == null) continue;
            latest = version; result = new(tag!, package, checksums);
        }
        return result;
    }

    public static async Task Download(HttpClient client, Uri url, string destination, long limit, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update download is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        await using var output = File.Create(destination);
        var buffer = new byte[81920]; long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellation)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Update download is too large.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }
    }

    public static void VerifyArchive(string archive, string checksums)
    {
        var matches = checksums.Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1] == "DeskMux-update-x64.zip").ToArray();
        using var stream = File.OpenRead(archive);
        if (matches.Length != 1 || !Convert.ToHexString(SHA256.HashData(stream)).Equals(matches[0][0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update failed verification. Please try again.");
    }
}
