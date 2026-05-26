using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// GH #221 — paste-back OAuth modal wiring verification.
//
// Phase 27 Plans 27-03 (AniList) + 27-04 (MAL) shipped two paste-back modals
// (`AniListPinModal.tsx`, `MalCallbackUrlModal.tsx`) with reserved data-testids
// but never wired them into `useOAuth.ts`. The single-fix PR for GH #221
// extends `useOAuth.ts` with a `completionMode` discriminant + wires the two
// modals via `OAuthInput.tsx` (keyed on `providerData.implementation`).
//
// This fixture asserts that:
//   * AniList Add picker -> Edit modal -> "Connect" button opens
//     `AniListPinModal` with the 3 reserved testids exposed
//     (`importlist-anilist-pin-input/-submit/-cancel`).
//   * MAL Add picker -> Edit modal -> "Connect" surfaces an actionable error
//     for new (un-persisted) lists (MAL's `startOAuth` requires `Definition.Id`
//     to be saved first per MalImportList.cs:109-124).
//
// Pattern κ enforcement (Phase 18 D-18): all testids use the allowed
// `add-importlist-*` / `edit-importlist-*` / `importlist-anilist-*` /
// `importlist-mal-*` prefixes; zero forbidden TV-shape selectors.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class OAuthPasteBackModalFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Pitfall 10 / cross-fixture contract: Comix indexer is un-cassetted and would
        // escape to the live network on PuppeteerSharp warm-up if its schema endpoint
        // were enumerated. Mirror the sibling AniList/MAL/MangaDex fixtures.
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task anilist_connect_button_opens_pin_modal_with_reserved_testids()
    {
        // 1. Navigate to /settings/importlists and open the Add picker.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var addCard = Page.GetByTestId("settings-importlist-add-card");
        await addCard.ClickAsync();

        var addModal = Page.GetByTestId("add-importlist-modal");
        await Assertions.Expect(addModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 2. Click the AniList tile to open the Edit modal in "add new" mode.
        var anilistTile = Page.GetByTestId("add-importlist-anilist");
        await anilistTile.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 3. Fill in the required ClientId + ClientSecret so the backend's
        //    AniListImportList.RequestAction("startOAuth") returns the pin
        //    authorize URL (the proxy.GetPinAuthorizeUrl call is deterministic
        //    and does NOT fire an HTTP request — safe under offline cassette).
        //    D-18 contract: use the `settings-{provider}-field-{name}` testid
        //    derived in ProviderFieldFormGroup.tsx:117 — `input[name=...]`
        //    selectors are banned by scripts/audit-test-assertions.sh.
        var clientIdInput = Page.GetByTestId("settings-importlist-field-clientId");
        await clientIdInput.FillAsync("42");

        var clientSecretInput = Page.GetByTestId("settings-importlist-field-clientSecret");
        await clientSecretInput.FillAsync("test-secret");

        // 4. Sanity-check the OAuthInput's wiring contract — the GH #221 fix
        //    annotates the OAuthInput container with three data attributes that
        //    expose the hook's state. Before clicking Connect, the discriminant
        //    derivation MUST already pin completionMode="paste-pin" because
        //    AniListImportListSettings carries `implementation: AniListImportList`.
        //    If this assertion fails, the OAuthInput wiring is broken — no point
        //    in proceeding to the click.
        var oauthContainer = Page.Locator(
            "[data-oauth-completion-mode='paste-pin']");
        await Assertions.Expect(oauthContainer).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 5. Click the "Connect" button (FieldType.OAuth affordance for the
        //    SignIn field at index 7 of AniListImportListSettings). GH #221's
        //    OAuthInput.tsx fix derives completionMode="paste-pin" and renders
        //    AniListPinModal once startOAuth returns the OauthUrl.
        var connectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Start OAuth" }).First;
        await Assertions.Expect(connectButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        // GH #230 — the URL is /authorize?...&redirect_uri=...pin, NOT /pin?... directly.
        // Pre-#230 tests asserted the broken contract (StartWith "/pin"); now we lock in
        // the fixed URL.
        //
        // Capture the popup's initial navigation request via Context.RouteAsync, set up
        // BEFORE the click that opens the popup. Reading newTab.Url is racy:
        //   • AniList redirects unauthenticated browsers /authorize → /login (CI is
        //     never authenticated), so the post-load URL is /login, not /authorize.
        //   • newTab.WaitForRequestAsync subscribes AFTER WaitForPopupAsync resolves,
        //     but the popup's first navigation request fires synchronously inside
        //     window.open — by the time we subscribe, the request has already left.
        //
        // Context.RouteAsync registered before the click intercepts the request as it
        // leaves the browser, regardless of timing. The handler captures the URL and
        // fulfills with a stub HTML so AniList never sees the request — keeps the test
        // hermetic (no dependency on AniList being reachable from CI) and avoids any
        // /authorize → /login redirect chain.
        string capturedAuthorizeUrl = null;
        var oauthRouteRegex = new Regex(@"anilist\.co/api/v2/oauth/(authorize|pin)");
        await Context.RouteAsync(oauthRouteRegex, async route =>
        {
            capturedAuthorizeUrl ??= route.Request.Url;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = "<html><body>stub</body></html>",
            });
        });

        try
        {
            // The Connect click triggers window.open(...) which Playwright intercepts
            // as a Popup event — register the handler BEFORE clicking to avoid a race.
            var newTabTask = Page.WaitForPopupAsync(new PageWaitForPopupOptions { Timeout = 10_000 });
            await connectButton.ClickAsync();

            var newTab = await newTabTask;
            newTab.Should().NotBeNull("the Connect button should open AniList's authorize URL in a new tab");

            // Wait for the stub HTML to land so we know Context.RouteAsync fulfilled the request.
            await newTab.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10_000 });

            capturedAuthorizeUrl.Should().NotBeNullOrWhiteSpace(
                "the popup should issue a navigation request to AniList's OAuth endpoint");
            capturedAuthorizeUrl.Should().StartWith("https://anilist.co/api/v2/oauth/authorize",
                "the popup should hit AniList's authorize endpoint, NOT /pin directly (GH #230 fix)");
            capturedAuthorizeUrl.Should().Contain("redirect_uri=https%3A%2F%2Fanilist.co%2Fapi%2Fv2%2Foauth%2Fpin",
                "the authorize call MUST carry the pin URL as redirect_uri or the pin page renders 'undefined'");

            await newTab.CloseAsync();
        }
        finally
        {
            await Context.UnrouteAsync(oauthRouteRegex);
        }

        // 6. State assertion (the GH #221 root-cause gate): after the click,
        //    the OAuthInput's pendingPaste hook state MUST flip from "none" to
        //    "paste-pin" (proves startOAuth's setOAuthValue commit fired). If
        //    this assertion fails, useOAuth.ts is NOT setting pendingPaste —
        //    that's the silent-stall failure mode the issue describes.
        var pasteState = Page.Locator(
            "[data-oauth-pending-paste='paste-pin']");
        await Assertions.Expect(pasteState).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // 7. The AniListPinModal opens in the original tab with the three
        //    reserved data-testids (Plan 27-01 reservation ledger). This is
        //    the GH #221 acceptance criterion — the modal MUST render after
        //    the new tab opens.
        var pinModal = Page.GetByTestId("importlist-anilist-pin-modal");
        await Assertions.Expect(pinModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        var pinInput = Page.GetByTestId("importlist-anilist-pin-input");
        await Assertions.Expect(pinInput).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        var pinSubmit = Page.GetByTestId("importlist-anilist-pin-submit");
        await Assertions.Expect(pinSubmit).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        var pinCancel = Page.GetByTestId("importlist-anilist-pin-cancel");
        await Assertions.Expect(pinCancel).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        // 7b. GH #221 PR #228 P1 fix gate (chatgpt-codex review): while the
        //     paste modal is open and the hook is PAUSED waiting for the user
        //     to paste, `authorizing` MUST be FALSE — otherwise the submit
        //     SpinnerErrorButton starts in a disabled spinning state and the
        //     user cannot submit. The `data-oauth-authorizing` attribute on
        //     the OAuthInput container exposes the hook state.
        var pausedState = Page.Locator(
            "[data-oauth-pending-paste='paste-pin'][data-oauth-authorizing='false']");
        await Assertions.Expect(pausedState).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 8. State assertion: the modal can be canceled cleanly (the
        //    cancelOAuth() callback resets pendingPaste). After cancel the
        //    modal must close and the underlying Edit modal must remain
        //    interactive (the user can re-click Connect to retry).
        await pinCancel.ClickAsync();
        await Assertions.Expect(pinModal).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    }

    [Test]
    public async Task mal_connect_button_surfaces_save_first_guidance_for_new_lists()
    {
        // 1. Navigate to /settings/importlists and open the Add picker.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var addCard = Page.GetByTestId("settings-importlist-add-card");
        await addCard.ClickAsync();

        // 2. Click the MAL tile to open the Edit modal in "add new" mode.
        var malTile = Page.GetByTestId("add-importlist-mal");
        await malTile.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 3. Sanity-check the OAuthInput's completion-mode derivation pins
        //    "paste-callback-url" for MalImportList implementation.
        var oauthContainer = Page.Locator(
            "[data-oauth-completion-mode='paste-callback-url']");
        await Assertions.Expect(oauthContainer).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 4. Fill in the ClientId (MAL needs this before startOAuth dispatches).
        //    D-18 contract: `settings-{provider}-field-{name}` testid.
        var clientIdInput = Page.GetByTestId("settings-importlist-field-clientId");
        await clientIdInput.FillAsync("test-mal-client-id");

        // 5. Click "Connect". For new (un-persisted) MAL lists the backend's
        //    MalImportList.RequestAction("startOAuth") returns
        //    `{ success: false, error: "Save this Import List first..." }`.
        //    The GH #221 wiring must surface this as a form-input error
        //    (NOT silently stall — the original bug).
        var connectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Start OAuth" }).First;
        await connectButton.ClickAsync();

        // 6. State assertion: pendingPaste must REMAIN "none" because the
        //    backend rejected startOAuth. authorizing must flip back to false.
        var pasteState = Page.Locator(
            "[data-oauth-pending-paste='none'][data-oauth-authorizing='false']");
        await Assertions.Expect(pasteState).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // 7. The MalCallbackUrlModal must NOT be visible (pendingPaste never
        //    populated because startOAuth threw on the success=false response).
        var callbackUrlModal = Page.GetByTestId("importlist-mal-callback-url-modal");
        await Assertions.Expect(callbackUrlModal).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 1_000 });

        // 8. PR #228 coderabbit review: the actionable "Save first" guidance
        //    from MalImportList.cs:122 must be USER-VISIBLE — not just present
        //    in hook state. The error flows through `formInputActions` (see
        //    OAuthInput.tsx:155-160) and renders in the FormInputGroup error
        //    display. Assert the leading phrase is on the page so silent
        //    surfacing regressions are caught.
        var saveFirstGuidance = Page.GetByText("Save this Import List first").First;
        await Assertions.Expect(saveFirstGuidance).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });

        // 9. The Edit modal stays open so the user can save first + retry.
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    }
}
