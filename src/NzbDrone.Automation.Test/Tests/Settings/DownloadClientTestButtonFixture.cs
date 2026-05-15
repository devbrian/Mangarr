using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — DownloadClient Test-button coverage (INVENTORY
/// v5-endpoint row: POST /api/v5/downloadclient/test).
///
/// W#2 (Plan 20-05) — verified at planning time that
/// InProcessImageDownloadClient.Test() (src/NzbDrone.Core/Download/Clients/InProcess/
/// InProcessImageDownloadClient.cs L129-156) has ZERO HttpClient/Get(/Post( calls
/// in its body. The Test() method only exercises DownloadScratchPath provisioning
/// via IDiskProvider — a local-only operation. The DownloadClient/test endpoint
/// is therefore cassette-feasible (and cassette-free here since there is no
/// upstream HTTP at all). Tag: AutomationTest (Nightly), NOT LiveService.
///
/// The AutomationTest base seeds the baseline "InProcess (test seed)" client
/// via SeedBaselineAsync, so the card render is deterministic without extra
/// seeding (Blocker #4 path (b) — replaces the previous "Baseline InProcess not
/// present" Inconclusive branch).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DownloadClientTestButtonFixture : AutomationTest
{
    [Test]
    public async Task test_button()
    {
        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        var card = page.CardByName("InProcess (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditDownloadClientModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/test") && r.Request.Method == "POST",
            new() { Timeout = 30_000 });
        await modal.TestButton.ClickAsync();
        var resp = await postTask;
        resp.Status.Should().BeOneOf(200, 204, 400);
    }
}
