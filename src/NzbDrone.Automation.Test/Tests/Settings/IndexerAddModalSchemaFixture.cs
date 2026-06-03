using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 — Indexer schema picker coverage (INVENTORY v5-endpoint
/// row: GET /api/v5/indexer/schema).
///
/// Opens /settings/indexers, clicks the empty add card, asserts the picker
/// modal renders, GET /api/v5/indexer/schema completed 200, and the gateway
/// schema card is visible (the sole IIndexer pick). Phase 39 Plan 39-07:
/// repointed from the retired in-process MangaDex schema card to the gateway
/// (testid add-indexer-gateway) — the in-process site-scraper indexers were
/// retired in Plan 39-03.
///
/// Tier (D-04): GET-axis = PR-smoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerAddModalSchemaFixture : AutomationTest
{
    [Test]
    public async Task schema_dropdown_renders()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        var schemaTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/indexer/schema") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await Page.GetByTestId("settings-indexer-add-card").ClickAsync();
        var resp = await schemaTask;
        resp.Status.Should().Be(200);

        var picker = new AddIndexerModal(Page);
        await Assertions.Expect(picker.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(picker.GatewayCard).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
