using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DeskMux.App.UI;

namespace DeskMux.App;

public sealed partial class AppController
{
    private OnlineRelease? _availableUpdate;
    private bool _checkingUpdate, _downloadingUpdate;
    public string? UpdateVersion => _availableUpdate?.Version.TrimStart('v', 'V');
    public bool DownloadingUpdate => _downloadingUpdate;
    public event Action? UpdateChanged;

    private static HttpClient UpdateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeskMux/" + AppInfo.Version);
        return client;
    }

    private async Task MonitorUpdatesAsync()
    {
        var cancellation = _lifetime.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
            while (!cancellation.IsCancellationRequested)
            {
                await CheckForUpdatesAsync(quiet: true);
                await Task.Delay(TimeSpan.FromHours(6), cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public async Task CheckForUpdatesAsync(bool quiet = false)
    {
        if (_checkingUpdate || _downloadingUpdate || _disposed) return;
        _checkingUpdate = true;
        try
        {
            using var client = UpdateClient(TimeSpan.FromSeconds(20));
            var json = await client.GetStringAsync(AppInfo.ReleasesApiUrl, _lifetime.Token);
            var release = OnlineUpdate.Select(json, AppInfo.Version);
            var changed = release?.Version != _availableUpdate?.Version;
            _availableUpdate = release;
            UpdateChanged?.Invoke();
            if (release != null)
            {
                if (changed) Notify("DeskMux update available", "Version " + UpdateVersion + " is ready to download. Open DeskMux to restart and update when convenient.");
                if (!quiet && MessageBox.Show("DeskMux " + UpdateVersion + " is available. Download it now? You will be asked before DeskMux restarts.", "DeskMux update", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    await InstallOnlineUpdateAsync();
            }
            else if (!quiet) MessageBox.Show("You are running the newest available complete stable release (" + AppInfo.Version + ").", "DeskMux update");
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception exception)
        {
            Log.Write("update_check_failed", exception.Message);
            if (!quiet) MessageBox.Show("DeskMux could not check for updates. Please try again later.\n\n" + exception.Message, "DeskMux update");
        }
        finally { _checkingUpdate = false; }
    }

    public async Task InstallOnlineUpdateAsync()
    {
        if (_availableUpdate is not { } release || _downloadingUpdate || _disposed) return;
        _downloadingUpdate = true; UpdateChanged?.Invoke();
        var work = Path.Combine(DataDirectory, "updates", "download-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            using var client = UpdateClient(TimeSpan.FromMinutes(15));
            var archive = Path.Combine(work, "DeskMux-update-x64.zip");
            var sums = Path.Combine(work, "SHA256SUMS.txt");
            var cancellation = _lifetime.Token;
            await OnlineUpdate.Download(client, release.Checksums, sums, 1024 * 1024, cancellation);
            await OnlineUpdate.Download(client, release.Package, archive, 512L * 1024 * 1024, cancellation);
            await Task.Run(() => OnlineUpdate.VerifyArchive(archive, File.ReadAllText(sums)), cancellation);
            if (!_disposed) UpdateInstaller.Install(this, archive, release.Version);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception exception)
        {
            Log.Write("update_download_failed", exception.Message);
            if (!_disposed) MessageBox.Show("The update could not be downloaded or verified. Your current installation is unchanged.\n\n" + exception.Message, "DeskMux update");
        }
        finally
        {
            _downloadingUpdate = false;
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (!_disposed) UpdateChanged?.Invoke();
        }
    }
}
