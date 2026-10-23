using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiskPartitioner.Helpers;
using DiskPartitioner.Services;

namespace DiskPartitioner.Views
{
    public partial class UpdateCheckDialog : Window
    {
        private readonly IUpdateService _updateService;
        private readonly IScreenReaderService _screenReader;
        private readonly CancellationTokenSource _cts = new();
        private UpdateInfo? _currentInfo;
        private bool _isDownloading = false;

        public UpdateCheckDialog(IUpdateService? updateService = null, IScreenReaderService? screenReader = null, UpdateInfo? preFetchedInfo = null)
        {
            InitializeComponent();
            _updateService = updateService ?? new UpdateService();
            _screenReader = screenReader ?? new ScreenReaderService();
            _currentInfo = preFetchedInfo;

            chkDailyCheck.IsChecked = _updateService.IsAutoCheckEnabled();

            Loaded += UpdateCheckDialog_Loaded;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    if (_isDownloading)
                    {
                        _cts.Cancel();
                    }
                    Close();
                }
            };
        }

        private async void UpdateCheckDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (_currentInfo != null)
            {
                ApplyUpdateInfo(_currentInfo);
                return;
            }

            txtStatus.Text = "Checking for updates...";
            txtVersionInfo.Text = $"Installed Version: {_updateService.GetCurrentVersion()}";

            try
            {
                var info = await _updateService.CheckForUpdatesAsync(_cts.Token);
                _updateService.RecordCheckPerformed();
                _currentInfo = info;
                ApplyUpdateInfo(info);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Unable to check for updates.";
                txtVersionInfo.Text = ex.Message;
                _screenReader.Speak("Unable to check for updates. Please check your internet connection.");
                btnClose.Focus();
            }
        }

        private void ApplyUpdateInfo(UpdateInfo info)
        {
            if (!info.IsSuccess)
            {
                txtStatus.Text = "Unable to check for updates.";
                txtVersionInfo.Text = info.Message;
                pnlDownloadProgress.Visibility = Visibility.Collapsed;
                btnDownloadInstall.Visibility = Visibility.Collapsed;
                btnSeeWhatsNew.Visibility = Visibility.Collapsed;
                btnNotNow.Visibility = Visibility.Collapsed;
                btnViewReleaseNotes.Visibility = Visibility.Visible;
                btnViewReleaseNotes.Content = "View Releases on GitHub";
                btnClose.Visibility = Visibility.Visible;
                Height = 220;
                Width = 500;
                _screenReader.Speak("Unable to check for updates. Please check your internet connection.");
                btnClose.Focus();
                return;
            }

            if (info.HasUpdate)
            {
                Title = "Update Available - Disk Partitioner for Windows";
                txtStatus.Text = $"A new version of Disk Partitioner (v{info.LatestVersion}) is available!";
                txtVersionInfo.Text = $"Installed Version: {info.CurrentVersion}   Latest Version: {info.LatestVersion}";
                txtDownloadStatus.Text = "Would you like to download and install this update now?";

                pnlDownloadProgress.Visibility = Visibility.Visible;
                btnDownloadInstall.Visibility = Visibility.Visible;
                btnSeeWhatsNew.Visibility = Visibility.Visible;
                btnNotNow.Visibility = Visibility.Visible;

                btnViewReleaseNotes.Visibility = Visibility.Collapsed;
                btnClose.Visibility = Visibility.Collapsed;

                Height = 260;
                Width = 520;

                _screenReader.Speak($"Update available: Version {info.LatestVersion}. Press Tab to download and install, or see what is new.");
                btnDownloadInstall.Focus();
            }
            else
            {
                Title = "Up to Date - Disk Partitioner for Windows";
                txtStatus.Text = "Disk Partitioner for Windows is up to date.";
                txtVersionInfo.Text = $"You have the latest version (Version {info.CurrentVersion}).";

                pnlDownloadProgress.Visibility = Visibility.Collapsed;
                btnDownloadInstall.Visibility = Visibility.Collapsed;
                btnSeeWhatsNew.Visibility = Visibility.Collapsed;
                btnNotNow.Visibility = Visibility.Collapsed;

                btnViewReleaseNotes.Visibility = Visibility.Visible;
                btnViewReleaseNotes.Content = "View Release Notes";
                btnClose.Visibility = Visibility.Visible;

                Height = 220;
                Width = 500;

                _screenReader.Speak($"Disk Partitioner for Windows is up to date. Version {info.CurrentVersion}.");
                btnViewReleaseNotes.Focus();
            }
        }

        private void ChkDailyCheck_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = chkDailyCheck.IsChecked ?? true;
            _updateService.SetAutoCheckEnabled(isChecked);
        }

        private async void BtnDownloadInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            string? downloadUrl = _currentInfo?.DownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                // Fallback to browser if installer asset is not directly available
                string fallbackUrl = _currentInfo?.GetEffectiveReleaseUrl() ?? UpdateService.RepoWebUrl;
                _updateService.OpenUrl(fallbackUrl);
                Close();
                return;
            }

            _isDownloading = true;
            btnDownloadInstall.IsEnabled = false;
            btnSeeWhatsNew.IsEnabled = false;
            btnNotNow.IsEnabled = false;
            pbDownload.Visibility = Visibility.Visible;
            pbDownload.Value = 0;

            txtDownloadStatus.Text = "Downloading update from GitHub...";
            _screenReader.Speak("Downloading update from GitHub.", interrupt: true);

            var progress = new Progress<int>(percent =>
            {
                pbDownload.Value = percent;
                txtDownloadStatus.Text = $"Downloading update... {percent}%";
                if (percent % 25 == 0 && percent > 0 && percent < 100)
                {
                    _screenReader.Speak($"Download progress: {percent} percent.", interrupt: false);
                }
            });

            try
            {
                string? expectedSha256 = _currentInfo?.ExpectedSha256;
                string? installerPath = await _updateService.DownloadInstallerAsync(downloadUrl, expectedSha256, progress, _cts.Token);
                if (!string.IsNullOrEmpty(installerPath) && File.Exists(installerPath))
                {
                    txtDownloadStatus.Text = "Download complete. Starting installer...";
                    _screenReader.Speak("Download complete. Starting installer.", interrupt: true);

                    await Task.Delay(600);

                    bool launched = _updateService.LaunchInstaller(installerPath);
                    if (launched)
                    {
                        // Cleanly shut down the application so installer can overwrite binaries
                        Application.Current.Shutdown();
                        return;
                    }
                    else
                    {
                        MessageBox.Show(this, "Failed to launch the downloaded installer.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    txtDownloadStatus.Text = "Failed to download update. Please try again or visit GitHub.";
                    _screenReader.Speak("Failed to download update.", interrupt: true);
                    btnDownloadInstall.IsEnabled = true;
                    btnSeeWhatsNew.IsEnabled = true;
                    btnNotNow.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                txtDownloadStatus.Text = $"Download error: {ex.Message}";
                _screenReader.Speak("Update download error.", interrupt: true);
                btnDownloadInstall.IsEnabled = true;
                btnSeeWhatsNew.IsEnabled = true;
                btnNotNow.IsEnabled = true;
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private void BtnSeeWhatsNew_Click(object sender, RoutedEventArgs e)
        {
            string url = _currentInfo?.GetEffectiveReleaseUrl() ??
                         $"{UpdateService.RepoWebUrl}/releases/tag/v{_currentInfo?.LatestVersion ?? _updateService.GetCurrentVersion()}";
            _updateService.OpenUrl(url);
            _screenReader.Speak("Opening release notes on GitHub.");
        }

        private void BtnViewReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            string url = _currentInfo?.GetEffectiveReleaseUrl() ??
                         $"{UpdateService.RepoWebUrl}/releases/tag/v{_updateService.GetCurrentVersion()}";
            _updateService.OpenUrl(url);
            _screenReader.Speak("Opening release notes on GitHub.");
        }

        private void BtnNotNow_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                _cts.Cancel();
            }
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_isDownloading)
            {
                _cts.Cancel();
            }
            base.OnClosed(e);
        }
    }
}
