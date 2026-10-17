using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;

namespace DiskPartitioner.Views
{
    public partial class CreatePartitionDialog : Window
    {
        public long PartitionSizeMb { get; private set; }
        public string? SelectedDriveLetter { get; private set; }
        public string? SelectedFileSystem { get; private set; }
        public string VolumeLabel { get; private set; } = string.Empty;
        public bool QuickFormat { get; private set; } = true;

        private readonly long _maxAvailableMb;

        private int _lastUnitIndex = 0; // 0 = GB, 1 = MB
        private bool _isUpdatingUnit = false;

        public CreatePartitionDialog(PartitionItem unallocatedSpace)
        {
            InitializeComponent();
            _maxAvailableMb = Math.Max(1, unallocatedSpace.SizeBytes / (1024 * 1024));

            txtSpaceInfo.Text = $"Available: {unallocatedSpace.FormattedSize} ({_maxAvailableMb:N0} MB) on Disk {unallocatedSpace.DiskIndex}";

            _isUpdatingUnit = true;
            if (_maxAvailableMb >= 1024)
            {
                cmbSizeUnit.SelectedIndex = 0; // GB
                _lastUnitIndex = 0;
                txtSizeValue.Text = Math.Round(_maxAvailableMb / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                cmbSizeUnit.SelectedIndex = 1; // MB
                _lastUnitIndex = 1;
                txtSizeValue.Text = _maxAvailableMb.ToString();
            }
            _isUpdatingUnit = false;

            PopulateDriveLetters();

            Loaded += (s, e) => txtSizeValue.Focus();
        }

        private void PopulateDriveLetters()
        {
            var usedLetters = DriveInfo.GetDrives()
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();

            var letters = new List<string> { "(None)" };
            for (char c = 'A'; c <= 'Z'; c++)
            {
                if (!usedLetters.Contains(c))
                {
                    letters.Add($"{c}:");
                }
            }

            cmbDriveLetter.ItemsSource = letters;
            cmbDriveLetter.SelectedIndex = letters.Count > 1 ? 1 : 0;
        }

        private void CmbSizeUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUnit || txtSizeValue == null) return;

            int newIndex = cmbSizeUnit.SelectedIndex;
            if (newIndex == _lastUnitIndex) return;

            string raw = txtSizeValue.Text.Trim();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) ||
                double.TryParse(raw, out val))
            {
                _isUpdatingUnit = true;
                if (newIndex == 0 && _lastUnitIndex == 1) // MB to GB
                {
                    txtSizeValue.Text = Math.Round(val / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (newIndex == 1 && _lastUnitIndex == 0) // GB to MB
                {
                    txtSizeValue.Text = Math.Round(val * 1024.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                _isUpdatingUnit = false;
            }

            _lastUnitIndex = newIndex;
        }

        private void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSizeUnit.SelectedIndex == 0)
            {
                txtSizeValue.Text = Math.Round(_maxAvailableMb / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                txtSizeValue.Text = _maxAvailableMb.ToString();
            }
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string raw = txtSizeValue.Text.Trim();
            if ((!double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double inputVal) &&
                 !double.TryParse(raw, out inputVal)) || inputVal <= 0)
            {
                MessageBox.Show(this, "Please enter a valid partition size (e.g. 50 GB or 51200 MB).", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtSizeValue.Focus();
                return;
            }

            long sizeMb = cmbSizeUnit.SelectedIndex == 0
                ? (long)Math.Round(inputVal * 1024.0)
                : (long)Math.Round(inputVal);

            if (sizeMb <= 0)
            {
                MessageBox.Show(this, "Partition size must be greater than 0.", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtSizeValue.Focus();
                return;
            }

            if (sizeMb > _maxAvailableMb)
            {
                string maxDisp = cmbSizeUnit.SelectedIndex == 0
                    ? $"{Math.Round(_maxAvailableMb / 1024.0, 2):N2} GB"
                    : $"{_maxAvailableMb:N0} MB";

                MessageBox.Show(this, $"The entered size exceeds available unallocated space ({maxDisp}).", "Size Exceeds Capacity", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtSizeValue.Focus();
                return;
            }

            PartitionSizeMb = sizeMb;

            string letter = cmbDriveLetter.SelectedItem?.ToString() ?? "(None)";
            SelectedDriveLetter = letter.StartsWith("(") ? null : letter.TrimEnd(':');

            if (cmbFileSystem.SelectedItem is ComboBoxItem fsItem)
            {
                string fs = fsItem.Content.ToString() ?? "NTFS";
                SelectedFileSystem = fs.Equals("Do not format", StringComparison.OrdinalIgnoreCase) ? null : fs.ToLowerInvariant();
            }

            VolumeLabel = TextSanitizer.SanitizeLabel(txtLabel.Text);
            QuickFormat = chkQuickFormat.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void CmbFileSystem_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (chkQuickFormat == null || txtLabel == null) return;
            bool isFormatting = cmbFileSystem.SelectedItem is ComboBoxItem item &&
                                !string.Equals(item.Content?.ToString(), "Do not format", StringComparison.OrdinalIgnoreCase);
            chkQuickFormat.IsEnabled = isFormatting;
            txtLabel.IsEnabled = isFormatting;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
