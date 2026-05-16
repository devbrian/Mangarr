using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 (D-05) — Negative-validation fixture for the DownloadClient
/// vertical (+2 of +3 D-05 negatives; +1 shipped in 20-04 Indexer, +1 lands in
/// 20-06 Notification).
///
/// Saves a DownloadClient with priority out of range (priority schema attribute on
/// EditDownloadClientModalContent.tsx:186 caps at max=50; the DownloadClient
/// SharedValidator enforces 1..50). The fixture is deterministic: it either
/// gets a 400 from the backend (server-side validation surfaced) OR client-side
/// validation blocks the POST and renders a per-field error. EITHER state is
/// a valid PASS — both prove validation surfaced. Same shape as Plan 20-04's
/// IndexerNegativeValidationFixture.
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DownloadClientNegativeValidationFixture : AutomationTest
{
    [Test]
    public async Task save_with_priority_out_of_range_surfaces_validation_error()
    {
        await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "downloadclient", "inprocessimage");

        var modal = new EditDownloadClientModal(Page);
        await modal.NameInput.FillAsync("OutOfRange (test)");

        // debug-30 iter-2 (2026-05-16): Priority is rendered inside an
        // isAdvanced={true} FormGroup. Toggle AdvancedSettings before fill.
        await modal.AdvancedToggle.ClickAsync();
        await Assertions.Expect(modal.PriorityInput).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await modal.PriorityInput.FillAsync("999");

        // Race a POST response against client-side blocking. Either outcome
        // proves validation surfaced.
        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient") && r.Request.Method == "POST",
            new() { Timeout = 8_000 });

        await modal.SaveButton.ClickAsync();

        try
        {
            var resp = await postTask;

            // Server-side validation path: backend returned 400 (FluentValidation
            // priority range failure). Modal stays open with error state.
            resp.Status.Should().Be(400);
        }
        catch (PlaywrightException)
        {
            // Client-side validation path: the POST never fired (HTML5 max
            // attribute on the priority input OR React form-level guard).
            // Assert that the modal remains open and a per-field / form-level
            // error surfaced.
            await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 5_000 });

            var validationFailures = modal.ModalRoot.Locator(
                ".validation-error, [role='alert'], [class*='hasError'], [class*='validationFailures']");
            var errorCount = await validationFailures.CountAsync();
            errorCount.Should().BeGreaterThan(
                0,
                "Validation must surface either as per-field error (client-side) or backend 400 (server-side)");
        }
    }
}
