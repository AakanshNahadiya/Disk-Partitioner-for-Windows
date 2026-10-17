using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using DiskPartitioner.Models;

namespace DiskPartitioner.Views
{
    public partial class ChangeDriveLetterDialog : Window
    {
        public bool IsRemove { get; private set; }
        public char SelectedLetter { get; private set; }

        private readonly PartitionItem _partition;

        public ChangeDriveLetterDialog(PartitionItem partition)
        {
            InitializeComponent();
            _partition = partition;

            string current = !string.IsNullOrWhiteSpace(partition.DriveLetter)
                ? $"Current Letter: {partition.DriveLetter}"
                : "No drive letter currently assigned";
            txtPartitionInfo.Text = $"Partition {partition.PartitionNumber} on Disk {partition.DiskIndex} ({partition.FormattedSize}) - {current}";

            if (string.IsNullOrWhiteSpace(partition.DriveLetter))
            {
                rbRemove.IsEnabled = false;
            }

            PopulateAvailableDriveLetters();

            Loaded += (s, e) => cmbDriveLetters.Focus();
        }

        private void PopulateAvailableDriveLetters()
        {
            var usedLetters = DriveInfo.GetDrives()
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();

            var available = new List<string>();
            for (char c = 'A'; c <= 'Z'; c++)
            {
                if (!usedLetters.Contains(c))
                {
                    available.Add($"{c}:");
                }
            }

            cmbDriveLetters.ItemsSource = available;
            if (available.Count > 0)
            {
                cmbDriveLetters.SelectedIndex = 0;
            }
        }

        private void RbMode_Changed(object sender, RoutedEventArgs e)
        {
            if (cmbDriveLetters == null) return;
            cmbDriveLetters.IsEnabled = rbAssign.IsChecked ?? true;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (rbRemove.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(_partition.DriveLetter))
                {
                    MessageBox.Show(this, "This partition has no drive letter to remove.", "Note", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                IsRemove = true;
                SelectedLetter = char.ToUpperInvariant(_partition.DriveLetter[0]);
                DialogResult = true;
                Close();
            }
            else
            {
                IsRemove = false;
                if (cmbDriveLetters.SelectedItem is string letterStr && letterStr.Length > 0)
                {
                    SelectedLetter = char.ToUpperInvariant(letterStr[0]);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(this, "Please select an available drive letter from the list.", "No Letter Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
