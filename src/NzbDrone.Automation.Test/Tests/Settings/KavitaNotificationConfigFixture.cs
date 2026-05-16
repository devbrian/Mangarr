using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-06 (D-06 same-family alternative Notification pick) —
/// Kavita CRUD round-trip: picker -> Add -> Edit -> Delete. Exercises
/// POST/PUT/DELETE /api/v5/notification through SettingsProviderFlow
/// (reused verbatim from Plan 20-04 with vertical="notification").
///
/// Greens INVENTORY req-axis row NOTIFY-02 (User can configure Kavita as
/// Notification target).
///
/// Kavita is the same-family alternative to Komga (Phase 6 D-16). Settings
/// shape: URL (required, ValidRootUrl) + ApiKey (required, NotEmpty) +
/// LibraryId (OPTIONAL — D-16 — Kavita has scan-all endpoint, unlike Komga
/// which has Pitfall 2 REQUIRED LibraryId).
///
/// LiveService-tier note: Kavita's `Test()` follows the two-step JWT flow
/// (POST /api/Plugin/authenticate?apiKey=...pluginName=Mangarr → JWT;
/// subsequent calls Bearer-authenticated). The CRUD round-trip below does
/// NOT call Test — POST/PUT/DELETE /api/v5/notification validates settings
/// server-side via KavitaNotificationSettingsValidator without calling
/// Kavita. The Test BUTTON path (which DOES traverse the JWT flow) is
/// covered by the same Plan 20-01 LiveService Enumeration row #4 family;
/// Kavita's JWT response shape is content-cacheable like Komga's (URL+key
/// in query string, deterministic; same Outcome A path). No separate row
/// added (D-10 invariant: row #4 covers the Notification Test surface
/// collectively; Kavita does not surface a NEW LiveService candidate).
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class KavitaNotificationConfigFixture : AutomationTest
{
    private const string TestName = "Kavita (CRUD test)";

    [Test]
    public async Task kavita_form_roundtrip()
    {
        // gh178 sub-B: register a route handler that appends
        // ?skipTesting=true to every POST/PUT to /api/v5/connection. The
        // fixture uses a placeholder Kavita URL (https://kavita.example)
        // that does NOT resolve; without skipTesting the backend
        // ProviderControllerBase.CreateProvider:85 + UpdateProvider:114 fires
        // KavitaProxy.Authenticate(...) which fails DNS and returns 400.
        // This is a test-only bypass — production save flows always pass
        // skipTesting=false (modulo the isResave UI path), so we're not
        // masking real behavior, just making the CRUD round-trip
        // deterministic without a live Kavita server.
        await SettingsProviderFlow.BypassConnectionTestAsync(Page, "notification");

        await new SettingsNotificationsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "notification", "kavita");

        var modal = new EditNotificationModal(Page);
        await modal.NameInput.FillAsync(TestName);
        await modal.UrlInput.FillAsync("https://kavita.example");
        await modal.ApiKeyInput.FillAsync("test-api-key");

        // D-16: LibraryId is OPTIONAL on Kavita (scan-all endpoint exists). Leave blank
        // to exercise the scan-all configuration path.
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

        // debug-30 iter-2 (2026-05-16): notification picker/modal maps to V5
        // resource "connection" (ConnectionController + useConnections PATH).
        // The route handler registered above also appends skipTesting=true
        // to the PUT, so UpdateProvider:114 (`hasDefinitionChanged && !skipTesting`)
        // skips the broker Test() and returns 2xx cleanly.
        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/connection/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await editAgain.SaveButton.ClickAsync();
        var putResp = await putTask;
        putResp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(editAgain.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Delete via the flow helper (uses the edited slug).
        await SettingsProviderFlow.DeleteByNameAsync(Page, "notification", editedName);
    }
}
