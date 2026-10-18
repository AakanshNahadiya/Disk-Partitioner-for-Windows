# Disk Partitioner for Windows

A fast, lightweight, and fully accessible disk partition manager for Windows with complete screen reader support.

## Why This Exists

Managing disks on Windows has always been frustrating for blind and visually impaired users:

* The built-in Windows Disk Management tool (diskmgmt.msc) uses a graphical canvas that constantly drops screen reader focus, leaves speech synthesizers silent, and announces controls as unknown.
* The Windows Command Prompt tool (diskpart.exe) is accessible, but typing commands manually is stressful. A single wrong disk number can wipe your main Windows drive with no confirmation and no undo.
* Third-party tools like EaseUS, AOMEI, and MiniTool are bloated, push ads, install background services, and use custom drawn canvases that screen readers cannot read at all.

Disk Partitioner for Windows gives you the full power of DiskPart inside a clean, two-pane accessible desktop interface that never drops your keyboard focus.

## What It Does

* Fully Accessible: Built specifically for NVDA, JAWS, and Windows Narrator. Every button, list, and dialog has clear speech descriptions and works with standard keyboard navigation.
* Operating System Protection: Your active Windows boot drive is automatically detected and locked. Destructive actions like disk wiping and critical partition deletion are blocked so you cannot accidentally wreck your PC.
* Partition Management: Create new partitions, format with either Quick format or Full format, assign or remove drive letters, shrink volumes, and extend partitions into contiguous free space.
* Real-Time Progress Updates: When running full format or disk wiping operations, the app tracks progress and speaks percentage updates out loud every 10 percent so you always know what is happening.
* Drive Letter Sorting: Available drive letters are strictly sorted alphabetically from A to Z, making it easy to pick the letter you want.
* Clean and Lightweight: A single native executable that launches instantly, uses under 30 MB of memory, and leaves zero background processes running when closed.
* Built-in Update Check: You can check for new updates directly from the Help menu or let the app check quietly once a day.

## Keyboard Shortcuts

### Navigation

* Tab and Shift+Tab: Move between the Physical Disks list, Partitions list, and window controls
* Up and Down Arrow: Navigate through disks and partitions
* Alt: Open the application menu bar
* F5: Refresh disk layout and storage topology
* Escape: Close any open dialog or menu

### Partition Actions (Partitions List)

* N: Create new partition (when Unallocated Space is selected)
* F: Format volume
* L: Assign or change drive letter
* E: Extend volume
* S: Shrink volume
* Delete: Delete partition
* Shift+Delete: Force delete partition (override OEM or recovery flags)

### Disk Actions (Physical Disks List)

* W: Clean and wipe secondary disk (requires typing CONFIRM)
* G: Convert disk to GPT
* M: Convert disk to MBR
* O: Take disk offline or bring disk online
* R: Set or clear disk read-only attribute

### Help and Reference

* F1: Open keyboard shortcuts reference
* Alt+H then U: Check for updates
* Alt+H then G: Open project page on GitHub
* Alt+H then A: Open About dialog

## Requirements

* Windows 10 or Windows 11 (64-bit)
* Administrator privileges (the app will prompt for elevation when modifying disks)

## Installation

1. Go to the Releases page on GitHub: https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases
2. Download the latest installer (DiskPartitioner_Setup_v1.0.2.exe).
3. Run the installer and follow the standard setup steps.

## Feedback and Contributions

If you find a bug, have a feature request, or want to suggest an improvement for screen reader accessibility, please open an issue on the GitHub repository.

> **Disclaimer:** This application is created by using Google's Antigravity AI assistant.

