using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiskPartitioner.Helpers;
using Microsoft.Win32;

namespace DiskPartitioner.Services
{
    public class UpdateInfo
    {
        public bool IsSuccess { get; set; } = true;
        public bool HasUpdate { get; set; } = false;
        public string CurrentVersion { get; set; } = "1.0.2";
        public string LatestVersion { get; set; } = "1.0.2";
        public string ReleaseName { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows";
        public string? DownloadUrl { get; set; }
        public string? ExpectedSha256 { get; set; }
        public string Message { get; set; } = string.Empty;

        public string GetEffectiveReleaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(HtmlUrl) && HtmlUrl.Contains("/releases", StringComparison.OrdinalIgnoreCase))
            {
                return HtmlUrl;
            }
            string ver = string.IsNullOrWhiteSpace(LatestVersion) ? CurrentVersion : LatestVersion;
            return $"https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v{ver}";
        }
    }

    public interface IUpdateService
    {
        Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
        string GetCurrentVersion();
        bool IsAutoCheckEnabled();
        void SetAutoCheckEnabled(bool enabled);
        bool ShouldPerformDailyCheck();
        void RecordCheckPerformed();
        bool HasNotifiedForVersion(string version);
        void RecordVersionNotified(string version);
        void OpenUrl(string url);
        bool VerifyFileHash(string filePath, string expectedSha256);
        Task<string?> DownloadInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
        Task<string?> DownloadInstallerAsync(string downloadUrl, string? expectedSha256, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
        bool LaunchInstaller(string installerPath);
    }

    public class UpdateService : IUpdateService
    {
        public const string RepoOwner = "AccessibleTools";
        public const string RepoName = "Disk-Partitioner-for-Windows";
        public const string ReleasesApiUrl = "https://api.github.com/repos/AccessibleTools/Disk-Partitioner-for-Windows/releases/latest";
        public const string RepoWebUrl = "https://github.com/AccessibleTools/Disk-Partitioner-for-Windows";

        private const string RegistrySubKey = @"Software\AccessibleTools\DiskPartitioner";
        private const string RegAutoCheck = "AutoCheckUpdates";
        private const string RegLastCheckUtc = "LastUpdateCheckUtc";
        private const string RegLastNotifiedVersion = "LastNotifiedVersion";

        private readonly HttpClient _httpClient;

        public UpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiskPartitioner-UpdateChecker");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            }
        }

        public string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.2";
        }

        public bool IsAutoCheckEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
                if (key != null)
                {
                    object? val = key.GetValue(RegAutoCheck);
                    if (val is int intVal)
                    {
                        return intVal == 1;
                    }
                }
            }
            catch { }
            return true; // Enabled by default
        }

        public void SetAutoCheckEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
                key?.SetValue(RegAutoCheck, enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        public bool ShouldPerformDailyCheck()
        {
            if (!IsAutoCheckEnabled()) return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
                if (key != null)
                {
                    string? lastStr = key.GetValue(RegLastCheckUtc) as string;
                    if (!string.IsNullOrWhiteSpace(lastStr) && DateTime.TryParse(lastStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastUtc))
                    {
                        // Skip if already checked today (same calendar day or within 24 hours)
                        if (lastUtc.Date == DateTime.UtcNow.Date || (DateTime.UtcNow - lastUtc).TotalHours < 24.0)
                        {
                            return false;
                        }
                    }
                }
            }
            catch { }

            return true; // Check if never checked today
        }

        public void RecordCheckPerformed()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
                key?.SetValue(RegLastCheckUtc, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
            }
            catch { }
        }

        public bool HasNotifiedForVersion(string version)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
                if (key != null)
                {
                    string? notified = key.GetValue(RegLastNotifiedVersion) as string;
                    return string.Equals(notified, version, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        public void RecordVersionNotified(string version)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey);
                key?.SetValue(RegLastNotifiedVersion, version ?? string.Empty, RegistryValueKind.String);
            }
            catch { }
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            string currentVerStr = GetCurrentVersion();
            var info = new UpdateInfo
            {
                CurrentVersion = currentVerStr,
                LatestVersion = currentVerStr,
                HtmlUrl = RepoWebUrl
            };

            try
            {
                using var response = await _httpClient.GetAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);

                // If repo does not have releases yet (404), gracefully report up to date
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    info.IsSuccess = true;
                    info.HasUpdate = false;
                    info.Message = "Disk Partitioner for Windows is up to date.";
                    info.HtmlUrl = $"{RepoWebUrl}/releases/tag/v{currentVerStr}";
                    return info;
                }

                if (!response.IsSuccessStatusCode)
                {
                    info.IsSuccess = false;
                    info.Message = $"Could not retrieve update information from GitHub (HTTP {(int)response.StatusCode}).";
                    return info;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string rawTag = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                string cleanTag = TextSanitizer.SanitizeLabel(rawTag.TrimStart('v', 'V').Trim());
                info.LatestVersion = cleanTag;

                info.ReleaseName = TextSanitizer.SanitizeSpeech(root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : cleanTag);
                info.ReleaseNotes = TextSanitizer.StripAnsiAndControl(root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "", preserveLineBreaks: true);
                string defaultTagUrl = !string.IsNullOrWhiteSpace(rawTag) ? $"{RepoWebUrl}/releases/tag/{rawTag}" : $"{RepoWebUrl}/releases";
                info.HtmlUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() ?? defaultTagUrl : defaultTagUrl;

                // Look for downloadable .exe asset
                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var assetNameElem) &&
                            assetNameElem.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true &&
                            asset.TryGetProperty("browser_download_url", out var dlElem))
                        {
                            info.DownloadUrl = dlElem.GetString();
                            if (asset.TryGetProperty("digest", out var digestElem))
                            {
                                string? digest = digestElem.GetString();
                                if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.ExpectedSha256 = digest["sha256:".Length..].Trim();
                                }
                            }
                            break;
                        }
                    }
                }

                // If digest wasn't directly in the asset, check if ReleaseNotes publishes a SHA256 checksum
                if (string.IsNullOrWhiteSpace(info.ExpectedSha256) && !string.IsNullOrWhiteSpace(info.ReleaseNotes))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(info.ReleaseNotes, @"(?:SHA-?256|Checksum)[:\s]+([a-fA-F0-9]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        info.ExpectedSha256 = match.Groups[1].Value.Trim();
                    }
                }

                // Version Comparison
                if (Version.TryParse(currentVerStr, out var currentVer) && Version.TryParse(cleanTag, out var latestVer))
                {
                    info.HasUpdate = latestVer > currentVer;
                }
                else
                {
                    info.HasUpdate = string.Compare(cleanTag, currentVerStr, StringComparison.OrdinalIgnoreCase) > 0;
                }

                info.IsSuccess = true;
                info.Message = info.HasUpdate
                    ? $"A new version (v{cleanTag}) is available."
                    : "Disk Partitioner for Windows is up to date.";

                return info;
            }
            catch (OperationCanceledException)
            {
                info.IsSuccess = false;
                info.Message = "The update check was cancelled.";
                return info;
            }
            catch (Exception ex)
            {
                info.IsSuccess = false;
                info.Message = $"Unable to connect to GitHub. Please check your internet connection.";
                Debug.WriteLine($"Update check exception: {ex.Message}");
                return info;
            }
        }

        public void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                // Validate that URL is an absolute HTTP/HTTPS address to prevent protocol handler injection (CWE-88)
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open URL '{url}': {ex.Message}");
            }
        }

        public bool VerifyFileHash(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedSha256))
                return false;

            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = sha256.ComputeHash(stream);
                string computed = Convert.ToHexString(hash);
                return computed.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public Task<string?> DownloadInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => DownloadInstallerAsync(downloadUrl, null, progress, cancellationToken);

        public async Task<string?> DownloadInstallerAsync(string downloadUrl, string? expectedSha256, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

            // Restrict downloads strictly to HTTPS endpoints
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            try
            {
                string tempDir = Path.GetTempPath();
                string rawFileName = Path.GetFileName(uri.LocalPath);
                string fileName = (!string.IsNullOrWhiteSpace(rawFileName) && rawFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ? rawFileName
                    : "DiskPartitioner_Update.exe";

                string tempFilePath = Path.Combine(tempDir, fileName);

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    byte[] buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            int progressPercent = (int)((totalRead * 100) / totalBytes.Value);
                            progress?.Report(progressPercent);
                        }
                    }
                }

                // Verify SHA-256 integrity if checksum is provided by GitHub
                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    if (!VerifyFileHash(tempFilePath, expectedSha256))
                    {
                        try { File.Delete(tempFilePath); } catch { }
                        Debug.WriteLine("Downloaded update failed SHA-256 verification and was deleted for security.");
                        return null;
                    }
                }

                return tempFilePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download installer: {ex.Message}");
                return null;
            }
        }

        public bool LaunchInstaller(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) ||
                !File.Exists(installerPath) ||
                !installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch installer: {ex.Message}");
                return false;
            }
        }
    }
}
