using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — INVENTORY modal-action row EditDownloadClientModal.
/// Opens the baseline InProcess DownloadClient ("InProcess (test seed)" seeded
/// by SeedBaselineAsync at TestKit L93-99) via its card, changes priority, and
/// asserts the PUT returns 2xx. Blocker #4 path (b) — render is deterministic
/// off the AutomationTest base seed; NO Inconclusive branch.
///
/// Tier (D-04): modal-action axis = Nightly.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class EditDownloadClientModalFixture : AutomationTest
{
    [Test]
    public async Task edit_persists()
    {
        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        var card = page.CardByName("InProcess (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditDownloadClientModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();
        await modal.PriorityInput.FillAsync("3");

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await modal.SaveButton.ClickAsync();
        var resp = await putTask;
        resp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(modal.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }
}
