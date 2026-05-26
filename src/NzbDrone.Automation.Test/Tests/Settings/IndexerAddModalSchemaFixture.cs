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
/// modal renders, GET /api/v5/indexer/schema completed 200, and the MangaDex
/// schema card is visible (D-06 canonical Indexer pick). Comix is disabled in
/// OneTimeSetUp (Pitfall 10) so the picker enumeration of the schema endpoint
/// does NOT warm PuppeteerSharp.
///
/// Tier (D-04): GET-axis = PR-smoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerAddModalSchemaFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

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
        await Assertions.Expect(picker.MangaDexCard).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
