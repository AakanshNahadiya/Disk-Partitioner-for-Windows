using System;
using System.Collections.Generic;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;
using DiskPartitioner.Services;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class MockStorageQueryService : IStorageQueryService
    {
        public int SystemDiskIndex { get; set; } = 0;

        public int GetSystemDiskIndex() => SystemDiskIndex;

        public List<PhysicalDiskItem> GetPhysicalDisks() => new();

        public List<PartitionItem> GetPartitions(int diskIndex, long totalDiskSizeBytes = 0) => new();
    }

    public class SafetyGuardrailServiceTests
    {
        private readonly MockStorageQueryService _mockStorage;
        private readonly SafetyGuardrailService _guardrails;

        public SafetyGuardrailServiceTests()
        {
            _mockStorage = new MockStorageQueryService { SystemDiskIndex = 0 };
            _guardrails = new SafetyGuardrailService(_mockStorage);
        }

        [Fact]
        public void CanCleanDisk_BlocksActiveOsDisk()
        {
            var osDisk = new PhysicalDiskItem
            {
                DiskIndex = 0,
                IsSystemDrive = true,
                IsBootDrive = true,
                Model = "NVMe Samsung 980 PRO"
            };

            var result = _guardrails.CanCleanDisk(osDisk);

            Assert.False(result.IsAllowed);
            Assert.Contains("permanently protected", result.Reason);
        }

        [Fact]
        public void CanCleanDisk_AllowsSecondaryDisk()
        {
            var secDisk = new PhysicalDiskItem
            {
                DiskIndex = 1,
                IsSystemDrive = false,
                IsBootDrive = false,
                Model = "Crucial MX500"
            };

            var result = _guardrails.CanCleanDisk(secDisk);

            Assert.True(result.IsAllowed);
            Assert.Empty(result.Reason);
        }

        [Fact]
        public void CanConvertDisk_BlocksOsDisk()
        {
            var osDisk = new PhysicalDiskItem
            {
                DiskIndex = 0,
                IsSystemDrive = true,
                PartitionsCount = 0
            };

            var result = _guardrails.CanConvertDisk(osDisk);

            Assert.False(result.IsAllowed);
            Assert.Contains("cannot be converted", result.Reason);
        }

        [Fact]
        public void CanConvertDisk_BlocksDiskWithPartitions()
        {
            var secDisk = new PhysicalDiskItem
            {
                DiskIndex = 1,
                IsSystemDrive = false,
                PartitionsCount = 2
            };

            var result = _guardrails.CanConvertDisk(secDisk);

            Assert.False(result.IsAllowed);
            Assert.Contains("contains 2 partitions", result.Reason);
        }

        [Fact]
        public void CanConvertDisk_AllowsCleanSecondaryDisk()
        {
            var secDisk = new PhysicalDiskItem
            {
                DiskIndex = 1,
                IsSystemDrive = false,
                PartitionsCount = 0
            };

            var result = _guardrails.CanConvertDisk(secDisk);

            Assert.True(result.IsAllowed);
        }

        [Fact]
        public void CanSetDiskOffline_BlocksOsDisk_AllowsSecondary()
        {
            var osDisk = new PhysicalDiskItem { DiskIndex = 0, IsSystemDrive = true };
            var secDisk = new PhysicalDiskItem { DiskIndex = 1, IsSystemDrive = false };

            Assert.False(_guardrails.CanSetDiskOffline(osDisk).IsAllowed);
            Assert.True(_guardrails.CanSetDiskOffline(secDisk).IsAllowed);
        }

        [Fact]
        public void CanSetDiskReadOnly_BlocksOsDisk_AllowsSecondary()
        {
            var osDisk = new PhysicalDiskItem { DiskIndex = 0, IsSystemDrive = true };
            var secDisk = new PhysicalDiskItem { DiskIndex = 1, IsSystemDrive = false };

            Assert.False(_guardrails.CanSetDiskReadOnly(osDisk).IsAllowed);
            Assert.True(_guardrails.CanSetDiskReadOnly(secDisk).IsAllowed);
        }

        [Fact]
        public void CanDeletePartition_FollowsDiskpartStandard_BlocksOnlyActiveSystemVolume()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };

            var systemPart = new PartitionItem
            {
                DiskIndex = 0,
                PartitionNumber = 3,
                IsSystem = true,
                DriveLetter = "C:"
            };

            var efiPart = new PartitionItem
            {
                DiskIndex = 0,
                PartitionNumber = 1,
                IsBoot = true,
                GptTypeGuid = GptPartitionTypes.EfiSystem
            };

            var dataPart = new PartitionItem
            {
                DiskIndex = 1,
                PartitionNumber = 1,
                IsSystem = false,
                IsBoot = false,
                DriveLetter = "D:"
            };

            // DiskPart allows deleting EFI partitions with OVERRIDE, but permanently blocks the active Windows system volume
            Assert.False(_guardrails.CanDeletePartition(disk, systemPart).IsAllowed);
            Assert.True(_guardrails.CanDeletePartition(disk, efiPart).IsAllowed);
            Assert.True(_guardrails.CanDeletePartition(disk, dataPart).IsAllowed);
        }

        [Fact]
        public void CanDeletePartition_BlocksUnallocatedSpace()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 1 };
            var unallocated = new PartitionItem { DiskIndex = 1, IsUnallocated = true };

            var result = _guardrails.CanDeletePartition(disk, unallocated);

            Assert.False(result.IsAllowed);
            Assert.Contains("Unallocated space", result.Reason);
        }

        [Fact]
        public void CanFormatPartition_BlocksWindowsSystemVolume()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };
            var sysVol = new PartitionItem { DiskIndex = 0, PartitionNumber = 3, IsSystem = true, DriveLetter = "C:" };
            var dataVol = new PartitionItem { DiskIndex = 1, PartitionNumber = 1, IsSystem = false, DriveLetter = "D:" };

            Assert.False(_guardrails.CanFormatPartition(disk, sysVol).IsAllowed);
            Assert.True(_guardrails.CanFormatPartition(disk, dataVol).IsAllowed);
        }

        [Fact]
        public void CanModifyDriveLetter_ProtectsSystemDriveLetterC()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };
            var sysVol = new PartitionItem { DiskIndex = 0, PartitionNumber = 3, IsSystem = true, DriveLetter = "C:" };
            var dataVol = new PartitionItem { DiskIndex = 1, PartitionNumber = 1, IsSystem = false, DriveLetter = "D:" };

            Assert.False(_guardrails.CanModifyDriveLetter(disk, sysVol).IsAllowed);
            Assert.True(_guardrails.CanModifyDriveLetter(disk, dataVol).IsAllowed);
        }

        [Fact]
        public void CanModifyDriveLetter_ProtectsEfiSystemPartition()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };
            var efi = new PartitionItem { DiskIndex = 0, PartitionNumber = 1, IsBoot = true, GptTypeGuid = GptPartitionTypes.EfiSystem };

            var result = _guardrails.CanModifyDriveLetter(disk, efi);

            Assert.False(result.IsAllowed);
            Assert.Contains("EFI System Boot", result.Reason);
        }

        [Fact]
        public void CanShrinkPartition_BlocksEfiMsrRecoveryAndNonNtfs()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };

            var efi = new PartitionItem { DiskIndex = 0, PartitionNumber = 1, IsBoot = true, GptTypeGuid = GptPartitionTypes.EfiSystem, FileSystem = "FAT32" };
            var msr = new PartitionItem { DiskIndex = 0, PartitionNumber = 2, GptTypeGuid = GptPartitionTypes.MicrosoftReserved };
            var recovery = new PartitionItem { DiskIndex = 0, PartitionNumber = 4, GptTypeGuid = GptPartitionTypes.WindowsRecovery };
            var fat32Data = new PartitionItem { DiskIndex = 1, PartitionNumber = 1, FileSystem = "FAT32" };
            var ntfsData = new PartitionItem { DiskIndex = 1, PartitionNumber = 2, FileSystem = "NTFS", FreeSizeBytes = 500L * 1024 * 1024 };

            Assert.False(_guardrails.CanShrinkPartition(disk, efi).IsAllowed);
            Assert.False(_guardrails.CanShrinkPartition(disk, msr).IsAllowed);
            Assert.False(_guardrails.CanShrinkPartition(disk, recovery).IsAllowed);
            Assert.False(_guardrails.CanShrinkPartition(disk, fat32Data).IsAllowed);
            Assert.True(_guardrails.CanShrinkPartition(disk, ntfsData).IsAllowed);
        }

        [Fact]
        public void CanExtendPartition_BlocksEfiMsrAndNonNtfs()
        {
            var disk = new PhysicalDiskItem { DiskIndex = 0 };

            var efi = new PartitionItem { DiskIndex = 0, PartitionNumber = 1, IsBoot = true, GptTypeGuid = GptPartitionTypes.EfiSystem };
            var msr = new PartitionItem { DiskIndex = 0, PartitionNumber = 2, GptTypeGuid = GptPartitionTypes.MicrosoftReserved };
            var exfatData = new PartitionItem { DiskIndex = 1, PartitionNumber = 1, FileSystem = "exFAT" };
            var ntfsData = new PartitionItem { DiskIndex = 1, PartitionNumber = 2, FileSystem = "NTFS" };

            Assert.False(_guardrails.CanExtendPartition(disk, efi).IsAllowed);
            Assert.False(_guardrails.CanExtendPartition(disk, msr).IsAllowed);
            Assert.False(_guardrails.CanExtendPartition(disk, exfatData).IsAllowed);
            Assert.True(_guardrails.CanExtendPartition(disk, ntfsData).IsAllowed);
        }
    }
}
