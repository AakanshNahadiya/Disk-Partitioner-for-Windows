using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskPartitioner.Helpers;

namespace DiskPartitioner.Models
{
    public class PhysicalDiskItem : INotifyPropertyChanged
    {
        private int _diskIndex;
        private string _model = string.Empty;
        private string _busType = "Unknown";
        private string _mediaType = "Unknown";
        private long _totalSizeBytes;
        private string _partitionStyle = "RAW";
        private bool _isSystemDrive;
        private bool _isBootDrive;
        private bool _isReadOnly;
        private bool _isOffline;
        private int _partitionsCount;

        public int DiskIndex
        {
            get => _diskIndex;
            set => SetProperty(ref _diskIndex, value);
        }

        public string DevicePath => $@"\\.\PhysicalDrive{DiskIndex}";

        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, TextSanitizer.SanitizeLabel(value));
        }

        public string BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, TextSanitizer.SanitizeLabel(value));
        }

        public string MediaType
        {
            get => _mediaType;
            set => SetProperty(ref _mediaType, value);
        }

        public long TotalSizeBytes
        {
            get => _totalSizeBytes;
            set => SetProperty(ref _totalSizeBytes, value);
        }

        public string FormattedSize
        {
            get => FormatHelper.FormatHardwareCapacity(TotalSizeBytes);
            private set { }
        }

        public string FormattedDiskSize
        {
            get => FormatHelper.FormatDiskSize(TotalSizeBytes);
            private set { }
        }

        public string PartitionStyle
        {
            get => _partitionStyle;
            set => SetProperty(ref _partitionStyle, value);
        }

        public bool IsSystemDrive
        {
            get => _isSystemDrive;
            set => SetProperty(ref _isSystemDrive, value);
        }

        public bool IsBootDrive
        {
            get => _isBootDrive;
            set => SetProperty(ref _isBootDrive, value);
        }

        public bool IsProtectedOsDrive => IsSystemDrive || IsBootDrive;

        public bool IsReadOnly
        {
            get => _isReadOnly;
            set => SetProperty(ref _isReadOnly, value);
        }

        public bool IsOffline
        {
            get => _isOffline;
            set => SetProperty(ref _isOffline, value);
        }

        public int PartitionsCount
        {
            get => _partitionsCount;
            set => SetProperty(ref _partitionsCount, value);
        }

        public string ProtectionBadge
        {
            get
            {
                if (IsProtectedOsDrive) return "[Windows OS Drive - Protected]";
                if (IsReadOnly) return "[Read-Only]";
                if (IsOffline) return "[Offline]";
                if (BusType.Equals("USB", StringComparison.OrdinalIgnoreCase)) return "[Removable USB]";
                return "[Online]";
            }
            private set { }
        }

        public string DisplayName
        {
            get => $"Disk {DiskIndex}: {Model} ({FormattedSize}, {BusType} {MediaType}, {PartitionStyle}) {ProtectionBadge}";
            private set { }
        }

        public string SpeechDescription
        {
            get
            {
                string status = IsProtectedOsDrive ? "Windows Operating System Drive, Protected against destructive changes" : (IsOffline ? "Offline" : "Online");
                return $"Disk {DiskIndex}, {Model}, {FormattedSize}, {BusType} {MediaType}, {PartitionStyle} partition table, {PartitionsCount} partitions, {status}";
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
            OnPropertyChanged(nameof(FormattedDiskSize));
            OnPropertyChanged(nameof(ProtectionBadge));
            OnPropertyChanged(nameof(IsProtectedOsDrive));
            return true;
        }

        public void CopyFrom(PhysicalDiskItem other)
        {
            Model = other.Model;
            BusType = other.BusType;
            MediaType = other.MediaType;
            TotalSizeBytes = other.TotalSizeBytes;
            PartitionStyle = other.PartitionStyle;
            IsSystemDrive = other.IsSystemDrive;
            IsBootDrive = other.IsBootDrive;
            IsReadOnly = other.IsReadOnly;
            IsOffline = other.IsOffline;
            PartitionsCount = other.PartitionsCount;
        }

        public override string ToString() => SpeechDescription;
    }
}
