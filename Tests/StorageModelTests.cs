using System;
using System.Linq;
using System.Runtime.InteropServices;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;
using DiskPartitioner.Services;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class StorageModelTests
    {
        [Fact]
        public void PhysicalDiskItem_ProtectedOsDriveBadge()
        {
            var disk = new PhysicalDiskItem
            {
                DiskIndex = 0,
                Model = "Samsung SSD 980 500GB",
                TotalSizeBytes = 500_000_000_000L,
                BusType = "NVMe",
                MediaType = "SSD",
                PartitionStyle = "GPT",
                IsSystemDrive = true,
                PartitionsCount = 4
            };

            Assert.True(disk.IsProtectedOsDrive);
            Assert.Equal("[Windows OS Drive - Protected]", disk.ProtectionBadge);
            Assert.Contains("Windows OS Drive - Protected", disk.DisplayName);
            Assert.Contains("Windows Operating System Drive", disk.SpeechDescription);
        }

        [Fact]
        public void PhysicalDiskItem_CopyFrom_SyncsPropertiesInPlace()
        {
            var diskA = new PhysicalDiskItem { DiskIndex = 1, Model = "Old Name", PartitionsCount = 1 };
            var diskB = new PhysicalDiskItem { DiskIndex = 1, Model = "New Name", PartitionsCount = 3, IsReadOnly = true };

            diskA.CopyFrom(diskB);

            Assert.Equal("New Name", diskA.Model);
            Assert.Equal(3, diskA.PartitionsCount);
            Assert.True(diskA.IsReadOnly);
        }

        [Fact]
        public void PartitionItem_RoleBadges_IdentifiedCorrectly()
        {
            var sysPart = new PartitionItem { IsSystem = true, DriveLetter = "C:" };
            Assert.Equal("[System - C:\\]", sysPart.RoleBadge);

            var bootPart = new PartitionItem { IsBoot = true };
            Assert.Equal("[Boot ESP]", bootPart.RoleBadge);

            var unallocPart = new PartitionItem { IsUnallocated = true };
            Assert.Equal("[Free Space]", unallocPart.RoleBadge);

            var msrPart = new PartitionItem { GptTypeGuid = GptPartitionTypes.MicrosoftReserved };
            Assert.Equal("[MSR]", msrPart.RoleBadge);

            var oemPart = new PartitionItem { GptTypeGuid = GptPartitionTypes.WindowsRecovery };
            Assert.Equal("[Recovery]", oemPart.RoleBadge);

            var activePart = new PartitionItem { IsActive = true, IsBoot = false };
            Assert.Equal("[Active]", activePart.RoleBadge);

            var logicalPart = new PartitionItem { PartitionStyle = "MBR", PartitionNumber = 5 };
            Assert.Equal("[Logical]", logicalPart.RoleBadge);
        }

        [Fact]
        public void PartitionItem_SpeechDescription_IncludesFreeSpace()
        {
            var part = new PartitionItem
            {
                DiskIndex = 0,
                PartitionNumber = 3,
                PartitionTypeFriendlyName = "Basic Data Partition",
                VolumeLabel = "OS",
                DriveLetter = "C:",
                FileSystem = "NTFS",
                SizeBytes = 500L * 1024 * 1024 * 1024,
                FreeSizeBytes = 200L * 1024 * 1024 * 1024,
                IsSystem = true
            };

            string speech = part.SpeechDescription;

            Assert.Contains("Partition 3", speech);
            Assert.Contains("Drive C:", speech);
            Assert.Contains("200.00 GB free", speech);
            Assert.Contains("Windows Operating System volume", speech);
        }

        [Fact]
        public void PartitionItem_SpeechDescription_UsbActiveMbrPartition_ReportsActiveNotEfi()
        {
            var usbPart = new PartitionItem
            {
                DiskIndex = 1,
                PartitionNumber = 1,
                PartitionTypeFriendlyName = "FAT32",
                VolumeLabel = "ESD-USB",
                DriveLetter = "F:",
                FileSystem = "FAT32",
                SizeBytes = 15_230_000_000L,
                FreeSizeBytes = 8_500_000_000L,
                IsActive = true,
                IsBoot = false
            };

            string speech = usbPart.SpeechDescription;

            Assert.Contains("Partition 1", speech);
            Assert.Contains("ESD-USB", speech);
            Assert.Contains("FAT32", speech);
            Assert.Contains("Drive F:", speech);
            Assert.Contains("Active partition", speech);
            Assert.DoesNotContain("EFI Boot", speech);
        }

        [Fact]
        public void PartitionItem_CopyFrom_SyncsPropertiesInPlace()
        {
            var partA = new PartitionItem { PartitionNumber = 1, VolumeLabel = "Old", FreeSizeBytes = 1000 };
            var partB = new PartitionItem { PartitionNumber = 1, VolumeLabel = "New", FreeSizeBytes = 2000, DriveLetter = "D:" };

            partA.CopyFrom(partB);

            Assert.Equal("New", partA.VolumeLabel);
            Assert.Equal(2000, partA.FreeSizeBytes);
            Assert.Equal("D:", partA.DriveLetter);
        }

        [Fact]
        public void PartitionItem_SpeechDescription_MsrPartition_DoesNotRepeatMicrosoftReserved()
        {
            var msrPart = new PartitionItem
            {
                DiskIndex = 0,
                PartitionNumber = 2,
                PartitionTypeFriendlyName = "Microsoft Reserved (MSR)",
                GptTypeGuid = GptPartitionTypes.MicrosoftReserved,
                SizeBytes = 16L * 1024 * 1024,
                PartitionStyle = "GPT"
            };

            string speech = msrPart.SpeechDescription;

            Assert.Equal("Partition 2, Microsoft Reserved (MSR), 16.0 MB", speech);
        }

        [Fact]
        public void GetPartitions_ReturnsValidListOnSystemDisk()
        {
            var service = new StorageQueryService();
            var parts = service.GetPartitions(0);
            Assert.NotNull(parts);
            Assert.NotEmpty(parts);
            Assert.All(parts, p => Assert.True(p.SizeBytes >= 0));
        }
    }
}
