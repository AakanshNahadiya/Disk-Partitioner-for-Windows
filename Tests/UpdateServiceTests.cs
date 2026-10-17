using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DiskPartitioner.Services;
using Xunit;

namespace DiskPartitioner.Tests
{
    public class UpdateServiceTests
    {
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _responseContent;

            public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
            {
                _statusCode = statusCode;
                _responseContent = responseContent;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task CheckForUpdatesAsync_Handles404GracefullyAsUpToDate()
        {
            var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
            var client = new HttpClient(handler);
            var service = new UpdateService(client);

            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.IsSuccess);
            Assert.False(result.HasUpdate);
            Assert.Contains("up to date", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_DetectsNewerVersionAndDownloadUrl()
        {
            string json = """
            {
                "tag_name": "v1.1.0",
                "name": "Disk Partitioner v1.1.0",
                "body": "- Added automated update checks\n- Improved screen reader labels",
                "html_url": "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v1.1.0",
                "assets": [
                    {
                        "name": "DiskPartitioner_Setup_v1.1.0.exe",
                        "browser_download_url": "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/download/v1.1.0/DiskPartitioner_Setup_v1.1.0.exe"
                    }
                ]
            }
            """;

            var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
            var client = new HttpClient(handler);
            var service = new UpdateService(client);

            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.IsSuccess);
            Assert.True(result.HasUpdate);
            Assert.Equal("1.1.0", result.LatestVersion);
            Assert.Equal("Disk Partitioner v1.1.0", result.ReleaseName);
            Assert.Contains("automated update checks", result.ReleaseNotes);
            Assert.NotNull(result.DownloadUrl);
            Assert.EndsWith(".exe", result.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_ReportsSameVersionAsUpToDate()
        {
            string currentVer = "1.0.0";
            string json = $$"""
            {
                "tag_name": "v{{currentVer}}",
                "name": "Disk Partitioner v{{currentVer}}",
                "body": "Initial release",
                "html_url": "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v1.0.0"
            }
            """;

            var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
            var client = new HttpClient(handler);
            var service = new UpdateService(client);

            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.IsSuccess);
            Assert.False(result.HasUpdate);
            Assert.Contains("up to date", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AutoCheckSetting_CanBeToggled()
        {
            var service = new UpdateService();
            bool initial = service.IsAutoCheckEnabled();

            service.SetAutoCheckEnabled(!initial);
            Assert.Equal(!initial, service.IsAutoCheckEnabled());

            // Restore initial setting
            service.SetAutoCheckEnabled(initial);
        }

        [Fact]
        public void VersionNotificationTracking_RecordsAndChecksProperly()
        {
            var service = new UpdateService();
            string testVer = $"test-{Guid.NewGuid():N}";

            Assert.False(service.HasNotifiedForVersion(testVer));
            service.RecordVersionNotified(testVer);
            Assert.True(service.HasNotifiedForVersion(testVer));
        }

        [Fact]
        public void UpdateInfo_GetEffectiveReleaseUrl_BuildsValidTagUrl()
        {
            var info = new UpdateInfo
            {
                CurrentVersion = "1.0.0",
                LatestVersion = "1.0.0",
                HtmlUrl = "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v1.0.0"
            };

            Assert.Equal("https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v1.0.0", info.GetEffectiveReleaseUrl());

            var defaultInfo = new UpdateInfo
            {
                CurrentVersion = "1.0.0",
                LatestVersion = "1.0.0",
                HtmlUrl = "https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows"
            };

            Assert.Equal("https://github.com/AakanshNahadiya/Disk-Partitioner-for-Windows/releases/tag/v1.0.0", defaultInfo.GetEffectiveReleaseUrl());
        }
    }
}
