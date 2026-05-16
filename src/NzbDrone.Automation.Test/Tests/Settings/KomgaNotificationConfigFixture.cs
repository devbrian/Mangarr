using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-06 (D-06 canonical Notification pick) — Komga CRUD
/// round-trip: picker -> Add -> Edit -> Delete. Exercises
/// POST/PUT/DELETE /api/v5/notification through SettingsProviderFlow
/// (reused verbatim from Plan 20-04 with vertical="notification").
///
/// Greens INVENTORY req-axis row NOTIFY-01 (User can configure Komga as
/// Notification target).
///
/// LiveService Enumeration row #4 ATTEMPT outcome (D-09a, traced in
/// SUMMARY): Komga's `Test()` calls `GET /api/v1/libraries` with the
/// `X-API-Key` header; URL is fully user-supplied (no nonces / no
/// signed envelopes / no fingerprinting). Plan 20-06 SHIPS a cassette
/// at Fixtures/Cassettes/Komga/f1c6ef1a0c71e2f8.json (SHA1(GET|url)
/// content-address per CassetteHandler.cs) → row #4 status flipped
/// `provisional` → `cassette-replayed` (Outcome A).
///
/// **This fixture is default-offline (NO [Category("LiveService")]):**
/// the CRUD round-trip (form Save / Edit / Delete) does NOT call Komga
/// itself — `POST /api/v5/notification` validates settings server-side
/// via `KomgaNotificationSettingsValidator` (URL.ValidRootUrl + ApiKey
/// not-empty + LibraryId > 0); only the Test BUTTON triggers
/// `_proxy.GetLibraries(...)` which is the cassette-replayed path.
/// The cassette satisfies seeds/liveservice-coverage-policy.md §3
/// (form-render + button-wiring + request-shape) intrinsically — no
/// separate KomgaNotificationOfflineFixture.cs is needed (Outcome A
/// invariant).
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class KomgaNotificationConfigFixture : AutomationTest
{
    private const string TestName = "Komga (CRUD test)";

    [Test]
    public async Task komga_form_roundtrip()
    {
        await new SettingsNotificationsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "notification", "komga");

        var modal = new EditNotificationModal(Page);
        await modal.NameInput.FillAsync(TestName);
        await modal.UrlInput.FillAsync("https://komga.example");
        await modal.ApiKeyInput.FillAsync("test-api-key");
        await modal.LibraryIdInput.FillAsync("1");

        await SettingsProviderFlow.SaveAsync(Page, "notification");

        // Reopen the saved card, edit Name, save again -> PUT.
        var page = new SettingsNotificationsPage(Page);
        var card = page.CardByName(TestName);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await card.ClickAsync();
        var editAgain = new EditNotificationModal(Page);
        await Assertions.Expect(editAgain.ModalRoot).ToBeVisibleAsync();

        var editedName = TestName + " (edited)";
        await editAgain.NameInput.FillAsync(editedName);

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/notification/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await editAgain.SaveButton.ClickAsync();
        var putResp = await putTask;
        putResp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(editAgain.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Delete via the flow helper (uses the edited slug).
        await SettingsProviderFlow.DeleteByNameAsync(Page, "notification", editedName);
    }
}
