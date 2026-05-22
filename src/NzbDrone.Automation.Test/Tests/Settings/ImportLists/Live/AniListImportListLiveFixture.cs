using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists.Live;

// Phase 28 Plan 28-01 Task 5 — LIVE AniList Pin walk fixture skeleton.
//
// STATUS at commit: DEFERRED — AniList login is gated by Cloudflare Turnstile
// (anti-bot CAPTCHA) which blocks headless / programmatic Playwright login
// before the developer-client-registration page can be reached. AniList does
// NOT provide a default OAuth client; the user must register one via the
// signed-in developer settings page. Without bypassing Turnstile, the
// orchestrator-driven LIVE walk cannot acquire AniList API credentials.
//
// FOLLOW-UP — see gh issue tracking v1.2 completion of LIVE OAuth walks. The
// likely v1.2 path is one of:
//   - User registers AniList OAuth client manually + adds ANILIST_CLIENT_ID
//     / ANILIST_CLIENT_SECRET to .env; fixture uses headed Chrome with user
//     completing Turnstile interactively on the LIVE record path.
//   - Cassette-replay variant: record cassettes for /graphql MediaListCollection
//     fetch leg only; OAuth setup stays LiveService-only (D-08 caveat).
//
// What this fixture WILL do once activated:
//   1. OneTimeSetUp: register AniListImportList; pre-seed root folder.
//   2. Drive Playwright to /settings/importlists -> Add card -> AniList tile.
//   3. Fill Name, clientId, clientSecret (from env vars
//      ANILIST_CLIENT_ID / ANILIST_CLIENT_SECRET).
//   4. Click `Sign In` (Phase 27 D-07 Type=FieldType.OAuth). Modal displays
//      AniList Pin URL (`https://anilist.co/api/v2/oauth/pin?client_id=...
//      &response_type=token&redirect_uri=https://anilist.co/api/v2/oauth/pin`).
//   5. (Multi-context leg) Open a SECOND Playwright browser context, navigate
//      to the Pin URL, fill `input[name=email]` with $IMPORT_LIST_MAIL +
//      `input[name=password]` with $IMPORT_LIST_PW, click Authorize. Extract
//      displayed Pin via `browser_evaluate("document.querySelector('h1,code,.pin')?.textContent")`.
//   6. Switch back to Mangarr modal, type Pin into the input field, click
//      Submit. Server-side pin->token exchange (Phase 27 D-07) populates the
//      Hidden AccessToken/RefreshToken Settings fields.
//   7. Click Save -> POST /api/v5/command {name:"ImportListSync"} -> poll
//      /api/v5/manga for count delta > 0.
//   8. Assert at least one AniList-sourced manga visible in MangaIndex.
//
// Per `feedback_orchestrator_drives_oauth_paste_back`: no `checkpoint:human-action`
// task slot, no `<resume-signal>`. Orchestrator drives the entire OAuth
// handshake when v1.2 unblocks the Turnstile step.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
[Explicit("Phase 28 deferred to v1.2 — AniList login Turnstile blocker; see UAT-RESULTS.md + GH issue tracking LIVE OAuth-walk completion")]
public class AniListImportListLiveFixture : AutomationTest
{
    [Test]
    public async Task anilist_pin_oauth_sync_produces_library_row_delta()
    {
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var schema = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/importlist/schema");
        schema.Status.Should().Be(200);
        var schemaJson = await schema.JsonAsync();
        var hasAniList = false;
        foreach (var provider in schemaJson!.Value.EnumerateArray())
        {
            if (provider.TryGetProperty("implementation", out var impl)
                && impl.GetString() == "AniListImportList")
            {
                hasAniList = true;
                break;
            }
        }

        hasAniList.Should().BeTrue("AniListImportList is registered in /api/v5/importlist/schema (Phase 27)");
    }
}
