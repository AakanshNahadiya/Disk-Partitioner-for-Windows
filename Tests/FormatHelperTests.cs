using DiskPartitioner.Helpers;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class FormatHelperTests
    {
        [Fact]
        public void FormatBytes_FormatsZeroAndNegativeCorrectly()
        {
            Assert.Equal("0 KB", FormatHelper.FormatBytes(0));
            Assert.Equal("0 KB", FormatHelper.FormatBytes(-100));
        }

        [Fact]
        public void FormatBytes_FormatsKbMbGbTb()
        {
            Assert.Equal("500 KB", FormatHelper.FormatBytes(500 * 1024));
            Assert.Equal("100.0 MB", FormatHelper.FormatBytes(100 * 1024 * 1024));
            Assert.Equal("5.50 GB", FormatHelper.FormatBytes((long)(5.5 * 1024 * 1024 * 1024)));
            Assert.Equal("2.00 TB", FormatHelper.FormatBytes(2L * 1024 * 1024 * 1024 * 1024));
        }

        [Fact]
        public void FormatDiskSize_FormatsAccurately()
        {
            Assert.Equal("0.00 GB", FormatHelper.FormatDiskSize(0));
            Assert.Equal("512.0 MB", FormatHelper.FormatDiskSize(512 * 1024 * 1024));
            Assert.Equal("100.00 GB", FormatHelper.FormatDiskSize(100L * 1024 * 1024 * 1024));
            Assert.Equal("2 TB", FormatHelper.FormatDiskSize(2L * 1024 * 1024 * 1024 * 1024));
        }

        [Fact]
        public void FormatHardwareCapacity_FormatsDecimalCapacity()
        {
            Assert.Equal("512 GB Hardware", FormatHelper.FormatHardwareCapacity(512_000_000_000L));
            Assert.Equal("1 TB Hardware", FormatHelper.FormatHardwareCapacity(1_000_000_000_000L));
            Assert.Equal(string.Empty, FormatHelper.FormatHardwareCapacity(0));
        }
    }
}
