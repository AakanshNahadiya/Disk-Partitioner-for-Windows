using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;
using DiskPartitioner.Services;
using DiskPartitioner.Views;

namespace DiskPartitioner
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int WM_SYSCOMMAND   = 0x0112;
        private const int SC_CLOSE        = 0xF060;

        private readonly IStorageQueryService _storageService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly IDiskpartExecutionService _diskpartService;
        private readonly ISafetyGuardrailService _guardrailService;
        private readonly IUpdateService _updateService;

        public ObservableCollection<PhysicalDiskItem> Disks { get; } = new();
        public ObservableCollection<PartitionItem> Partitions { get; } = new();

        private readonly Dictionary<int, PhysicalDiskItem> _diskMap = new();
        private readonly Dictionary<string, PartitionItem> _partitionMap = new();

        private HwndSource? _hwndSource;
        private bool _isAdmin;
        private bool _isRefreshing;
        private bool _isPartitionRefreshing;
        private bool _isOperationInProgress;
        private bool _isInitialLoad = true;
        private UIElement? _lastFocusedElementBeforeMenu;
        private bool _altKeyDown;
        private bool _otherKeyPressedWhileAlt;

        public MainWindow()
        {
            InitializeComponent();

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (s, e) => Close()));

            _storageService = new StorageQueryService();
            _screenReaderService = new ScreenReaderService();
            _diskpartService = new DiskpartExecutionService();
            _guardrailService = new SafetyGuardrailService(_storageService);
            _updateService = new UpdateService();

            lstDisks.ItemsSource = Disks;
            lstPartitions.ItemsSource = Partitions;

            _isAdmin = ElevationHelper.IsRunningAsAdmin();
            txtElevationStatus.Text = "Administrator Mode (Full Disk Management Privileges)";

            mainMenu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnAnyMenuItemClicked));

            Loaded += (s, e) =>
            {
                var helper = new WindowInteropHelper(this);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                _hwndSource?.AddHook(WndProc);

                // Immediately announce to screen reader that app is loaded and ready
                Announce("Disk Partitioner for Windows ready.");

                // Launch storage discovery asynchronously in background without freezing UI
                _ = RefreshStorageTopologyAsync(isSilent: true);

                // Explicitly land cursor on physical disks list so NVDA focuses first list on startup
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    lstDisks.Focus();
                    if (lstDisks.SelectedIndex >= 0 && lstDisks.ItemContainerGenerator.ContainerFromIndex(lstDisks.SelectedIndex) is ListBoxItem item)
                    {
                        item.Focus();
                    }
                });
            };

            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    // Defer trimming until WPF finishes the minimize transition and DirectX surface unmapping
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
                    {
                        await Task.Delay(250);
                        if (WindowState == WindowState.Minimized)
                        {
                            TrimProcessMemory();
                        }
                    });
                }
                else
                {
                    _ = RefreshStorageTopologyAsync(isSilent: true);
                }
            };

            Closed += (s, e) =>
            {
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource?.Dispose();
                Application.Current.Shutdown();
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_CLOSE)
            {
                handled = true;
                Close();
                return IntPtr.Zero;
            }

            if (msg == WM_DEVICECHANGE)
            {
                // Instant zero-CPU refresh when a drive or USB disk is plugged or unplugged
                Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
                {
                    if (!_isOperationInProgress)
                    {
                        await RefreshStorageTopologyAsync(isSilent: true);
                    }
                });
            }
            return IntPtr.Zero;
        }

        public static void TrimProcessMemory()
        {
            try
            {
                // Full two-pass garbage collection with compaction and finalizer drain
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                IntPtr hProcess = NativeMethods.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(hProcess);
                NativeMethods.SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        public void Announce(string message, bool interrupt = true)
        {
            string clean = TextSanitizer.SanitizeSpeech(message);
            if (string.IsNullOrWhiteSpace(clean)) return;

            _screenReaderService.Speak(clean, interrupt);

            txtAnnouncement.Text = clean;
            txtStatus.Text = clean;
        }

        private async Task RefreshStorageTopologyAsync(bool isSilent = false)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                int selectedDiskIndex = lstDisks.SelectedItem is PhysicalDiskItem sel ? sel.DiskIndex : -1;

                // Storage inspection performed on background thread pool thread to keep UI thread 100% fluid
                var freshDisks = await Task.Run(() => _storageService.GetPhysicalDisks());

                CollectionHelper.SynchronizeInPlace(
                    Disks,
                    _diskMap,
                    freshDisks,
                    d => d.DiskIndex,
                    (dest, src) => dest.CopyFrom(src),
                    lstDisks);

                txtDiskCount.Text = $"({Disks.Count} disks)";

                // Restore or initialize selection
                bool selectionHandled = false;
                if (lstDisks.SelectedItem == null && Disks.Count > 0)
                {
                    selectionHandled = true;
                    lstDisks.SelectedIndex = 0;
                }
                else if (selectedDiskIndex >= 0 && _diskMap.TryGetValue(selectedDiskIndex, out var reselected))
                {
                    if (lstDisks.SelectedItem != reselected)
                    {
                        selectionHandled = true;
                        lstDisks.SelectedItem = reselected;
                    }
                }

                if (!selectionHandled)
                {
                    await RefreshPartitionsForSelectedDiskAsync();
                }

                if (_isInitialLoad)
                {
                    _isInitialLoad = false;
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        lstDisks.Focus();
                        if (lstDisks.SelectedIndex >= 0 && lstDisks.ItemContainerGenerator.ContainerFromIndex(lstDisks.SelectedIndex) is ListBoxItem item)
                        {
                            item.Focus();
                        }
                    });

                    // Flush initial JIT, WMI query allocations, and presentation buffers once window and list are loaded
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
                    {
                        await Task.Delay(400);
                        TrimProcessMemory();

                        await Task.Delay(2000);
                        CheckForDailyUpdatesInBackground();
                    });
                }
                else if (!isSilent)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        if (!lstPartitions.IsKeyboardFocusWithin && !IsMenuFocusedOrActive())
                        {
                            lstDisks.Focus();
                            if (lstDisks.SelectedIndex >= 0 && lstDisks.ItemContainerGenerator.ContainerFromIndex(lstDisks.SelectedIndex) is ListBoxItem item)
                            {
                                item.Focus();
                            }
                        }
                    });

                    Announce($"Refreshed: {Disks.Count} physical disks found.");
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Refresh note: {ex.Message}";
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task RefreshPartitionsForSelectedDiskAsync()
        {
            if (_isPartitionRefreshing) return;
            _isPartitionRefreshing = true;

            try
            {
                if (lstDisks.SelectedItem is not PhysicalDiskItem selectedDisk)
                {
                    Partitions.Clear();
                    _partitionMap.Clear();
                    txtPartitionsHeader.Text = "2. Partitions & Volumes";
                    txtPartitionCount.Text = "(0 partitions)";
                    return;
                }

                int diskIdx = selectedDisk.DiskIndex;
                long totalSize = selectedDisk.TotalSizeBytes;
                string model = selectedDisk.Model;

                txtPartitionsHeader.Text = $"2. Partitions on Disk {diskIdx} ({model})";

                var freshPartitions = await Task.Run(() => _storageService.GetPartitions(diskIdx, totalSize));

                if (lstDisks.SelectedItem is PhysicalDiskItem current && current.DiskIndex == diskIdx)
                {
                    CollectionHelper.SynchronizeInPlace(
                        Partitions,
                        _partitionMap,
                        freshPartitions,
                        p => $"{p.PartitionNumber}_{p.StartingOffset}",
                        (dest, src) => dest.CopyFrom(src),
                        lstPartitions);

                    int realPartitions = Partitions.Count(p => !p.IsUnallocated);
                    int unallocatedCount = Partitions.Count(p => p.IsUnallocated);
                    txtPartitionCount.Text = unallocatedCount > 0
                        ? $"({realPartitions} partitions, {unallocatedCount} unallocated)"
                        : $"({realPartitions} partitions)";

                    if (lstPartitions.SelectedItem == null && Partitions.Count > 0)
                    {
                        lstPartitions.SelectedIndex = 0;
                    }
                }
            }
            finally
            {
                _isPartitionRefreshing = false;
            }
        }

        private async void LstDisks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await RefreshPartitionsForSelectedDiskAsync();

            if (lstDisks.SelectedItem is PhysicalDiskItem selectedDisk)
            {
                txtStatus.Text = selectedDisk.DisplayName;
            }
        }

        private void LstPartitions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPartitions.SelectedItem is PartitionItem selectedPart)
            {
                txtStatus.Text = selectedPart.DisplayName;
            }
        }

        private void LstDisks_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Right Arrow: Shift focus seamlessly into Partitions list
            if (e.Key == Key.Right)
            {
                if (Partitions.Count > 0)
                {
                    e.Handled = true;
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        lstPartitions.Focus();
                        if (lstPartitions.SelectedIndex < 0 && Partitions.Count > 0)
                        {
                            lstPartitions.SelectedIndex = 0;
                        }
                        if (lstPartitions.ItemContainerGenerator.ContainerFromIndex(lstPartitions.SelectedIndex) is ListBoxItem item)
                        {
                            item.Focus();
                        }
                    });
                }
            }
            else if (e.Key == Key.C)
            {
                e.Handled = true;
                MenuDiskClean_Click(sender, e);
            }
            else if (e.Key == Key.G)
            {
                e.Handled = true;
                MenuDiskConvertGpt_Click(sender, e);
            }
        }

        private void LstPartitions_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Left Arrow: Return focus seamlessly to Disks list
            if (e.Key == Key.Left)
            {
                e.Handled = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    lstDisks.Focus();
                    if (lstDisks.ItemContainerGenerator.ContainerFromIndex(lstDisks.SelectedIndex) is ListBoxItem item)
                    {
                        item.Focus();
                    }
                });
            }
            else if (e.Key == Key.Delete)
            {
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    MenuPartitionForceDelete_Click(sender, e);
                }
                else
                {
                    MenuPartitionDelete_Click(sender, e);
                }
            }
            else if (e.Key == Key.N)
            {
                e.Handled = true;
                MenuPartitionCreate_Click(sender, e);
            }
            else if (e.Key == Key.F)
            {
                e.Handled = true;
                MenuPartitionFormat_Click(sender, e);
            }
            else if (e.Key == Key.L)
            {
                e.Handled = true;
                MenuPartitionDriveLetter_Click(sender, e);
            }
            else if (e.Key == Key.E)
            {
                e.Handled = true;
                MenuPartitionExtend_Click(sender, e);
            }
            else if (e.Key == Key.S)
            {
                e.Handled = true;
                MenuPartitionShrink_Click(sender, e);
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Alt+F4: Close the application immediately on first press
            if ((e.Key == Key.System && e.SystemKey == Key.F4) ||
                (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                e.Handled = true;
                Close();
                return;
            }

            // Alt key tracking: do NOT toggle menu on KeyDown.
            // In Windows, top menu bars toggle on Alt KeyUp, only when no other key was pressed.
            if (e.Key == Key.System)
            {
                if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
                {
                    _altKeyDown = true;
                    _otherKeyPressedWhileAlt = false;
                    return;
                }

                _otherKeyPressedWhileAlt = true;

                if (e.SystemKey == Key.Space)
                {
                    // Allow Alt+Space (system window menu)
                    return;
                }

                // Alt + Access Keys (F = File, D = Disk, P = Partition, H = Help)
                if (e.SystemKey == Key.F)
                {
                    e.Handled = true;
                    OpenMenu(menuFile);
                    return;
                }
                if (e.SystemKey == Key.D)
                {
                    e.Handled = true;
                    OpenMenu(menuDisk);
                    return;
                }
                if (e.SystemKey == Key.P)
                {
                    e.Handled = true;
                    OpenMenu(menuPartition);
                    return;
                }
                if (e.SystemKey == Key.H)
                {
                    e.Handled = true;
                    OpenMenu(menuHelp);
                    return;
                }

                if (mainMenu.Visibility == Visibility.Collapsed)
                {
                    OpenMenu();
                }
            }
            else
            {
                if (_altKeyDown)
                {
                    _otherKeyPressedWhileAlt = true;
                }

                if (e.Key == Key.F10)
                {
                    e.Handled = true;
                    if (mainMenu.Visibility == Visibility.Visible)
                    {
                        CloseMenu(restoreFocus: true);
                    }
                    else
                    {
                        OpenMenu();
                    }
                    return;
                }
                else if (e.Key == Key.Escape && mainMenu.Visibility == Visibility.Visible)
                {
                    // If a submenu popup is open, let WPF handle closing the submenu first
                    if (mainMenu.Items.OfType<MenuItem>().Any(m => m.IsSubmenuOpen))
                    {
                        return;
                    }

                    e.Handled = true;
                    CloseMenu(restoreFocus: true);
                    return;
                }
                else if (e.Key == Key.F5)
                {
                    e.Handled = true;
                    _ = RefreshStorageTopologyAsync();
                    return;
                }
                else if (e.Key == Key.F1)
                {
                    e.Handled = true;
                    ShowShortcutsDialog();
                    return;
                }
            }
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);

            if (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
            {
                if (_altKeyDown && !_otherKeyPressedWhileAlt)
                {
                    e.Handled = true;
                    if (mainMenu.Visibility == Visibility.Visible)
                    {
                        CloseMenu(restoreFocus: true);
                    }
                    else
                    {
                        OpenMenu();
                    }
                }
                _altKeyDown = false;
                _otherKeyPressedWhileAlt = false;
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _altKeyDown = false;
            _otherKeyPressedWhileAlt = false;
        }

        private void OpenMenu(MenuItem? targetMenuItem = null)
        {
            if (mainMenu.Visibility != Visibility.Visible)
            {
                _lastFocusedElementBeforeMenu = Keyboard.FocusedElement as UIElement;
                if (_lastFocusedElementBeforeMenu == null || _lastFocusedElementBeforeMenu == this)
                {
                    _lastFocusedElementBeforeMenu = lstDisks.IsKeyboardFocusWithin ? lstDisks : lstPartitions;
                }

                mainMenu.Visibility = Visibility.Visible;
                menuSeparator.Visibility = Visibility.Visible;
                mainMenu.UpdateLayout();
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                var toFocus = targetMenuItem ?? (mainMenu.Items.Count > 0 ? mainMenu.Items[0] as MenuItem : null);
                if (toFocus != null)
                {
                    toFocus.Focus();
                    if (targetMenuItem != null)
                    {
                        toFocus.IsSubmenuOpen = true;
                    }
                }
            });
        }

        private void CloseMenu(bool restoreFocus = true)
        {
            if (mainMenu.Visibility == Visibility.Collapsed) return;

            // Close any open submenus
            foreach (var item in mainMenu.Items.OfType<MenuItem>())
            {
                item.IsSubmenuOpen = false;
            }

            mainMenu.Visibility = Visibility.Collapsed;
            menuSeparator.Visibility = Visibility.Collapsed;

            if (restoreFocus)
            {
                var target = _lastFocusedElementBeforeMenu;
                _lastFocusedElementBeforeMenu = null;

                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (target != null && target.IsVisible && target.Focusable)
                    {
                        target.Focus();
                        if (target is ListBox lb && lb.SelectedItem != null && lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) is ListBoxItem lbi)
                        {
                            lbi.Focus();
                        }
                    }
                    else
                    {
                        lstDisks.Focus();
                    }
                });
            }
        }

        private bool IsMenuFocusedOrActive()
        {
            if (mainMenu.Visibility != Visibility.Visible) return false;
            if (mainMenu.IsKeyboardFocusWithin) return true;

            // Check if any top-level menu item has its drop-down submenu open
            foreach (var item in mainMenu.Items.OfType<MenuItem>())
            {
                if (item.IsSubmenuOpen) return true;
            }

            // Check if keyboard focus is currently inside the menu or its popups
            if (Keyboard.FocusedElement is DependencyObject focused)
            {
                DependencyObject? current = focused;
                while (current != null)
                {
                    if (current == mainMenu) return true;

                    DependencyObject? parent = LogicalTreeHelper.GetParent(current);
                    if (parent == null && current is Visual v)
                    {
                        parent = VisualTreeHelper.GetParent(v);
                    }
                    current = parent;
                }
            }

            return false;
        }

        private void MainMenu_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!mainMenu.IsKeyboardFocusWithin)
            {
                // Defer check to allow focus transition between menu items, submenus, or popups to complete
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (!IsMenuFocusedOrActive())
                    {
                        CloseMenu(restoreFocus: false);
                    }
                });
            }
        }

        private void OnAnyMenuItemClicked(object sender, RoutedEventArgs e)
        {
            // Only close when an actual action item (not a submenu header) is clicked
            if (e.OriginalSource is MenuItem mi && !mi.HasItems)
            {
                CloseMenu(restoreFocus: false);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshStorageTopologyAsync();
        }

        private void BtnShortcuts_Click(object sender, RoutedEventArgs e)
        {
            ShowShortcutsDialog();
        }

        private void ShowShortcutsDialog()
        {
            var dlg = new KeyboardShortcutsDialog { Owner = this };
            dlg.ShowDialog();
            TrimProcessMemory();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MenuCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UpdateCheckDialog(_updateService, _screenReaderService) { Owner = this };
            dlg.ShowDialog();
            TrimProcessMemory();
        }

        private void MenuViewOnGitHub_Click(object sender, RoutedEventArgs e)
        {
            _updateService.OpenUrl(UpdateService.RepoWebUrl);
        }

        private async void CheckForDailyUpdatesInBackground()
        {
            try
            {
                if (!_updateService.ShouldPerformDailyCheck()) return;

                _updateService.RecordCheckPerformed();
                var info = await _updateService.CheckForUpdatesAsync();

                if (info.IsSuccess && info.HasUpdate)
                {
                    if (!_updateService.HasNotifiedForVersion(info.LatestVersion))
                    {
                        _updateService.RecordVersionNotified(info.LatestVersion);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            Announce($"A new version of Disk Partitioner for Windows ({info.LatestVersion}) is available. Check the Help menu to view.", interrupt: false);
                        });
                    }
                }
            }
            catch { }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AboutDialog { Owner = this };
            dlg.ShowDialog();
            TrimProcessMemory();
        }

        private void TriggerAdminRestart()
        {
            if (!_isAdmin)
            {
                Announce("Requesting Administrator elevation prompt...");
                ElevationHelper.RestartAsAdmin();
            }
            else
            {
                Announce("Application is already running as Administrator.");
            }
        }

        private bool CheckAdminForModification()
        {
            if (!_isAdmin)
            {
                var result = MessageBox.Show(
                    this,
                    "Modifying disk partitions requires Windows Administrator privileges.\n\nWould you like to restart Disk Partitioner for Windows as Administrator now?",
                    "Administrator Elevation Required",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.OK)
                {
                    ElevationHelper.RestartAsAdmin();
                }
                return false;
            }
            return true;
        }

        #region Operations Engine Execution Handlers

        private async void MenuDiskClean_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;

            var check = _guardrailService.CanCleanDisk(disk);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new WipeConfirmationDialog(disk) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Confirmed)
            {
                _isOperationInProgress = true;
                pbOperation.Visibility = Visibility.Visible;
                pbOperation.Value = 0;
                txtStatus.Text = $"Cleaning Disk {disk.DiskIndex}...";
                Announce($"Cleaning Disk {disk.DiskIndex}. Please wait...");

                var progressPct = new Progress<int>(pct =>
                {
                    pbOperation.Value = pct;
                    txtStatus.Text = $"Cleaning Disk {disk.DiskIndex}: {pct}% completed";
                });

                var progressMsg = new Progress<string>(msg =>
                {
                    txtStatus.Text = TextSanitizer.StripAnsiAndControl(msg);
                });

                var result = await _diskpartService.CleanDiskAsync(disk.DiskIndex, progressPct, progressMsg);

                pbOperation.Visibility = Visibility.Collapsed;
                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Clean Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuDiskConvertGpt_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;

            var check = _guardrailService.CanConvertDisk(disk);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Cannot Convert Disk", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var confirm = MessageBox.Show(
                this,
                $"Are you sure you want to convert Disk {disk.DiskIndex} to GPT partition table?",
                "Confirm Convert to GPT",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.OK)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Converting Disk {disk.DiskIndex} to GPT...";
                Announce($"Converting Disk {disk.DiskIndex} to GPT...");

                var result = await _diskpartService.ConvertStyleAsync(disk.DiskIndex, "gpt");

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Convert Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuDiskConvertMbr_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;

            var check = _guardrailService.CanConvertDisk(disk);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Cannot Convert Disk", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var confirm = MessageBox.Show(
                this,
                $"Are you sure you want to convert Disk {disk.DiskIndex} to MBR partition table?",
                "Confirm Convert to MBR",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.OK)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Converting Disk {disk.DiskIndex} to MBR...";
                Announce($"Converting Disk {disk.DiskIndex} to MBR...");

                var result = await _diskpartService.ConvertStyleAsync(disk.DiskIndex, "mbr");

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Convert Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuDiskReadOnly_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;

            var check = _guardrailService.CanSetDiskReadOnly(disk);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!CheckAdminForModification()) return;

            _isOperationInProgress = true;
            var result = await _diskpartService.SetDiskReadOnlyAsync(disk.DiskIndex, readOnly: true);
            _isOperationInProgress = false;

            Announce(result.Message);
            await RefreshStorageTopologyAsync(isSilent: true);
        }

        private async void MenuDiskClearReadOnly_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;
            if (!CheckAdminForModification()) return;

            _isOperationInProgress = true;
            var result = await _diskpartService.SetDiskReadOnlyAsync(disk.DiskIndex, readOnly: false);
            _isOperationInProgress = false;

            Announce(result.Message);
            await RefreshStorageTopologyAsync(isSilent: true);
        }

        private async void MenuDiskOffline_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;

            var check = _guardrailService.CanSetDiskOffline(disk);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!CheckAdminForModification()) return;

            _isOperationInProgress = true;
            var result = await _diskpartService.SetDiskOfflineAsync(disk.DiskIndex, offline: true);
            _isOperationInProgress = false;

            Announce(result.Message);
            await RefreshStorageTopologyAsync(isSilent: true);
        }

        private async void MenuDiskOnline_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is not PhysicalDiskItem disk) return;
            if (!CheckAdminForModification()) return;

            _isOperationInProgress = true;
            var result = await _diskpartService.SetDiskOfflineAsync(disk.DiskIndex, offline: false);
            _isOperationInProgress = false;

            Announce(result.Message);
            await RefreshStorageTopologyAsync(isSilent: true);
        }

        private async void MenuPartitionCreate_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem unallocated || !unallocated.IsUnallocated)
            {
                Announce("Please select an Unallocated Space item to create a partition.");
                MessageBox.Show(this, "Please select an Unallocated Space item in the partitions list to create a new partition.", "Select Unallocated Space", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new CreatePartitionDialog(unallocated) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _isOperationInProgress = true;
                pbOperation.Visibility = Visibility.Visible;
                pbOperation.Value = 0;

                string initialMsg = dlg.QuickFormat
                    ? $"Creating partition on Disk {unallocated.DiskIndex}..."
                    : $"Creating partition on Disk {unallocated.DiskIndex}... Full format in progress (0%)...";
                txtStatus.Text = initialMsg;
                Announce(dlg.QuickFormat
                    ? "Creating partition..."
                    : "Creating partition. Full format in progress, please wait...");

                int lastAnnouncedPct = -10;
                var progressPct = new Progress<int>(pct =>
                {
                    pbOperation.Value = pct;
                    txtStatus.Text = $"Creating partition on Disk {unallocated.DiskIndex}: {pct}% completed";
                    if (pct >= lastAnnouncedPct + 10 || pct == 100)
                    {
                        lastAnnouncedPct = (pct / 10) * 10;
                        Announce($"{pct} percent completed");
                    }
                });

                var progressMsg = new Progress<string>(msg =>
                {
                    if (!msg.EndsWith("completed", StringComparison.OrdinalIgnoreCase))
                    {
                        txtStatus.Text = TextSanitizer.StripAnsiAndControl(msg);
                    }
                });

                var result = await _diskpartService.CreatePartitionAsync(
                    unallocated.DiskIndex,
                    dlg.PartitionSizeMb,
                    dlg.SelectedDriveLetter,
                    dlg.SelectedFileSystem,
                    dlg.VolumeLabel,
                    dlg.QuickFormat,
                    progressPct,
                    progressMsg);

                pbOperation.Visibility = Visibility.Collapsed;
                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Create Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionFormat_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanFormatPartition(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new FormatPartitionDialog(part) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _isOperationInProgress = true;
                pbOperation.Visibility = Visibility.Visible;
                pbOperation.Value = 0;

                string initialMsg = dlg.QuickFormat
                    ? $"Formatting Partition {part.PartitionNumber}..."
                    : $"Formatting Partition {part.PartitionNumber}... Full format in progress (0%)...";
                txtStatus.Text = initialMsg;
                Announce(dlg.QuickFormat
                    ? $"Formatting Partition {part.PartitionNumber}..."
                    : $"Formatting Partition {part.PartitionNumber}. Full format in progress, please wait...");

                int lastAnnouncedPct = -10;
                var progressPct = new Progress<int>(pct =>
                {
                    pbOperation.Value = pct;
                    txtStatus.Text = $"Formatting Partition {part.PartitionNumber}: {pct}% completed";
                    if (pct >= lastAnnouncedPct + 10 || pct == 100)
                    {
                        lastAnnouncedPct = (pct / 10) * 10;
                        Announce($"{pct} percent completed");
                    }
                });

                var progressMsg = new Progress<string>(msg =>
                {
                    if (!msg.EndsWith("completed", StringComparison.OrdinalIgnoreCase))
                    {
                        txtStatus.Text = TextSanitizer.StripAnsiAndControl(msg);
                    }
                });

                var result = await _diskpartService.FormatPartitionAsync(
                    part.DiskIndex,
                    part.PartitionNumber,
                    dlg.SelectedFileSystem,
                    dlg.VolumeLabel,
                    dlg.QuickFormat,
                    progressPct,
                    progressMsg);

                pbOperation.Visibility = Visibility.Collapsed;
                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionDriveLetter_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanModifyDriveLetter(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new ChangeDriveLetterDialog(part) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _isOperationInProgress = true;
                DiskOperationResult result;

                if (dlg.IsRemove)
                {
                    txtStatus.Text = $"Removing drive letter {dlg.SelectedLetter}:...";
                    result = await _diskpartService.RemoveDriveLetterAsync(part.DiskIndex, part.PartitionNumber, dlg.SelectedLetter);
                }
                else
                {
                    txtStatus.Text = $"Assigning drive letter {dlg.SelectedLetter}:...";
                    result = await _diskpartService.AssignDriveLetterAsync(part.DiskIndex, part.PartitionNumber, dlg.SelectedLetter);
                }

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Drive Letter Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionExtend_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanExtendPartition(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Cannot Extend Partition", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new ResizePartitionDialog(part, isShrink: false) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Extending Partition {part.PartitionNumber}...";
                Announce($"Extending Partition {part.PartitionNumber}...");

                var result = await _diskpartService.ExtendPartitionAsync(part.DiskIndex, part.PartitionNumber, dlg.DeltaMb, part.DriveLetter);

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Extend Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionShrink_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanShrinkPartition(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Cannot Shrink Partition", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var dlg = new ResizePartitionDialog(part, isShrink: true) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Shrinking Partition {part.PartitionNumber}...";
                Announce($"Shrinking Partition {part.PartitionNumber}...");

                var result = await _diskpartService.ShrinkPartitionAsync(part.DiskIndex, part.PartitionNumber, dlg.DeltaMb, part.DriveLetter);

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Shrink Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanDeletePartition(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var confirm = MessageBox.Show(
                this,
                $"Are you sure you want to delete Partition {part.PartitionNumber} ({part.FormattedSize})?\n\nAll data on this volume will be lost.",
                "Confirm Delete Partition",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.OK)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Deleting Partition {part.PartitionNumber}...";
                Announce($"Deleting Partition {part.PartitionNumber}...");

                var result = await _diskpartService.DeletePartitionAsync(part.DiskIndex, part.PartitionNumber, overrideFlag: false);

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    if (result.Message.Contains("OVERRIDE", StringComparison.OrdinalIgnoreCase))
                    {
                        var retryWithOverride = MessageBox.Show(
                            this,
                            $"Partition {part.PartitionNumber} ({part.PartitionTypeFriendlyName}) is protected by Windows (e.g. a Recovery or OEM partition).\n\nWindows Virtual Disk Service requires the OVERRIDE parameter to delete this type of partition.\n\nWould you like to force delete Partition {part.PartitionNumber} using the OVERRIDE parameter now?",
                            "Protected Partition - Force Delete with Override?",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (retryWithOverride == MessageBoxResult.Yes)
                        {
                            _isOperationInProgress = true;
                            txtStatus.Text = $"Force deleting Partition {part.PartitionNumber}...";
                            Announce($"Force deleting Partition {part.PartitionNumber}...");

                            var overrideResult = await _diskpartService.DeletePartitionAsync(part.DiskIndex, part.PartitionNumber, overrideFlag: true);

                            _isOperationInProgress = false;
                            Announce(overrideResult.Message);
                            if (!overrideResult.Success)
                            {
                                MessageBox.Show(this, overrideResult.Message, "Force Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }

                            await RefreshStorageTopologyAsync(isSilent: true);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show(this, result.Message, "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        private async void MenuPartitionForceDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lstPartitions.SelectedItem is not PartitionItem part || part.IsUnallocated) return;

            var check = _guardrailService.CanDeletePartition(lstDisks.SelectedItem as PhysicalDiskItem ?? new PhysicalDiskItem(), part);
            if (!check.IsAllowed)
            {
                MessageBox.Show(this, check.Reason, "Operation Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                Announce(check.Reason);
                return;
            }

            if (!CheckAdminForModification()) return;

            var confirm = MessageBox.Show(
                this,
                $"Are you sure you want to FORCE delete Partition {part.PartitionNumber} ({part.FormattedSize}) with the OVERRIDE flag?\n\nThis will force deletion even if this is an OEM Recovery partition.",
                "Confirm Force Delete (Override)",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.OK)
            {
                _isOperationInProgress = true;
                txtStatus.Text = $"Force deleting Partition {part.PartitionNumber}...";
                Announce($"Force deleting Partition {part.PartitionNumber}...");

                var result = await _diskpartService.DeletePartitionAsync(part.DiskIndex, part.PartitionNumber, overrideFlag: true);

                _isOperationInProgress = false;
                Announce(result.Message);
                if (!result.Success)
                {
                    MessageBox.Show(this, result.Message, "Force Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                await RefreshStorageTopologyAsync(isSilent: true);
            }
        }

        #endregion
    }
}
