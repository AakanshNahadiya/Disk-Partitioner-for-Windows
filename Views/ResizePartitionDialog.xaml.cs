using System;
using System.Windows;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;

namespace DiskPartitioner.Views
{
    public partial class ResizePartitionDialog : Window
    {
        private readonly PartitionItem _partition;
        private readonly long _maxAvailableMb;
        public bool IsShrink { get; }
        public long DeltaMb { get; private set; }
        private int _lastUnitIndex = 0; // 0 = GB, 1 = MB
        private bool _isUpdatingUnit = false;

        public ResizePartitionDialog(PartitionItem partition, bool isShrink, long maxAvailableMb = 0)
        {
            InitializeComponent();
            _partition = partition;
            IsShrink = isShrink;

            _isUpdatingUnit = true;
            cmbDeltaUnit.SelectedIndex = 0; // GB default
            _lastUnitIndex = 0;

            if (isShrink)
            {
                Title = "Shrink Volume";
                txtTitle.Text = "Shrink Volume";
                lblSizePrompt.Text = "Amount of space to shrink:";
                System.Windows.Automation.AutomationProperties.SetName(txtDeltaValue, "Amount of space to shrink");
                System.Windows.Automation.AutomationProperties.SetName(cmbDeltaUnit, "Unit of space to shrink");
                btnApply.Content = "Shrink";

                long totalMb = Math.Max(1, partition.SizeBytes / (1024 * 1024));
                long freeMb = partition.FreeSizeBytes > 0 ? partition.FreeSizeBytes / (1024 * 1024) : 0;
                long maxShrinkMb = freeMb > 512 ? (freeMb - 256) : (freeMb > 0 ? freeMb : Math.Max(1, totalMb / 2));
                _maxAvailableMb = maxShrinkMb;

                // Sensible default: 1024 MB (1 GB) if ample free space exists, otherwise half of freeMb
                long defaultShrinkMb;
                if (freeMb >= 2048)
                {
                    defaultShrinkMb = 1024;
                }
                else if (freeMb > 0)
                {
                    defaultShrinkMb = Math.Max(1, freeMb / 2);
                }
                else
                {
                    defaultShrinkMb = Math.Min(1024, Math.Max(1, totalMb / 10));
                }

                if (defaultShrinkMb >= 1024 && defaultShrinkMb % 1024 == 0)
                {
                    cmbDeltaUnit.SelectedIndex = 0; // GB
                    txtDeltaValue.Text = (defaultShrinkMb / 1024).ToString();
                }
                else if (defaultShrinkMb >= 1024)
                {
                    cmbDeltaUnit.SelectedIndex = 0; // GB
                    txtDeltaValue.Text = Math.Round(defaultShrinkMb / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    cmbDeltaUnit.SelectedIndex = 1; // MB
                    txtDeltaValue.Text = defaultShrinkMb.ToString();
                }

                txtHint.Text = freeMb > 0
                    ? $"Available free space: {partition.FormattedFreeSize}. Estimated max shrink space: {maxShrinkMb:N0} MB."
                    : "This amount will be subtracted from the partition and turned into unallocated space.";
            }
            else
            {
                Title = "Extend Volume";
                txtTitle.Text = "Extend Volume";
                lblSizePrompt.Text = "Amount of space to extend:";
                _maxAvailableMb = maxAvailableMb;

                if (_maxAvailableMb >= 1024)
                {
                    cmbDeltaUnit.SelectedIndex = 0; // GB
                    txtDeltaValue.Text = Math.Round(_maxAvailableMb / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (_maxAvailableMb > 0)
                {
                    cmbDeltaUnit.SelectedIndex = 1; // MB
                    txtDeltaValue.Text = _maxAvailableMb.ToString();
                }
                else
                {
                    cmbDeltaUnit.SelectedIndex = 0; // GB
                    txtDeltaValue.Text = "0";
                }

                txtHint.Text = _maxAvailableMb > 0
                    ? $"Available adjacent unallocated space: {FormatHelper.FormatBytes(_maxAvailableMb * 1024 * 1024)}. Click Max Size or enter 0 to extend using all available space."
                    : "The partition will be extended into adjacent unallocated space (enter 0 or click Max Size to use all available space).";

                System.Windows.Automation.AutomationProperties.SetName(txtDeltaValue, "Amount of space to extend, enter 0 or click Max Size for all available unallocated space");
                System.Windows.Automation.AutomationProperties.SetName(cmbDeltaUnit, "Unit of space to extend");
                btnApply.Content = "Extend";
            }
            _isUpdatingUnit = false;

            string fsStr = !string.IsNullOrWhiteSpace(partition.FileSystem) ? $" ({partition.FileSystem})" : "";
            string freeStr = partition.FreeSizeBytes >= 0 ? $" | Free: {partition.FormattedFreeSize}" : "";
            txtPartitionInfo.Text = $"Partition {partition.PartitionNumber}{fsStr} on Disk {partition.DiskIndex} - Total: {partition.FormattedSize}{freeStr}";

            Loaded += (s, e) => txtDeltaValue.Focus();
        }

        private void CmbDeltaUnit_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isUpdatingUnit || txtDeltaValue == null) return;

            int newIndex = cmbDeltaUnit.SelectedIndex;
            if (newIndex == _lastUnitIndex) return;

            string raw = txtDeltaValue.Text.Trim();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) ||
                double.TryParse(raw, out val))
            {
                _isUpdatingUnit = true;
                if (newIndex == 0 && _lastUnitIndex == 1) // MB to GB
                {
                    txtDeltaValue.Text = Math.Round(val / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (newIndex == 1 && _lastUnitIndex == 0) // GB to MB
                {
                    txtDeltaValue.Text = Math.Round(val * 1024.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                _isUpdatingUnit = false;
            }

            _lastUnitIndex = newIndex;
        }

        private void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            if (_maxAvailableMb > 0)
            {
                if (cmbDeltaUnit.SelectedIndex == 0)
                {
                    txtDeltaValue.Text = Math.Round(_maxAvailableMb / 1024.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    txtDeltaValue.Text = _maxAvailableMb.ToString();
                }
            }
            else
            {
                txtDeltaValue.Text = "0";
            }

            txtDeltaValue.Focus();
            txtDeltaValue.SelectAll();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            string raw = txtDeltaValue.Text.Trim();
            if ((!double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double inputVal) &&
                 !double.TryParse(raw, out inputVal)) || inputVal < 0)
            {
                MessageBox.Show(this, "Please enter a valid size (e.g. 10 GB or 10240 MB).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtDeltaValue.Focus();
                return;
            }

            long mb = cmbDeltaUnit.SelectedIndex == 0
                ? (long)Math.Round(inputVal * 1024.0)
                : (long)Math.Round(inputVal);

            if (IsShrink)
            {
                if (mb <= 0)
                {
                    MessageBox.Show(this, "Shrink amount must be greater than 0.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDeltaValue.Focus();
                    return;
                }

                long totalMb = _partition.SizeBytes / (1024 * 1024);
                if (mb >= totalMb - 50)
                {
                    MessageBox.Show(this, $"Shrink amount ({mb:N0} MB) is too large. A partition cannot be shrunk to less than 50 MB.", "Size Exceeds Limit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDeltaValue.Focus();
                    return;
                }

                if (_partition.FreeSizeBytes > 0 && mb > (_partition.FreeSizeBytes / (1024 * 1024)))
                {
                    MessageBox.Show(this,
                        $"The requested shrink amount ({mb:N0} MB) exceeds the available free space ({_partition.FormattedFreeSize}).\n\nA partition cannot be shrunk by more than its available free space.",
                        "Shrink Exceeds Free Space",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    txtDeltaValue.Focus();
                    return;
                }

                DeltaMb = mb;
            }
            else
            {
                if (_maxAvailableMb > 0 && mb > _maxAvailableMb)
                {
                    string maxDisp = cmbDeltaUnit.SelectedIndex == 0
                        ? $"{Math.Round(_maxAvailableMb / 1024.0, 2):N2} GB"
                        : $"{_maxAvailableMb:N0} MB";

                    MessageBox.Show(this, $"The entered size exceeds available unallocated space ({maxDisp}).", "Size Exceeds Capacity", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDeltaValue.Focus();
                    return;
                }

                // In Diskpart, size=0 or omitting size extends across all available unallocated space
                if (mb <= 0 || (_maxAvailableMb > 0 && mb >= _maxAvailableMb))
                {
                    DeltaMb = 0;
                }
                else
                {
                    DeltaMb = mb;
                }
            }

            DeltaMb = mb;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
