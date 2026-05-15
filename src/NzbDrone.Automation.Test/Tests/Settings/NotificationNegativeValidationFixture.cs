using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-06 (D-05) — Negative-validation fixture for the
/// Notification vertical (+3 of +3 D-05 negatives — COMPLETES the
/// negative-validation set across Indexer/DownloadClient/Notification).
///
/// Saves a Komga notification with an invalid URL (KomgaNotificationSettings
/// requires `RuleFor(c => c.Url).ValidRootUrl()`; the SharedValidator rejects
/// values that don't parse as an absolute http/https URL). The fixture is
/// deterministic: either the backend returns 400 (server-side FluentValidation
/// surfaced via ValidRootUrl extension) OR client-side validation blocks the
/// POST and a per-field/form-level error renders. EITHER state is a valid PASS
/// — both prove validation surfaced.
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
/// Mirrors Plan 20-04 IndexerNegativeValidationFixture + Plan 20-05
/// DownloadClientNegativeValidationFixture race-and-assert shape verbatim.
///
/// Blocker #4 invariant: NO Assert.Inconclusive — both branches assert
/// real state (response 400 OR positive count of validation-error elements).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NotificationNegativeValidationFixture : AutomationTest
{
    [Test]
    public async Task save_with_invalid_url_surfaces_validation_error()
    {
        await new SettingsNotificationsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "notification", "komga");

        var modal = new EditNotificationModal(Page);
        await modal.NameInput.FillAsync("InvalidUrl (test)");

        // KomgaNotificationSettingsValidator.Url has ValidRootUrl() — "not-a-url"
        // fails on the absolute-http(s) parse.
        await modal.UrlInput.FillAsync("not-a-url");
        await modal.ApiKeyInput.FillAsync("test-api-key");
        await modal.LibraryIdInput.FillAsync("1");

        // Race a POST response against client-side blocking. Either outcome
        // proves validation surfaced.
        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/notification") && r.Request.Method == "POST",
            new() { Timeout = 8_000 });

        await modal.SaveButton.ClickAsync();

        try
        {
            var resp = await postTask;

            // Server-side validation path: backend returned 400 (FluentValidation
            // ValidRootUrl failure). Modal stays open with error state.
            resp.Status.Should().Be(400);
        }
        catch (PlaywrightException)
        {
            // Client-side validation path: the POST never fired (HTML5
            // attribute or React form-level guard). Assert that the modal
            // remains open and a per-field / form-level error surfaced.
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
