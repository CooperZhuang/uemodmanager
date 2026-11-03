using System.IO;
using UEManager.Core.Games;
using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

public class HelpReportTests
{
    [Fact]
    public void The_report_names_where_the_remote_data_and_the_update_check_came_from_but_no_address()
    {
        var facts = new HelpFacts(GameProfile.BuiltIn().First(), null, null, null, [], [], null,
            Path.Combine(Path.GetTempPath(), "uemm-no-logs-" + Guid.NewGuid().ToString("N")),
            RemoteData: "Cloudflare", UpdateCheck: "Cloudflare, up to date (newest 1.5.2)");

        var report = HelpReport.Collect(facts);

        Assert.Contains("Remote data: Cloudflare; update check: Cloudflare, up to date (newest 1.5.2)", report);
        Assert.DoesNotContain("openmods.nexus", report);
        Assert.DoesNotContain("pages.dev", report);
    }
}
