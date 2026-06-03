using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 — Test-All button coverage (INVENTORY v5-endpoint row:
/// POST /api/v5/indexer/testall). Plan 20-01 LiveService Enumeration row #2
/// (GH #164); status flips provisional -> confirmed by this fixture's
/// existence in-tree.
///
/// Uses TestKit.SeedIndexerAsync to guarantee at least one indexer is present
/// (replaces the prior Inconclusive("Test All button not present") branch
/// per Blocker #4).
///
/// LiveService: the testall command iterates every enabled indexer's
/// IndexerService.Test() which hits the external gateway. Offline shape
/// coverage lives in IndexerTestAllOfflineFixture.
///
/// Phase 39 Plan 39-07: the seeded indexer is now the GatewayIndexer (the sole
/// IIndexer after the in-process site-scraper indexers were retired in Plan 39-03).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
public class IndexerTestAllFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedIndexerAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task testall_button()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        // Strict state assertion (no Inconclusive fallback): the seeded gateway
        // indexer guarantees the toolbar button is rendered.
        await Assertions.Expect(settings.TestAllButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/indexer/testall") && r.Request.Method == "POST",
            new() { Timeout = 60_000 });
        await settings.TestAllButton.ClickAsync();
        var resp = await postTask;
        resp.Status.Should().BeInRange(200, 499);
    }
}
