using System.Collections.Generic;
using System.Windows;

namespace DiskPartitioner.Views
{
    public partial class KeyboardShortcutsDialog : Window
    {
        public class ShortcutItem
        {
            public string Key { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public string SpeechText => $"{Key}: {Description}";
            public override string ToString() => SpeechText;
        }

        public KeyboardShortcutsDialog()
        {
            InitializeComponent();

            var list = new List<ShortcutItem>
            {
                new() { Key = "Right Arrow", Description = "Shift focus from selected Physical Disk into its Partitions list" },
                new() { Key = "Left Arrow", Description = "Return focus from Partitions list back to Physical Disks list" },
                new() { Key = "Tab / Shift+Tab", Description = "Cycle circularly between Disks, Partitions, and Action Toolbar" },
                new() { Key = "Up / Down Arrow", Description = "Navigate through items in the currently focused list" },
                new() { Key = "Shift + F10 / App Key", Description = "Open Accessible Context Menu for selected Disk or Partition" },
                new() { Key = "Alt", Description = "Show / Hide accessible top menu bar (File, Disk, Partition, Help)" },
                new() { Key = "C", Description = "Clean / Wipe selected secondary disk (on Disks list)" },
                new() { Key = "G", Description = "Convert unpartitioned disk to GPT style (on Disks list)" },
                new() { Key = "N", Description = "Create New Partition in selected unallocated space (on Partitions list)" },
                new() { Key = "F", Description = "Format selected volume (on Partitions list)" },
                new() { Key = "L", Description = "Assign or change Drive Letter (on Partitions list)" },
                new() { Key = "Delete", Description = "Delete selected partition (on Partitions list)" },
                new() { Key = "Shift + Delete", Description = "Force delete stubborn recovery partition with override (on Partitions list)" },
                new() { Key = "F1", Description = "Open this Keyboard Shortcuts Reference modal" },
                new() { Key = "Escape", Description = "Close active modal dialog" }
            };

            lstShortcuts.ItemsSource = list;
            Loaded += (s, e) =>
            {
                if (lstShortcuts.Items.Count > 0)
                {
                    lstShortcuts.SelectedIndex = 0;
                    lstShortcuts.Focus();
                }
            };
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
