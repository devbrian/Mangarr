using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 (D-06) / Phase 39 Plan 39-07 — DownloadClient canonical CRUD
/// round-trip against the gateway client: picker -> Add -> Edit (priority) -> Delete.
/// Exercises POST/PUT/DELETE /api/v5/downloadclient plus the SettingsProviderFlow
/// orchestrator (reused verbatim from Plan 20-04).
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
///
/// The AutomationTest base seeds a "Gateway" baseline client (SeedBaselineAsync). This
/// test creates a SECOND gateway client with a distinct name to drive the round-trip
/// without colliding with the baseline row. Repointed from the retired in-process
/// client (Plan 39-02) — GatewayDownloadClient is the sole download client and the
/// `inprocessimage` schema slug no longer exists in GET /api/v5/downloadclient/schema.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DownloadClientCrudFixture : AutomationTest
{
    private const string TestName = "Gateway (CRUD test)";

    [Test]
    public async Task crud_roundtrip_for_gateway()
    {
        await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "downloadclient", "gateway");

        var editModal = new EditDownloadClientModal(Page);
        await editModal.NameInput.FillAsync(TestName);

        await SettingsProviderFlow.SaveAsync(Page, "downloadclient");

        // Reopen the saved card, change priority, save again -> PUT.
        var settings = new SettingsDownloadClientsPage(Page);
        var card = settings.CardByName(TestName);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await card.ClickAsync();
        var editAgain = new EditDownloadClientModal(Page);
        await Assertions.Expect(editAgain.ModalRoot).ToBeVisibleAsync();

        // debug-30 iter-2 (2026-05-16): Client Priority lives inside an
        // isAdvanced={true} FormGroup. Toggle AdvancedSettings before fill.
        await editAgain.AdvancedToggle.ClickAsync();
        await Assertions.Expect(editAgain.PriorityInput).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await editAgain.PriorityInput.FillAsync("10");

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await editAgain.SaveButton.ClickAsync();
        var putResp = await putTask;
        putResp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(editAgain.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Delete via the flow helper.
        await SettingsProviderFlow.DeleteByNameAsync(Page, "downloadclient", TestName);
    }
}
