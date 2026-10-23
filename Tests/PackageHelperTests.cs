using System;
using DiskPartitioner.Helpers;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class PackageHelperTests
    {
        [Fact]
        public void IsStorePackage_ExecutesSafelyAndReturnsFalseWhenUnpackaged()
        {
            // Unit tests execute inside the dotnet test host process which is not an MSIX container.
            bool isStore = PackageHelper.IsStorePackage();
            Assert.False(isStore);
        }
    }
}
