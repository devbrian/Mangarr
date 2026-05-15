using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — DownloadClient schema picker coverage (INVENTORY
/// v5-endpoint row: GET /api/v5/downloadclient/schema).
///
/// Opens /settings/downloadclients, clicks the empty add card, asserts the
/// picker modal renders, GET /api/v5/downloadclient/schema completed 200, and
/// the InProcess schema card is visible (D-06 canonical DownloadClient pick).
///
/// Tier (D-04): GET-axis = PR-smoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class DownloadClientAddModalSchemaFixture : AutomationTest
{
    [Test]
    public async Task schema_dropdown_renders()
    {
        var settings = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        var schemaTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/schema") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await Page.GetByTestId("settings-downloadclient-add-card").ClickAsync();
        var resp = await schemaTask;
        resp.Status.Should().Be(200);

        var picker = new AddDownloadClientModal(Page);
        await Assertions.Expect(picker.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(picker.InProcessCard).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
