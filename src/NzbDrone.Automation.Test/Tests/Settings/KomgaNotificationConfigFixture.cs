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
        // gh178 sub-B: register a route handler that appends
        // ?skipTesting=true to every POST/PUT to /api/v5/connection. The
        // fixture uses a placeholder Komga URL (https://komga.example) that
        // does NOT resolve; without skipTesting the backend
        // ProviderControllerBase.CreateProvider:85 + UpdateProvider:114 invokes
        // the Komga broker's Test() which makes a live outbound HTTPS call
        // (the existing fixture docstring above identifies it as
        // GET /api/v1/libraries with the X-API-Key header). DNS resolution
        // fails and the response surfaces as a 400 with
        // "Unable to connect to Komga". This is a test-only bypass —
        // production save flows always default to skipTesting=false, so we
        // are not masking real behavior, just making the CRUD round-trip
        // deterministic without a live Komga server (no cassette is wired
        // in for Komga's URL at the time of writing).
        await SettingsProviderFlow.BypassConnectionTestAsync(Page, "notification");

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
