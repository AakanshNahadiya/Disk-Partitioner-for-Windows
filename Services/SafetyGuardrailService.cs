using System;
using System.IO;
using DiskPartitioner.Helpers;
using DiskPartitioner.Models;

namespace DiskPartitioner.Services
{
    public class GuardrailCheckResult
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; } = string.Empty;

        public static GuardrailCheckResult Allowed() => new() { IsAllowed = true };
        public static GuardrailCheckResult Blocked(string reason) => new() { IsAllowed = false, Reason = reason };
    }

    public interface ISafetyGuardrailService
    {
        GuardrailCheckResult CanCleanDisk(PhysicalDiskItem disk);
        GuardrailCheckResult CanConvertDisk(PhysicalDiskItem disk);
        GuardrailCheckResult CanSetDiskOffline(PhysicalDiskItem disk);
        GuardrailCheckResult CanSetDiskReadOnly(PhysicalDiskItem disk);
        GuardrailCheckResult CanDeletePartition(PhysicalDiskItem disk, PartitionItem partition);
        GuardrailCheckResult CanFormatPartition(PhysicalDiskItem disk, PartitionItem partition);
        GuardrailCheckResult CanModifyDriveLetter(PhysicalDiskItem disk, PartitionItem partition);
        GuardrailCheckResult CanShrinkPartition(PhysicalDiskItem disk, PartitionItem partition);
        GuardrailCheckResult CanExtendPartition(PhysicalDiskItem disk, PartitionItem partition);
    }

    public class SafetyGuardrailService : ISafetyGuardrailService
    {
        private readonly IStorageQueryService _storageQueryService;

        public SafetyGuardrailService(IStorageQueryService storageQueryService)
        {
            _storageQueryService = storageQueryService;
        }

        public GuardrailCheckResult CanCleanDisk(PhysicalDiskItem disk)
        {
            if (disk == null)
            {
                return GuardrailCheckResult.Blocked("No disk was selected.");
            }

            int sysDisk = _storageQueryService.GetSystemDiskIndex();
            if (disk.DiskIndex == sysDisk || disk.IsProtectedOsDrive)
            {
                return GuardrailCheckResult.Blocked(
                    $"Disk {disk.DiskIndex} contains the active Windows Operating System and is permanently protected against clean or wipe operations.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanConvertDisk(PhysicalDiskItem disk)
        {
            if (disk == null)
            {
                return GuardrailCheckResult.Blocked("No disk was selected.");
            }

            int sysDisk = _storageQueryService.GetSystemDiskIndex();
            if (disk.DiskIndex == sysDisk || disk.IsProtectedOsDrive)
            {
                return GuardrailCheckResult.Blocked(
                    $"Disk {disk.DiskIndex} contains the active Windows Operating System and cannot be converted while Windows is running.");
            }

            if (disk.PartitionsCount > 0)
            {
                return GuardrailCheckResult.Blocked(
                    $"Disk {disk.DiskIndex} contains {disk.PartitionsCount} partitions. All partitions must be cleaned or deleted before converting between MBR and GPT.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanSetDiskOffline(PhysicalDiskItem disk)
        {
            if (disk == null)
            {
                return GuardrailCheckResult.Blocked("No disk was selected.");
            }

            int sysDisk = _storageQueryService.GetSystemDiskIndex();
            if (disk.DiskIndex == sysDisk || disk.IsProtectedOsDrive)
            {
                return GuardrailCheckResult.Blocked(
                    $"Disk {disk.DiskIndex} contains the active Windows Operating System and cannot be taken offline.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanSetDiskReadOnly(PhysicalDiskItem disk)
        {
            if (disk == null)
            {
                return GuardrailCheckResult.Blocked("No disk was selected.");
            }

            int sysDisk = _storageQueryService.GetSystemDiskIndex();
            if (disk.DiskIndex == sysDisk || disk.IsProtectedOsDrive)
            {
                return GuardrailCheckResult.Blocked(
                    $"Disk {disk.DiskIndex} contains the active Windows Operating System. Setting the OS drive to read-only would crash Windows.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanDeletePartition(PhysicalDiskItem disk, PartitionItem partition)
        {
            if (partition == null)
            {
                return GuardrailCheckResult.Blocked("No partition was selected.");
            }

            if (partition.IsUnallocated)
            {
                return GuardrailCheckResult.Blocked("Unallocated space is already empty free space and cannot be deleted.");
            }

            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            if (partition.IsSystem || (partition.DriveLetter != null && partition.DriveLetter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase)))
            {
                return GuardrailCheckResult.Blocked(
                    $"The active Windows System partition ({systemDrive}\\) contains the running operating system and page files. Microsoft Virtual Disk Service disallows deleting the current boot, system, and paging volume.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanFormatPartition(PhysicalDiskItem disk, PartitionItem partition)
        {
            if (partition == null)
            {
                return GuardrailCheckResult.Blocked("No partition was selected.");
            }

            if (partition.IsUnallocated)
            {
                return GuardrailCheckResult.Blocked("Unallocated space cannot be formatted. Create a partition first.");
            }

            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            if (partition.IsSystem || (partition.DriveLetter != null && partition.DriveLetter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase)))
            {
                return GuardrailCheckResult.Blocked(
                    $"The active Windows boot volume ({systemDrive}\\) cannot be formatted while Windows is running.");
            }

            if (partition.IsBoot || partition.GptTypeGuid == GptPartitionTypes.EfiSystem)
            {
                return GuardrailCheckResult.Blocked(
                    "The active EFI System Boot partition cannot be formatted while Windows is running.");
            }

            if (partition.GptTypeGuid == GptPartitionTypes.MicrosoftReserved)
            {
                return GuardrailCheckResult.Blocked(
                    "The Microsoft Reserved (MSR) partition does not have a file system and cannot be formatted.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanModifyDriveLetter(PhysicalDiskItem disk, PartitionItem partition)
        {
            if (partition == null)
            {
                return GuardrailCheckResult.Blocked("No partition was selected.");
            }

            if (partition.IsUnallocated)
            {
                return GuardrailCheckResult.Blocked("Unallocated space cannot have a drive letter.");
            }

            if (partition.GptTypeGuid == GptPartitionTypes.MicrosoftReserved)
            {
                return GuardrailCheckResult.Blocked("The Microsoft Reserved (MSR) partition cannot have a drive letter.");
            }

            if (partition.IsBoot || partition.GptTypeGuid == GptPartitionTypes.EfiSystem)
            {
                return GuardrailCheckResult.Blocked("The EFI System Boot partition is reserved for UEFI firmware and cannot have its drive letter modified.");
            }

            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            if (partition.IsSystem || (partition.DriveLetter != null && partition.DriveLetter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase)))
            {
                return GuardrailCheckResult.Blocked(
                    $"The Windows system drive letter ({systemDrive}\\) cannot be changed or removed while Windows is running.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanShrinkPartition(PhysicalDiskItem disk, PartitionItem partition)
        {
            if (partition == null)
            {
                return GuardrailCheckResult.Blocked("No partition was selected.");
            }

            if (partition.IsUnallocated)
            {
                return GuardrailCheckResult.Blocked("Unallocated space is already free space and cannot be shrunk.");
            }

            if (partition.IsBoot || partition.GptTypeGuid == GptPartitionTypes.EfiSystem)
            {
                return GuardrailCheckResult.Blocked("The EFI System Boot partition is required by Windows UEFI firmware and cannot be shrunk.");
            }

            if (partition.GptTypeGuid == GptPartitionTypes.MicrosoftReserved)
            {
                return GuardrailCheckResult.Blocked("The Microsoft Reserved (MSR) partition is 16 MB and cannot be shrunk.");
            }

            if (partition.GptTypeGuid == GptPartitionTypes.WindowsRecovery)
            {
                return GuardrailCheckResult.Blocked("Windows Recovery partitions cannot be shrunk.");
            }

            if (!string.IsNullOrWhiteSpace(partition.FileSystem))
            {
                string fs = partition.FileSystem.Trim().ToUpperInvariant();
                if (fs != "NTFS" && fs != "REFS")
                {
                    return GuardrailCheckResult.Blocked($"The {partition.FileSystem} file system does not support online shrinking. Windows only supports shrinking NTFS and ReFS volumes.");
                }
            }

            if (partition.FreeSizeBytes >= 0 && partition.FreeSizeBytes < 50 * 1024 * 1024)
            {
                return GuardrailCheckResult.Blocked($"Partition {partition.PartitionNumber} has only {partition.FormattedFreeSize} free space. At least 50 MB of free space is required to shrink a volume.");
            }

            return GuardrailCheckResult.Allowed();
        }

        public GuardrailCheckResult CanExtendPartition(PhysicalDiskItem disk, PartitionItem partition)
        {
            if (partition == null)
            {
                return GuardrailCheckResult.Blocked("No partition was selected.");
            }

            if (partition.IsUnallocated)
            {
                return GuardrailCheckResult.Blocked("Unallocated space cannot be extended. Use 'Create New Partition' instead.");
            }

            if (partition.IsBoot || partition.GptTypeGuid == GptPartitionTypes.EfiSystem)
            {
                return GuardrailCheckResult.Blocked("The EFI System Boot partition cannot be extended.");
            }

            if (partition.GptTypeGuid == GptPartitionTypes.MicrosoftReserved)
            {
                return GuardrailCheckResult.Blocked("The Microsoft Reserved (MSR) partition cannot be extended.");
            }

            if (!string.IsNullOrWhiteSpace(partition.FileSystem))
            {
                string fs = partition.FileSystem.Trim().ToUpperInvariant();
                if (fs != "NTFS" && fs != "REFS")
                {
                    return GuardrailCheckResult.Blocked($"The {partition.FileSystem} file system does not support online extending. Windows only supports extending NTFS and ReFS volumes.");
                }
            }

            return GuardrailCheckResult.Allowed();
        }
    }
}
