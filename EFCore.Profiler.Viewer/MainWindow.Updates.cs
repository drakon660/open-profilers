using System.Threading;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Velopack;
using Velopack.Sources;

namespace EFCore.Profiler.Viewer;

public partial class MainWindow
{
    private const string DefaultLocalUpdateFeedPath = @"C:\efcore-profiler";
    private int _updateCheckRunning;
    private int _updateApplyRunning;
    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdateInfo;

    private async Task CheckForViewerUpdateAvailabilityAsync(bool manualRequest)
    {
        if (Interlocked.Exchange(ref _updateCheckRunning, 1) == 1)
        {
            if (manualRequest)
                SetStatus("Update check is already running.", StatusKind.Warning);
            return;
        }

        try
        {
            var channel = ReadEnvironment("EFCORE_PROFILER_VIEWER_UPDATE_CHANNEL");
            var options = string.IsNullOrWhiteSpace(channel)
                ? new UpdateOptions()
                : new UpdateOptions { ExplicitChannel = channel };
            var manager = CreateUpdateManager(options, out var sourceDescription);
            _updateManager = manager;

            var isPortable = manager.IsPortable;
            if (isPortable)
            {
                ApplyBuildVersionToTitle();
                if (!manualRequest)
                {
                    _pendingUpdateInfo = null;
                    SetUpdateButtonState(visible: false, enabled: false);
                    return;
                }
            }

            if (!manager.IsInstalled && !manualRequest)
            {
                ApplyBuildVersionToTitle();
                _pendingUpdateInfo = null;
                SetUpdateButtonState(visible: false, enabled: false);
                return;
            }

            if (!isPortable && manager.IsInstalled)
                ApplyBuildVersionToTitle(manager.CurrentVersion?.ToString());

            if (manualRequest)
                SetStatus($"Checking for viewer updates from {sourceDescription}...", StatusKind.Info);

            var updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo is null)
            {
                _pendingUpdateInfo = null;
                SetUpdateButtonState(visible: false, enabled: false);
                if (manualRequest)
                    SetStatus("Viewer is already up to date.", StatusKind.Info);
                return;
            }

            _pendingUpdateInfo = updateInfo;
            SetUpdateButtonState(visible: true, enabled: true);
            SetStatus(
                isPortable
                    ? "A newer EF viewer version is available. Portable mode can detect updates, but applying them may require installed mode."
                    : "A new EF viewer version is available. Click Update to apply.",
                StatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"Update check failed: {FormatException(ex)}", StatusKind.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }

    private async void UpdateNow_Click(object? sender, RoutedEventArgs e)
    {
        await ApplyPendingViewerUpdateAsync();
    }

    private async Task ApplyPendingViewerUpdateAsync()
    {
        if (Interlocked.Exchange(ref _updateApplyRunning, 1) == 1)
        {
            SetStatus("Update is already in progress.", StatusKind.Warning);
            return;
        }

        try
        {
            if (_updateManager is null || _pendingUpdateInfo is null)
            {
                await CheckForViewerUpdateAvailabilityAsync(manualRequest: true);
                if (_updateManager is null || _pendingUpdateInfo is null)
                    return;
            }

            SetUpdateButtonState(visible: true, enabled: false, caption: "Updating...");
            SetStatus("Downloading viewer update...", StatusKind.Warning);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdateInfo);
            SetStatus("Viewer update downloaded. Restarting to apply update...", StatusKind.Warning);
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdateInfo);
        }
        catch (Exception ex)
        {
            SetUpdateButtonState(visible: true, enabled: true);
            SetStatus($"Update failed: {FormatException(ex)}", StatusKind.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _updateApplyRunning, 0);
        }
    }

    private static string ReadEnvironment(string key)
    {
        return Environment.GetEnvironmentVariable(key)?.Trim() ?? string.Empty;
    }

    private static bool ReadEnvironmentAsBool(string key, bool defaultValue)
    {
        var raw = ReadEnvironment(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static UpdateManager CreateUpdateManager(UpdateOptions options, out string sourceDescription)
    {
        var feedUrl = ReadEnvironment("EFCORE_PROFILER_VIEWER_UPDATE_FEED_URL");
        if (!string.IsNullOrWhiteSpace(feedUrl))
        {
            sourceDescription = "local/http feed";
            return new UpdateManager(feedUrl, options);
        }

        var repoUrl = ReadEnvironment("EFCORE_PROFILER_VIEWER_UPDATE_REPO_URL");
        if (!string.IsNullOrWhiteSpace(repoUrl))
        {
            var includePreRelease = ReadEnvironmentAsBool("EFCORE_PROFILER_VIEWER_UPDATE_PRERELEASE", defaultValue: false);
            var token = ReadEnvironment("EFCORE_PROFILER_VIEWER_UPDATE_GITHUB_TOKEN");
            sourceDescription = "GitHub Releases";
            return new UpdateManager(new GithubSource(repoUrl, token, includePreRelease), options);
        }

        sourceDescription = $"local feed ({DefaultLocalUpdateFeedPath})";
        return new UpdateManager(DefaultLocalUpdateFeedPath, options);
    }

    private void SetUpdateButtonState(bool visible, bool enabled, string caption = "Update")
    {
        void Apply()
        {
            UpdateButton.IsVisible = visible;
            UpdateButton.IsEnabled = enabled;
            UpdateButton.Content = caption;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }
}
