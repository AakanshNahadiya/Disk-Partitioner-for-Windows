using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskPartitioner.Helpers;

namespace DiskPartitioner.Models
{
    public class PartitionItem : INotifyPropertyChanged
    {
        private int _diskIndex;
        private uint _partitionNumber;
        private string? _driveLetter;
        private string _volumeLabel = string.Empty;
        private string _fileSystem = string.Empty;
        private long _startingOffset;
        private long _sizeBytes;
        private string _partitionStyle = "GPT";
        private Guid _gptTypeGuid = Guid.Empty;
        private byte _mbrType;
        private string _partitionTypeFriendlyName = "Basic Data Partition";
        private bool _isBoot;
        private bool _isSystem;
        private bool _isActive;
        private bool _isUnallocated;

        public int DiskIndex
        {
            get => _diskIndex;
            set => SetProperty(ref _diskIndex, value);
        }

        public uint PartitionNumber
        {
            get => _partitionNumber;
            set => SetProperty(ref _partitionNumber, value);
        }

        public string? DriveLetter
        {
            get => _driveLetter;
            set => SetProperty(ref _driveLetter, value);
        }

        public string VolumeLabel
        {
            get => _volumeLabel;
            set => SetProperty(ref _volumeLabel, TextSanitizer.SanitizeLabel(value));
        }

        public string FileSystem
        {
            get => _fileSystem;
            set => SetProperty(ref _fileSystem, value);
        }

        public long StartingOffset
        {
            get => _startingOffset;
            set => SetProperty(ref _startingOffset, value);
        }

        public long SizeBytes
        {
            get => _sizeBytes;
            set => SetProperty(ref _sizeBytes, value);
        }

        public string FormattedSize
        {
            get => FormatHelper.FormatDiskSize(SizeBytes);
            private set { }
        }

        private long _freeSizeBytes = -1;
        public long FreeSizeBytes
        {
            get => _freeSizeBytes;
            set => SetProperty(ref _freeSizeBytes, value);
        }

        public string FormattedFreeSize
        {
            get => FreeSizeBytes >= 0 ? FormatHelper.FormatDiskSize(FreeSizeBytes) : string.Empty;
            private set { }
        }

        public string FormattedUsedSize
        {
            get => (SizeBytes > 0 && FreeSizeBytes >= 0 && SizeBytes >= FreeSizeBytes)
                ? FormatHelper.FormatDiskSize(SizeBytes - FreeSizeBytes)
                : string.Empty;
            private set { }
        }

        public string FreeSpaceSummary
        {
            get => FreeSizeBytes >= 0
                ? $" | Free: {FormattedFreeSize} (Used: {FormattedUsedSize})"
                : string.Empty;
            private set { }
        }

        public string PartitionStyle
        {
            get => _partitionStyle;
            set => SetProperty(ref _partitionStyle, value);
        }

        public Guid GptTypeGuid
        {
            get => _gptTypeGuid;
            set => SetProperty(ref _gptTypeGuid, value);
        }

        public byte MbrType
        {
            get => _mbrType;
            set => SetProperty(ref _mbrType, value);
        }

        public string PartitionTypeFriendlyName
        {
            get => _partitionTypeFriendlyName;
            set => SetProperty(ref _partitionTypeFriendlyName, value);
        }

        public bool IsBoot
        {
            get => _isBoot;
            set => SetProperty(ref _isBoot, value);
        }

        public bool IsSystem
        {
            get => _isSystem;
            set => SetProperty(ref _isSystem, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool IsUnallocated
        {
            get => _isUnallocated;
            set => SetProperty(ref _isUnallocated, value);
        }

        private string _gptPartitionName = string.Empty;
        public string GptPartitionName
        {
            get => _gptPartitionName;
            set => SetProperty(ref _gptPartitionName, TextSanitizer.SanitizeLabel(value));
        }

        public string RoleBadge
        {
            get
            {
                if (IsUnallocated) return "[Free Space]";
                if (IsSystem) return !string.IsNullOrWhiteSpace(DriveLetter) ? $"[System - {DriveLetter}\\]" : "[System]";
                if (IsBoot) return "[Boot ESP]";
                if (GptTypeGuid == GptPartitionTypes.WindowsRecovery || MbrType == 0x27) return "[Recovery]";
                if (GptTypeGuid == GptPartitionTypes.MicrosoftReserved) return "[MSR]";
                if (IsActive) return "[Active]";
                if (PartitionStyle == "MBR" && PartitionNumber > 4) return "[Logical]";
                if (MbrType == 0x05 || MbrType == 0x0F) return "[Extended]";
                return "[Primary]";
            }
            private set { }
        }

        public string DisplayName
        {
            get
            {
                if (IsUnallocated)
                {
                    return $"Unallocated Space: {FormattedSize} (Free Space)";
                }

                string letterStr = !string.IsNullOrWhiteSpace(DriveLetter) ? $", Volume {DriveLetter}" : string.Empty;
                string labelStr = !string.IsNullOrWhiteSpace(VolumeLabel) ? $", Label {VolumeLabel}" : string.Empty;
                string fsStr = !string.IsNullOrWhiteSpace(FileSystem) ? $", {FileSystem}" : string.Empty;
                string freeStr = FreeSizeBytes >= 0 ? $", {FormattedFreeSize} free" : string.Empty;

                return $"Partition {PartitionNumber}: {PartitionTypeFriendlyName}{labelStr} ({FormattedSize}{fsStr}{letterStr}{freeStr}) {RoleBadge}";
            }
            private set { }
        }

        public string SpeechDescription
        {
            get
            {
                if (IsUnallocated)
                {
                    return $"Unallocated Space, {FormattedSize} free space";
                }

                string letterStr = !string.IsNullOrWhiteSpace(DriveLetter) ? $", Drive {DriveLetter}" : string.Empty;
                string labelStr = !string.IsNullOrWhiteSpace(VolumeLabel) ? $", Label {VolumeLabel}" : string.Empty;
                string fsStr = !string.IsNullOrWhiteSpace(FileSystem) ? $", File System {FileSystem}" : string.Empty;
                string freeSpeech = FreeSizeBytes >= 0 ? $", {FormattedFreeSize} free" : string.Empty;
                string role = string.Empty;
                if (IsSystem)
                {
                    role = ", Windows Operating System volume";
                }
                else if (IsActive && !PartitionTypeFriendlyName.Contains("Active", StringComparison.OrdinalIgnoreCase))
                {
                    role = ", Active partition";
                }
                else if (PartitionStyle == "MBR")
                {
                    role = PartitionNumber > 4 ? ", Logical partition" : ", Primary partition";
                }

                return $"Partition {PartitionNumber}, {PartitionTypeFriendlyName}{labelStr}, {FormattedSize}{fsStr}{letterStr}{freeSpeech}{role}";
            }
            private set { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(SpeechDescription));
            OnPropertyChanged(nameof(FormattedSize));
            OnPropertyChanged(nameof(FormattedFreeSize));
            OnPropertyChanged(nameof(FormattedUsedSize));
            OnPropertyChanged(nameof(FreeSpaceSummary));
            OnPropertyChanged(nameof(RoleBadge));
            return true;
        }

        public void CopyFrom(PartitionItem other)
        {
            DriveLetter = other.DriveLetter;
            VolumeLabel = other.VolumeLabel;
            FileSystem = other.FileSystem;
            StartingOffset = other.StartingOffset;
            SizeBytes = other.SizeBytes;
            FreeSizeBytes = other.FreeSizeBytes;
            PartitionStyle = other.PartitionStyle;
            GptTypeGuid = other.GptTypeGuid;
            MbrType = other.MbrType;
            PartitionTypeFriendlyName = other.PartitionTypeFriendlyName;
            IsBoot = other.IsBoot;
            IsSystem = other.IsSystem;
            IsActive = other.IsActive;
            IsUnallocated = other.IsUnallocated;
            GptPartitionName = other.GptPartitionName;
        }

        public override string ToString() => SpeechDescription;
    }
}
