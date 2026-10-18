using System;
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
        private UpdateInfo? _currentInfo;

        public UpdateCheckDialog(IUpdateService? updateService = null, IScreenReaderService? screenReader = null, UpdateInfo? preFetchedInfo = null)
        {
            InitializeComponent();
            _updateService = updateService ?? new UpdateService();
            _screenReader = screenReader ?? new ScreenReaderService();
            _currentInfo = preFetchedInfo;

            chkDailyCheck.IsChecked = _updateService.IsAutoCheckEnabled();

            Loaded += UpdateCheckDialog_Loaded;
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
                var info = await _updateService.CheckForUpdatesAsync();
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
                pnlReleaseNotes.Visibility = Visibility.Collapsed;
                btnDownload.Visibility = Visibility.Collapsed;
                btnViewReleaseNotes.Visibility = Visibility.Visible;
                btnViewReleaseNotes.Content = "View Releases on GitHub";
                Height = 300;
                Width = 500;
                _screenReader.Speak("Unable to check for updates. Please check your internet connection.");
                btnClose.Focus();
                return;
            }

            if (info.HasUpdate)
            {
                txtStatus.Text = "A new version of Disk Partitioner for Windows is available!";
                txtVersionInfo.Text = $"Installed Version: {info.CurrentVersion}   Latest Version: {info.LatestVersion}";
                txtReleaseNotesHeader.Text = $"Release Notes (Version {info.LatestVersion}):";

                string rawNotes = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? info.ReleaseName : info.ReleaseNotes;
                string cleanNotes = TextSanitizer.StripAnsiAndControl(rawNotes, preserveLineBreaks: true);
                txtReleaseNotes.Text = cleanNotes.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                txtReleaseNotes.SelectionStart = 0;
                txtReleaseNotes.SelectionLength = 0;

                pnlReleaseNotes.Visibility = Visibility.Visible;
                btnDownload.Visibility = !string.IsNullOrWhiteSpace(info.DownloadUrl) ? Visibility.Visible : Visibility.Collapsed;
                btnViewReleaseNotes.Visibility = Visibility.Visible;
                btnViewReleaseNotes.Content = "See What's New";
                Height = 480;
                Width = 540;

                _screenReader.Speak($"Update available: Version {info.LatestVersion}. Press Tab to view release notes or download.");
                if (btnDownload.Visibility == Visibility.Visible)
                {
                    btnDownload.Focus();
                }
                else
                {
                    txtReleaseNotes.Focus();
                }
            }
            else
            {
                txtStatus.Text = "Disk Partitioner for Windows is up to date.";
                txtVersionInfo.Text = $"You have the latest version: Version {info.CurrentVersion}";
                pnlReleaseNotes.Visibility = Visibility.Collapsed;
                btnDownload.Visibility = Visibility.Collapsed;
                btnViewReleaseNotes.Visibility = Visibility.Visible;
                btnViewReleaseNotes.Content = "View Release Notes";
                Height = 300;
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

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentInfo?.DownloadUrl))
            {
                _updateService.OpenUrl(_currentInfo.DownloadUrl);
            }
            else if (!string.IsNullOrWhiteSpace(_currentInfo?.HtmlUrl))
            {
                _updateService.OpenUrl(_currentInfo.HtmlUrl);
            }
        }

        private void BtnViewReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            string url = _currentInfo?.GetEffectiveReleaseUrl() ??
                         $"{UpdateService.RepoWebUrl}/releases/tag/v{_updateService.GetCurrentVersion()}";
            _updateService.OpenUrl(url);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
