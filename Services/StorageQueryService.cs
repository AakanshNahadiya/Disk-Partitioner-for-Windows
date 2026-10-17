using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;

namespace DiskPartitioner.Services
{
    public interface IStorageQueryService
    {
        int GetSystemDiskIndex();
        List<PhysicalDiskItem> GetPhysicalDisks();
        List<PartitionItem> GetPartitions(int diskIndex, long totalDiskSizeBytes = 0);
    }

    public class StorageQueryService : IStorageQueryService
    {
        private int _cachedSystemDiskIndex = -2;

        public int GetSystemDiskIndex()
        {
            if (_cachedSystemDiskIndex >= -1) return _cachedSystemDiskIndex;

            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
                int index = NativeMethods.GetStorageDeviceNumber(sysDrive);
                if (index >= 0)
                {
                    _cachedSystemDiskIndex = index;
                    return index;
                }
            }
            catch { }

            _cachedSystemDiskIndex = 0; // Standard fallback
            return _cachedSystemDiskIndex;
        }

        public List<PhysicalDiskItem> GetPhysicalDisks()
        {
            var diskList = new List<PhysicalDiskItem>();
            int systemDiskIndex = GetSystemDiskIndex();

            // Native Win32 physical drive discovery: high-speed, zero WMI/COM memory footprint
            int consecutiveMissing = 0;
            for (int index = 0; index < 32; index++)
            {
                var info = NativeMethods.QueryDiskDeviceInfo(index);
                if (info == null)
                {
                    consecutiveMissing++;
                    if (consecutiveMissing >= 2 && index > 0) break;
                    continue;
                }

                consecutiveMissing = 0;

                long size = NativeMethods.QueryDiskLength(index);
                var (style, partCount) = NativeMethods.QueryDiskLayout(index);
                bool? seekPenalty = NativeMethods.QueryDiskSeekPenalty(index);

                string mediaType = seekPenalty switch
                {
                    false => "SSD",
                    true => "HDD",
                    _ => (info.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase) || info.RemovableMedia) ? "Removable USB" : "Drive"
                };

                bool isOs = (index == systemDiskIndex);

                diskList.Add(new PhysicalDiskItem
                {
                    DiskIndex = index,
                    Model = info.Model,
                    BusType = info.BusType,
                    MediaType = mediaType,
                    TotalSizeBytes = size,
                    PartitionStyle = style,
                    IsSystemDrive = isOs,
                    IsBootDrive = isOs,
                    PartitionsCount = partCount
                });
            }

            return diskList.OrderBy(d => d.DiskIndex).ToList();
        }

        public List<PartitionItem> GetPartitions(int diskIndex, long totalDiskSizeBytes = 0)
        {
            var partitions = new List<PartitionItem>();
            if (diskIndex < 0) return partitions;

            if (totalDiskSizeBytes <= 0)
            {
                totalDiskSizeBytes = NativeMethods.QueryDiskLength(diskIndex);
            }

            int systemDiskIndex = GetSystemDiskIndex();
            var volumeMap = QueryDriveLettersForDisk(diskIndex);

            try
            {
                string devicePath = $@"\\.\PhysicalDrive{diskIndex}";
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    0, // dwDesiredAccess = 0 (Query access without requiring admin elevation)
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    int bufferSize = 65536;
                    IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                    try
                    {
                        if (NativeMethods.DeviceIoControl(
                            handle,
                            NativeMethods.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                            IntPtr.Zero,
                            0,
                            buffer,
                            (uint)bufferSize,
                            out uint bytesReturned,
                            IntPtr.Zero) && bytesReturned >= 48)
                        {
                            int style = Marshal.ReadInt32(buffer, 0); // 0 = MBR, 1 = GPT
                            int count = Marshal.ReadInt32(buffer, 4);

                            // If totalDiskSizeBytes was not available from geometry, derive from GPT UsableLength
                            if (style == 1 && totalDiskSizeBytes <= 0)
                            {
                                long gptStartUsable = Marshal.ReadInt64(buffer, 24);
                                long gptUsableLength = Marshal.ReadInt64(buffer, 32);
                                if (gptUsableLength > 0)
                                {
                                    totalDiskSizeBytes = gptStartUsable + gptUsableLength;
                                }
                            }
                            int headerSize = 48;
                            int entrySize = 144;

                            for (int i = 0; i < count; i++)
                            {
                                int offset = headerSize + (i * entrySize);
                                if (offset + entrySize > bytesReturned) break;

                                IntPtr pEntry = IntPtr.Add(buffer, offset);
                                long startingOffset = Marshal.ReadInt64(pEntry, 8);
                                long partitionLength = Marshal.ReadInt64(pEntry, 16);
                                uint partitionNumber = (uint)Marshal.ReadInt32(pEntry, 24);

                                if (partitionLength <= 0) continue;

                                var item = new PartitionItem
                                {
                                    DiskIndex = diskIndex,
                                    PartitionNumber = partitionNumber,
                                    StartingOffset = startingOffset,
                                    SizeBytes = partitionLength,
                                    PartitionStyle = style == 1 ? "GPT" : "MBR"
                                };

                                if (style == 1) // GPT
                                {
                                    byte[] guidBytes = new byte[16];
                                    Marshal.Copy(IntPtr.Add(pEntry, 32), guidBytes, 0, 16);
                                    Guid typeGuid = new Guid(guidBytes);
                                    item.GptTypeGuid = typeGuid;
                                    item.PartitionTypeFriendlyName = GptPartitionTypes.GetFriendlyName(typeGuid);
                                    item.IsBoot = (typeGuid == GptPartitionTypes.EfiSystem);
                                    item.IsActive = false;

                                    // Read GPT partition name from PARTITION_INFORMATION_GPT.Name (offset 72, WCHAR[36])
                                    string gptName = TextSanitizer.SanitizeLabel(Marshal.PtrToStringUni(IntPtr.Add(pEntry, 72), 36));
                                    if (!string.IsNullOrWhiteSpace(gptName))
                                    {
                                        item.GptPartitionName = gptName;
                                    }
                                }
                                else // MBR
                                {
                                    byte mbrType = Marshal.ReadByte(pEntry, 32);
                                    bool bootIndicator = Marshal.ReadByte(pEntry, 33) != 0;
                                    item.MbrType = mbrType;
                                    item.PartitionTypeFriendlyName = MbrPartitionTypes.GetFriendlyName(mbrType);
                                    item.IsActive = bootIndicator;
                                    item.IsBoot = (mbrType == 0xEF);
                                }

                                string systemDriveLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

                                // Match with mapped volumes (drive letter, label, filesystem)
                                if (volumeMap.TryGetValue(partitionNumber, out var volInfo))
                                {
                                    item.DriveLetter = volInfo.DriveLetter;
                                    item.VolumeLabel = volInfo.VolumeLabel ?? string.Empty;
                                    item.FileSystem = volInfo.FileSystem;
                                    item.FreeSizeBytes = volInfo.FreeBytes;
                                    if (volInfo.DriveLetter != null && volInfo.DriveLetter.Equals(systemDriveLetter, StringComparison.OrdinalIgnoreCase))
                                    {
                                        item.IsSystem = true;
                                    }
                                }
                                else
                                {
                                    item.VolumeLabel = string.Empty;
                                }

                                partitions.Add(item);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch { }

            // Sort by starting offset
            partitions = partitions.OrderBy(p => p.StartingOffset).ToList();

            // Calculate unallocated space blocks
            var resultWithUnallocated = new List<PartitionItem>();
            long currentOffset = 1048576; // Standard 1 MB alignment

            foreach (var part in partitions)
            {
                long gap = part.StartingOffset - currentOffset;
                // If gap is >= 1 MB, represent as Unallocated Space
                if (gap >= 1048576)
                {
                    resultWithUnallocated.Add(new PartitionItem
                    {
                        DiskIndex = diskIndex,
                        PartitionNumber = 0,
                        StartingOffset = currentOffset,
                        SizeBytes = gap,
                        IsUnallocated = true,
                        PartitionTypeFriendlyName = "Unallocated Space"
                    });
                }

                resultWithUnallocated.Add(part);
                currentOffset = part.StartingOffset + part.SizeBytes;
            }

            // Check for unallocated space at the end of the disk (>= 1 MB)
            if (totalDiskSizeBytes > 0 && (totalDiskSizeBytes - currentOffset) >= 1048576)
            {
                resultWithUnallocated.Add(new PartitionItem
                {
                    DiskIndex = diskIndex,
                    PartitionNumber = 0,
                    StartingOffset = currentOffset,
                    SizeBytes = totalDiskSizeBytes - currentOffset,
                    IsUnallocated = true,
                    PartitionTypeFriendlyName = "Unallocated Space"
                });
            }

            return resultWithUnallocated;
        }

        private class VolumeInfo
        {
            public string? DriveLetter { get; set; }
            public string VolumeLabel { get; set; } = string.Empty;
            public string FileSystem { get; set; } = string.Empty;
            public long FreeBytes { get; set; } = -1;
        }

        private Dictionary<uint, VolumeInfo> QueryDriveLettersForDisk(int targetDiskIndex)
        {
            var map = new Dictionary<uint, VolumeInfo>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                    string letter = drive.Name.TrimEnd('\\');
                    string devPath = $@"\\.\{letter}";

                    using var h = NativeMethods.CreateFile(
                        devPath,
                        0,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
                        IntPtr.Zero,
                        NativeMethods.OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (!h.IsInvalid)
                    {
                        if (NativeMethods.DeviceIoControl(
                            h,
                            NativeMethods.IOCTL_STORAGE_GET_DEVICE_NUMBER,
                            IntPtr.Zero,
                            0,
                            out NativeMethods.STORAGE_DEVICE_NUMBER sdn,
                            (uint)Marshal.SizeOf(typeof(NativeMethods.STORAGE_DEVICE_NUMBER)),
                            out _,
                            IntPtr.Zero))
                        {
                            if (sdn.DeviceNumber == targetDiskIndex && sdn.PartitionNumber > 0)
                            {
                                string label = string.Empty;
                                string format = string.Empty;
                                long freeBytes = -1;
                                try
                                {
                                    if (drive.IsReady)
                                    {
                                        label = TextSanitizer.SanitizeLabel(drive.VolumeLabel);
                                        format = TextSanitizer.SanitizeLabel(drive.DriveFormat);
                                        freeBytes = drive.TotalFreeSpace;
                                    }
                                }
                                catch { }

                                map[(uint)sdn.PartitionNumber] = new VolumeInfo
                                {
                                    DriveLetter = letter,
                                    VolumeLabel = label,
                                    FileSystem = format,
                                    FreeBytes = freeBytes
                                };
                            }
                        }
                    }
                }
            }
            catch { }
            return map;
        }
    }
}
