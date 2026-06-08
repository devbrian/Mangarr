using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 / Phase 39 Plan 39-07 — INVENTORY modal-action row
/// EditDownloadClientModal. Opens the baseline gateway DownloadClient
/// ("Gateway (test seed)" seeded by SeedBaselineAsync) via its card, changes
/// priority, and asserts the PUT returns 2xx. Blocker #4 path (b) — render is
/// deterministic off the AutomationTest base seed; NO Inconclusive branch.
/// Repointed from the retired in-process client (Plan 39-02) — this fixture
/// CRUD-exercises a download client without asserting its implementation, so the
/// gateway client satisfies it.
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
        // This fixture verifies edit PERSISTENCE, not connection validity. The gateway
        // DownloadClient's save-time Test() probes the gateway's /status and validates that
        // its advertised output folder (e.g. /data/manga) is locally reachable — true in the
        // docker/CI environment where the gateway mount exists, but not in a bare offline
        // test run, where it would 400 the PUT. Inject ?skipTesting=true so the edit is
        // persisted without the live output-folder probe (mirrors the negative-validation +
        // CRUD fixtures, which already bypass).
        await SettingsProviderFlow.BypassConnectionTestAsync(Page, "downloadclient");

        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        var card = page.CardByName("Gateway (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditDownloadClientModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // debug-30 iter-2 (2026-05-16): Client Priority lives inside an
        // isAdvanced={true} FormGroup. Toggle AdvancedSettings before fill.
        await modal.AdvancedToggle.ClickAsync();
        await Assertions.Expect(modal.PriorityInput).ToBeVisibleAsync(new() { Timeout = 5_000 });
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
