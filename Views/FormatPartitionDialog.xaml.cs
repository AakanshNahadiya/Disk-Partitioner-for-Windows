using System;
using System.Windows;
using System.Windows.Controls;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;

namespace DiskPartitioner.Views
{
    public partial class FormatPartitionDialog : Window
    {
        public string SelectedFileSystem { get; private set; } = "ntfs";
        public string VolumeLabel { get; private set; } = string.Empty;
        public bool QuickFormat { get; private set; } = true;

        public FormatPartitionDialog(PartitionItem partition)
        {
            InitializeComponent();
            txtPartitionInfo.Text = $"Target: Partition {partition.PartitionNumber} ({partition.FormattedSize}) on Disk {partition.DiskIndex}";
            txtLabel.Text = string.IsNullOrWhiteSpace(partition.VolumeLabel) ? "New Volume" : partition.VolumeLabel;

            Loaded += (s, e) => txtLabel.Focus();
        }

        private void BtnFormat_Click(object sender, RoutedEventArgs e)
        {
            if (cmbFileSystem.SelectedItem is ComboBoxItem item)
            {
                SelectedFileSystem = item.Content.ToString()?.ToLowerInvariant() ?? "ntfs";
            }
            VolumeLabel = TextSanitizer.SanitizeLabel(txtLabel.Text);
            QuickFormat = chkQuickFormat.IsChecked ?? true;

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
