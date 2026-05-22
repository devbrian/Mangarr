using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists.Live;

// Phase 28 Plan 28-01 Task 6 — LIVE MAL callback-URL walk fixture skeleton.
//
// STATUS at commit: DEFERRED — MyAnimeList API config requires manual
// developer-portal registration of a Web Application client with a configured
// redirect URI (e.g., http://localhost:8989/...) before clientId/clientSecret
// can be obtained. The redirect URI must match between MAL's app config and
// the Mangarr backend's PKCE state issuance (Phase 27 D-09). MAL login also
// has its own anti-bot measures and an authorize/consent screen separate
// from the login page.
//
// FOLLOW-UP — see gh issue tracking v1.2 completion of LIVE OAuth walks.
// Likely v1.2 path:
//   - User registers MAL Web App OAuth client manually + adds
//     MAL_CLIENT_ID / MAL_CLIENT_SECRET to .env.
//   - Fixture uses headed Chrome with user completing any MAL anti-bot
//     prompts interactively on the LIVE record path; cassette captures
//     post-OAuth /v2/users/@me/mangalist data-fetch leg only (D-08 caveat).
//
// What this fixture WILL do once activated:
//   1. OneTimeSetUp: register MalImportList; pre-seed root folder.
//   2. Drive Playwright to /settings/importlists -> Add card -> MyAnimeList tile.
//   3. Fill Name, clientId, clientSecret (from env vars MAL_CLIENT_ID /
//      MAL_CLIENT_SECRET).
//   4. Click `Sign In` (Phase 27 D-09 Type=FieldType.OAuth). Server returns
//      the MAL authorize URL with PKCE `code_challenge=plain` + random
//      `state` nonce; modal renders the URL + an empty paste-callback-URL
//      input.
//   5. (Multi-context leg) Open SECOND Playwright context to MAL authorize
//      URL. Fill `input[name=user_name]` with $IMPORT_LIST_MAIL (MAL accepts
//      email or username), `input[name=password]` with $IMPORT_LIST_PW.
//      Click login, then on consent screen click Allow.
//   6. MAL redirects to `http://localhost:8989/<callback-path>?code=<grant>
//      &state=<nonce>`. Capture final URL via
//      `browser_evaluate("location.href")`. Close second context.
//   7. Switch back to Mangarr modal, paste captured URL into paste-callback-
//      URL input, click Submit. Server validates `state` against issued
//      nonce + exchanges `code` via PKCE `code_verifier` (Phase 27 D-09);
//      AccessToken/RefreshToken Hidden fields populate.
//   8. Click Save -> POST /api/v5/command {name:"ImportListSync"} -> poll
//      /api/v5/manga for count delta > 0.
//   9. Assert at least one MAL-sourced manga visible in MangaIndex.
//   10. Audit current-session log for `state.*valid` or `MAL.*state.*ok`
//       confirming server-side nonce validation fired.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
[Explicit("Phase 28 deferred to v1.2 — MAL OAuth-app registration + anti-bot blockers; see UAT-RESULTS.md + GH issue tracking LIVE OAuth-walk completion")]
public class MalImportListLiveFixture : AutomationTest
{
    [Test]
    public async Task mal_callback_oauth_sync_produces_library_row_delta()
    {
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var schema = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/importlist/schema");
        schema.Status.Should().Be(200);
        var schemaJson = await schema.JsonAsync();
        var hasMal = false;
        foreach (var provider in schemaJson!.Value.EnumerateArray())
        {
            if (provider.TryGetProperty("implementation", out var impl)
                && impl.GetString() == "MalImportList")
            {
                hasMal = true;
                break;
            }
        }

        hasMal.Should().BeTrue("MalImportList is registered in /api/v5/importlist/schema (Phase 27)");
    }
}
