// UpdateChecker.cs
// Add to FlexTool project to enable automatic update detection

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FlexTool;

/// <summary>
/// Checks GitHub releases for newer versions of FlexTool.
/// Runs once at startup and periodically in the background.
/// </summary>
public static class UpdateChecker
{
    // Matches the version in MainWindow.Changelog.cs (currently "1.3.0")
    public const string CurrentVersion = "1.3.0";

    private static readonly string VersionCacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlexTool", "version_check.json");

    private static DateTime _lastCheckTime = DateTime.MinValue;
    private const int CheckIntervalHours = 24;

    // GitHub API endpoint (replace with your actual repo)
    private const string GitHubApi = 
        "https://api.github.com/repos/YourUsername/FlexTool/releases/latest";

    public record VersionInfo(
        string Tag,
        string Name,
        string Body,
        string DownloadUrl,
        DateTime Published
    );

    /// <summary>
    /// Check for updates on app startup (non-blocking, runs in background).
    /// </summary>
    public static void CheckForUpdatesAsync(Action<VersionInfo> onUpdateFound, Action<string>? onError = null)
    {
        Task.Run(async () =>
        {
            try
            {
                // Skip if checked recently
                if (DateTime.UtcNow - _lastCheckTime < TimeSpan.FromHours(CheckIntervalHours))
                    return;

                var latest = await FetchLatestReleaseAsync();
                if (latest == null) return;

                _lastCheckTime = DateTime.UtcNow;

                // Compare versions: "1.3.0" -> [1, 3, 0]
                if (IsNewerVersion(latest.Tag))
                {
                    // Cache the result so we don't spam the user
                    await CacheVersionCheckAsync(latest);

                    // Invoke callback on main dispatcher thread (safe for UI)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        onUpdateFound?.Invoke(latest);
                    });
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Update check failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Fetch the latest release info from GitHub.
    /// </summary>
    private static async Task<VersionInfo?> FetchLatestReleaseAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "FlexTool-UpdateChecker");

            var response = await client.GetAsync(GitHubApi);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var name = root.GetProperty("name").GetString() ?? tagName;
            var body = root.GetProperty("body").GetString() ?? "";
            var publishedAt = root.GetProperty("published_at").GetString() ?? "";
            var published = DateTime.TryParse(publishedAt, out var date) ? date : DateTime.UtcNow;

            // Extract download URL from assets (FlexTool.exe)
            var downloadUrl = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.GetProperty("name").GetString();
                    if (assetName?.EndsWith(".exe") == true || assetName?.EndsWith(".zip") == true)
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        break;
                    }
                }
            }

            return new VersionInfo(tagName, name, body, downloadUrl, published);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compare version strings: "1.3.0" > "1.2.5" = true
    /// </summary>
    private static bool IsNewerVersion(string latestTag)
    {
        try
        {
            // Normalize tags: remove "v" prefix if present
            var latest = latestTag.TrimStart('v');
            var current = CurrentVersion;

            var latestParts = latest.Split('.');
            var currentParts = current.Split('.');

            for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
            {
                if (!int.TryParse(latestParts[i], out var l) || 
                    !int.TryParse(currentParts[i], out var c))
                    return false;

                if (l > c) return true;
                if (l < c) return false;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cache the version check result to avoid repeated notifications.
    /// </summary>
    private static async Task CacheVersionCheckAsync(VersionInfo info)
    {
        try
        {
            var dir = Path.GetDirectoryName(VersionCacheFile)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var cache = new
            {
                Version = info.Tag,
                CheckedAt = DateTime.UtcNow,
                Name = info.Name,
                Body = info.Body,
                DownloadUrl = info.DownloadUrl
            };

            var json = JsonSerializer.Serialize(cache, 
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(VersionCacheFile, json);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Get the cached version check, or null if not found/stale.
    /// </summary>
    public static VersionInfo? GetCachedUpdateInfo()
    {
        try
        {
            if (!File.Exists(VersionCacheFile))
                return null;

            var json = File.ReadAllText(VersionCacheFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("Version").GetString() ?? "";
            var name = root.GetProperty("Name").GetString() ?? tag;
            var body = root.GetProperty("Body").GetString() ?? "";
            var downloadUrl = root.GetProperty("DownloadUrl").GetString() ?? "";

            // Invalidate stale caches (older than 7 days)
            if (root.TryGetProperty("CheckedAt", out var checkedAtProp)
                && checkedAtProp.TryGetDateTime(out var checkedAt)
                && DateTime.UtcNow - checkedAt > TimeSpan.FromDays(7))
                return null;

            // Invalidate if the cached version is no longer newer than the
            // running version (e.g. user already updated).
            if (!IsNewerVersion(tag))
                return null;

            return new VersionInfo(tag, name, body, downloadUrl, DateTime.UtcNow);
        }
        catch
        {
            return null;
        }
    }
}

// Usage in MainWindow.xaml.cs:
// 
// In MainWindow_Loaded():
// UpdateChecker.CheckForUpdatesAsync(
//     onUpdateFound: (info) =>
//     {
//         ShowUpdateNotificationCard(info);
//         ShowToast("Update Available", $"FlexTool {info.Tag} is ready to download", ToastService.ToastType.Info);
//     },
//     onError: (err) => Log.Error(err)
// );
