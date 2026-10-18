using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskPartitioner.Helpers
{
    /// <summary>
    /// Centralized Win32 Native P/Invoke methods, storage IOCTLs, structs, and Microsoft SDL security helpers.
    /// Follows official Microsoft .NET guidelines for native interop consolidation.
    /// </summary>
    internal static class NativeMethods
    {
        #region Process & DLL Security Hardening (Microsoft SDL / CWE-427)

        public const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        public const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        public const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Applies Microsoft Security Development Lifecycle (SDL) DLL search order hardening at application startup.
        /// Protects against CWE-427 (Uncontrolled Search Path / DLL Preloading Vulnerability).
        /// </summary>
        public static void InitializeProcessSecurity()
        {
            try
            {
                // Restrict DLL resolution to application directory and System32 only
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                // Remove the current working directory from the DLL search path
                SetDllDirectory("");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Process security initialization note: {ex.Message}");
            }
        }

        #endregion

        #region Storage IOCTL Control Codes (winioctl.h)

        public const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX   = 0x00070050;
        public const uint IOCTL_DISK_GET_PARTITION_INFO_EX = 0x00070048;
        public const uint IOCTL_DISK_SET_DRIVE_LAYOUT_EX   = 0x0007C054;
        public const uint IOCTL_DISK_GET_LENGTH_INFO       = 0x0007405C;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER  = 0x002D1080;
        public const uint IOCTL_STORAGE_QUERY_PROPERTY     = 0x002D1400;
        public const uint IOCTL_DISK_UPDATE_PROPERTIES     = 0x00070140;

        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint FILE_SHARE_DELETE = 4;
        public const uint OPEN_EXISTING = 3;

        #endregion

        #region Storage Enums

        public enum PARTITION_STYLE : uint
        {
            PARTITION_STYLE_MBR = 0, // Master Boot Record (Legacy BIOS)
            PARTITION_STYLE_GPT = 1, // GUID Partition Table (Modern UEFI)
            PARTITION_STYLE_RAW = 2  // Unpartitioned / Uninitialized
        }

        public enum STORAGE_BUS_TYPE : uint
        {
            BusTypeUnknown = 0,
            BusTypeScsi = 1,
            BusTypeAtapi = 2,
            BusTypeAta = 3,
            BusType1394 = 4,
            BusTypeSsa = 5,
            BusTypeFibre = 6,
            BusTypeUsb = 7,
            BusTypeRAID = 8,
            BusTypeiScsi = 9,
            BusTypeSas = 10,
            BusTypeSata = 11,
            BusTypeSd = 12,
            BusTypeMmc = 13,
            BusTypeVirtual = 14,
            BusTypeFileBackedVirtual = 15,
            BusTypeSpaces = 16,
            BusTypeNvme = 17,
            BusTypeScm = 18,
            BusTypeUfs = 19
        }

        #endregion

        #region Storage Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_DEVICE_NUMBER
        {
            public int DeviceType;
            public int DeviceNumber;
            public int PartitionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.I1)]
            public bool IncursSeekPenalty; // false = SSD, true = HDD
        }



        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_DEVICE_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public byte DeviceType;
            public byte DeviceTypeModifier;
            [MarshalAs(UnmanagedType.I1)]
            public bool RemovableMedia;
            [MarshalAs(UnmanagedType.I1)]
            public bool CommandQueueing;
            public uint VendorIdOffset;
            public uint ProductIdOffset;
            public uint ProductRevisionOffset;
            public uint SerialNumberOffset;
            public STORAGE_BUS_TYPE BusType;
            public uint RawPropertiesLength;
        }

        public class DiskDeviceInfo
        {
            public string Vendor { get; set; } = string.Empty;
            public string Product { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
            public string BusType { get; set; } = string.Empty;
            public bool RemovableMedia { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PARTITION_INFORMATION_MBR
        {
            public byte PartitionType;
            [MarshalAs(UnmanagedType.Bool)]
            public bool BootIndicator;
            [MarshalAs(UnmanagedType.Bool)]
            public bool RecognizedPartition;
            public uint HiddenSectors;
            public Guid PartitionId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PARTITION_INFORMATION_GPT
        {
            public Guid PartitionType;
            public Guid PartitionId;
            public ulong Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 36)]
            public string Name;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct PARTITION_INFORMATION_UNION
        {
            [FieldOffset(0)]
            public PARTITION_INFORMATION_MBR Mbr;

            [FieldOffset(0)]
            public PARTITION_INFORMATION_GPT Gpt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PARTITION_INFORMATION_EX
        {
            public PARTITION_STYLE PartitionStyle;
            public ushort PartitionOrdinal;
            public long StartingOffset;
            public long PartitionLength;
            public uint PartitionNumber;
            [MarshalAs(UnmanagedType.Bool)]
            public bool RewritePartition;
            [MarshalAs(UnmanagedType.Bool)]
            public bool IsServicePartition;
            public PARTITION_INFORMATION_UNION Union;
        }

        #endregion

        #region Win32 File & IOCTL P/Invoke

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            out STORAGE_DEVICE_NUMBER lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        #endregion

        #region Reusable Storage Query Methods

        /// <summary>
        /// Maps a volume drive letter (e.g. "C:", "D:") to its underlying physical disk number (PhysicalDrive0, 1...).
        /// </summary>
        public static int GetStorageDeviceNumber(string driveLetter)
        {
            try
            {
                string cleanLetter = driveLetter.TrimEnd('\\');
                string devicePath = $@"\\.\{cleanLetter}";
                using var handle = CreateFile(
                    devicePath,
                    0, // dwDesiredAccess = 0 (Query access without requiring admin elevation)
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    if (DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                        out STORAGE_DEVICE_NUMBER sdn, (uint)Marshal.SizeOf(typeof(STORAGE_DEVICE_NUMBER)),
                        out _, IntPtr.Zero))
                    {
                        return sdn.DeviceNumber;
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Queries whether the physical disk incurs a seek penalty (false = SSD, true = HDD).
        /// </summary>
        public static bool? QueryDiskSeekPenalty(int diskIndex)
        {
            if (diskIndex < 0) return null;
            try
            {
                string path = $@"\\.\PhysicalDrive{diskIndex}";
                using var handle = CreateFile(
                    path,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    var query = new STORAGE_PROPERTY_QUERY
                    {
                        PropertyId = 7, // StorageDeviceSeekPenaltyProperty
                        QueryType = 0,  // PropertyStandardQuery
                        AdditionalParameters = new byte[1]
                    };
                    int qSize = Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY));
                    IntPtr inBuf = Marshal.AllocHGlobal(qSize);
                    Marshal.StructureToPtr(query, inBuf, false);
                    int outSize = Marshal.SizeOf(typeof(DEVICE_SEEK_PENALTY_DESCRIPTOR));
                    IntPtr outBuf = Marshal.AllocHGlobal(outSize);
                    try
                    {
                        if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, inBuf, (uint)qSize, outBuf, (uint)outSize, out _, IntPtr.Zero))
                        {
                            var desc = Marshal.PtrToStructure<DEVICE_SEEK_PENALTY_DESCRIPTOR>(outBuf);
                            return desc.IncursSeekPenalty;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(inBuf);
                        Marshal.FreeHGlobal(outBuf);
                    }
                }
            }
            catch { }
            return null;
        }



        /// <summary>
        /// Queries the device hardware model, bus type, and removable media status via IOCTL_STORAGE_QUERY_PROPERTY.
        /// </summary>
        public static DiskDeviceInfo? QueryDiskDeviceInfo(int diskIndex)
        {
            if (diskIndex < 0) return null;
            try
            {
                string path = $@"\\.\PhysicalDrive{diskIndex}";
                using var handle = CreateFile(
                    path,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid) return null;

                var query = new STORAGE_PROPERTY_QUERY
                {
                    PropertyId = 0, // StorageDeviceProperty
                    QueryType = 0,  // PropertyStandardQuery
                    AdditionalParameters = new byte[1]
                };

                int qSize = Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY));
                IntPtr inBuf = Marshal.AllocHGlobal(qSize);
                Marshal.StructureToPtr(query, inBuf, false);

                int outSize = 2048;
                IntPtr outBuf = Marshal.AllocHGlobal(outSize);

                try
                {
                    if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, inBuf, (uint)qSize, outBuf, (uint)outSize, out uint bytesReturned, IntPtr.Zero) && bytesReturned >= 28)
                    {
                        var desc = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(outBuf);
                        string vendor = string.Empty;
                        string product = string.Empty;

                        if (desc.VendorIdOffset > 0 && desc.VendorIdOffset < bytesReturned)
                        {
                            vendor = TextSanitizer.SanitizeLabel(Marshal.PtrToStringAnsi(IntPtr.Add(outBuf, (int)desc.VendorIdOffset)));
                        }

                        if (desc.ProductIdOffset > 0 && desc.ProductIdOffset < bytesReturned)
                        {
                            product = TextSanitizer.SanitizeLabel(Marshal.PtrToStringAnsi(IntPtr.Add(outBuf, (int)desc.ProductIdOffset)));
                        }

                        string model = !string.IsNullOrWhiteSpace(vendor)
                            ? $"{vendor} {product}".Trim()
                            : (!string.IsNullOrWhiteSpace(product) ? product : $"Disk {diskIndex}");

                        string busType = desc.BusType switch
                        {
                            STORAGE_BUS_TYPE.BusTypeNvme => "NVMe",
                            STORAGE_BUS_TYPE.BusTypeSata => "SATA",
                            STORAGE_BUS_TYPE.BusTypeUsb => "USB",
                            STORAGE_BUS_TYPE.BusTypeScsi => "SCSI",
                            STORAGE_BUS_TYPE.BusTypeSas => "SAS",
                            STORAGE_BUS_TYPE.BusTypeAta or STORAGE_BUS_TYPE.BusTypeAtapi => "ATA",
                            STORAGE_BUS_TYPE.BusTypeSd or STORAGE_BUS_TYPE.BusTypeMmc => "SD/MMC",
                            _ => (uint)desc.BusType > 0 ? desc.BusType.ToString().Replace("BusType", "") : "Storage"
                        };

                        return new DiskDeviceInfo
                        {
                            Vendor = vendor,
                            Product = product,
                            Model = model,
                            BusType = busType,
                            RemovableMedia = desc.RemovableMedia
                        };
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuf);
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Queries the total disk capacity in bytes using IOCTL_DISK_GET_DRIVE_GEOMETRY_EX or IOCTL_DISK_GET_LENGTH_INFO.
        /// </summary>
        public static long QueryDiskLength(int diskIndex)
        {
            if (diskIndex < 0) return 0;
            try
            {
                string path = $@"\\.\PhysicalDrive{diskIndex}";
                using var handle = CreateFile(
                    path,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid) return 0;

                // 1. Try IOCTL_DISK_GET_DRIVE_GEOMETRY_EX (FILE_ANY_ACCESS - works with dwDesiredAccess=0 without elevation)
                int geomSize = 256;
                IntPtr geomBuf = Marshal.AllocHGlobal(geomSize);
                try
                {
                    if (DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0, geomBuf, (uint)geomSize, out _, IntPtr.Zero))
                    {
                        long size = Marshal.ReadInt64(geomBuf, 24); // DiskSize is at offset 24
                        if (size > 0) return size;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(geomBuf);
                }

                // 2. Fallback to IOCTL_DISK_GET_LENGTH_INFO
                IntPtr outBuf = Marshal.AllocHGlobal(8);
                try
                {
                    if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, outBuf, 8, out _, IntPtr.Zero))
                    {
                        long size = Marshal.ReadInt64(outBuf);
                        if (size > 0) return size;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Queries the drive layout (MBR vs GPT) and valid partition count using IOCTL_DISK_GET_DRIVE_LAYOUT_EX.
        /// </summary>
        public static (string style, int partitionCount) QueryDiskLayout(int diskIndex)
        {
            if (diskIndex < 0) return ("RAW", 0);
            try
            {
                string path = $@"\\.\PhysicalDrive{diskIndex}";
                using var handle = CreateFile(
                    path,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid) return ("RAW", 0);

                int bufferSize = 65536;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    if (DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_LAYOUT_EX, IntPtr.Zero, 0, buffer, (uint)bufferSize, out uint bytesReturned, IntPtr.Zero) && bytesReturned >= 8)
                    {
                        int styleNum = Marshal.ReadInt32(buffer, 0);
                        int count = Marshal.ReadInt32(buffer, 4);

                        string style = styleNum switch
                        {
                            0 => "MBR",
                            1 => "GPT",
                            _ => "RAW"
                        };

                        int validPartitions = 0;
                        int headerSize = 48;
                        int entrySize = 144;
                        for (int i = 0; i < count; i++)
                        {
                            int offset = headerSize + (i * entrySize);
                            if (offset + entrySize > bytesReturned) break;
                            long partLength = Marshal.ReadInt64(IntPtr.Add(buffer, offset + 16));
                            if (partLength > 0) validPartitions++;
                        }

                        return (style, validPartitions);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { }
            return ("RAW", 0);
        }

        #endregion
    }

    #region Microsoft GPT Partition Type GUIDs (Official Windows SDK)

    public static class GptPartitionTypes
    {
        // Basic Data Partition (Standard NTFS / FAT32 / exFAT user volume)
        public static readonly Guid BasicData = new("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");

        // EFI System Partition (ESP bootloader)
        public static readonly Guid EfiSystem = new("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");

        // Microsoft Reserved Partition (MSR metadata)
        public static readonly Guid MicrosoftReserved = new("e3c9e316-0b5c-4db8-817d-f92df00215ae");

        // Windows Recovery Environment (WinRE)
        public static readonly Guid WindowsRecovery = new("de94bba4-06d1-4d40-a16a-bfd50179d6ac");

        // Dynamic Disk LDM Data & Metadata
        public static readonly Guid LdmData = new("af9b60a0-1431-4f62-bc68-3311714a69ad");
        public static readonly Guid LdmMetadata = new("5808c8aa-7e8f-42e0-85d2-e1e90434cfb3");

        // Windows Storage Spaces
        public static readonly Guid StorageSpaces = new("e75caf8f-f680-4cee-afa3-b001e56efc2d");

        // Linux Partition Types
        public static readonly Guid LinuxData = new("0fc63daf-8483-4772-8e79-3d69d8477de4");
        public static readonly Guid LinuxSwap = new("0657fd6d-a4ab-43c4-84e5-0933c84b4f4f");
        public static readonly Guid LinuxLvm  = new("e6d6d379-f507-44c2-a23c-238f2a3df928");

        // Apple APFS & HFS+
        public static readonly Guid AppleApfs = new("7c3457ef-0000-11aa-aa11-00306543ecac");
        public static readonly Guid AppleHfs  = new("48465300-0000-11aa-aa11-00306543ecac");

        // Unused / Free partition entry
        public static readonly Guid Unused = Guid.Empty;

        public static string GetFriendlyName(Guid typeGuid)
        {
            if (typeGuid == BasicData) return "Basic Data Partition";
            if (typeGuid == EfiSystem) return "EFI System Partition (ESP)";
            if (typeGuid == MicrosoftReserved) return "Microsoft Reserved (MSR)";
            if (typeGuid == WindowsRecovery) return "Windows Recovery (WinRE)";
            if (typeGuid == LdmData || typeGuid == LdmMetadata) return "Dynamic Disk (LDM)";
            if (typeGuid == StorageSpaces) return "Windows Storage Spaces";
            if (typeGuid == LinuxData) return "Linux Data Partition";
            if (typeGuid == LinuxSwap) return "Linux Swap";
            if (typeGuid == LinuxLvm) return "Linux LVM";
            if (typeGuid == AppleApfs) return "Apple APFS Container";
            if (typeGuid == AppleHfs) return "Apple HFS+";
            return "OEM / Unknown Partition";
        }
    }

    #endregion

    #region MBR Partition Types (Legacy IDs)

    public static class MbrPartitionTypes
    {
        public static string GetFriendlyName(byte partitionType) => partitionType switch
        {
            0x07 => "NTFS / exFAT",
            0x0B or 0x0C => "FAT32",
            0x01 or 0x04 or 0x06 or 0x0E => "FAT12 / FAT16",
            0x05 or 0x0F => "Extended Partition",
            0x12 => "OEM Diagnostics / Recovery",
            0x27 => "Windows Recovery Environment",
            0x42 => "Windows Dynamic Disk",
            0x82 => "Linux Swap",
            0x83 => "Linux Native (ext4/btrfs)",
            0x8E => "Linux LVM",
            0xEE => "GPT Protective MBR",
            0xEF => "EFI System Partition",
            _ => $"Type 0x{partitionType:X2}"
        };
    }

    #endregion
}
