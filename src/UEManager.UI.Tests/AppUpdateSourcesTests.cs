using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

public class AppUpdateSourcesTests
{
    [Fact]
    public void The_update_check_reads_cloudflare_first_and_github_last()
    {
        var urls = AppInfo.LatestReleaseUrls;

        Assert.Equal(3, urls.Count);
        Assert.Equal("https://metadata.openmods.nexus/uemodmanager/app/latest.json", urls[0]);
        Assert.Equal("https://uemm-metadata.pages.dev/uemodmanager/app/latest.json", urls[1]);
        Assert.Equal(AppInfo.LatestReleaseUrl, urls[^1]);
    }
}
