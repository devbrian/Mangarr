using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists.Live;

// Phase 28 Plan 28-01 Task 4 — LIVE MangaDex Test-and-Connect fixture skeleton.
//
// STATUS at commit: PARTIAL — orchestrator-led LIVE walk demonstrated end-to-end
// up to the OAuth-token-acquisition step (count delta still 0). The MangaDex
// API client is registered (AUTOAPPROVED + ACTIVE on the user account); the
// ImportList POSTs accept and persist credentials; ImportListSync command
// completes — but the password-grant exchange wasn't fired because the saved
// list's `AccessToken` / `RefreshToken` Hidden fields stay empty until the
// user clicks `Start OAuth` inside the Edit modal (separate from the `Test`
// validation gear). The card-click selector in the orchestrator session
// didn't surface the Edit modal in time to fire `Start OAuth` before the
// session context budget required handing off.
//
// FOLLOW-UP — gh issue tracking v1.2 completion of LIVE OAuth walks across
// all 3 providers (this fixture + AniList + MAL siblings).
//
// What this fixture WILL do once activated (cassette-replay or LIVE):
//   1. OneTimeSetUp: register MangaDexImportList provider via TestKit; pre-
//      seed a root folder. Set cassette mode based on category.
//   2. Drive Playwright to /settings/importlists -> click Add card -> select
//      MangaDex tile -> fill Name/clientId/clientSecret/username/password
//      from env vars (MANGADEX_CLIENT_ID / MANGADEX_CLIENT_SECRET /
//      IMPORT_LIST_USER / IMPORT_LIST_PW).
//   3. Click `Start OAuth` button (NOT the `Test` validation gear) - the
//      RequestAction("startOAuth") server-side handler fires the password-
//      grant, server stores AccessToken/RefreshToken Hidden field values.
//   4. Click `Save`.
//   5. POST /api/v5/command {name:"ImportListSync"} - wait for command
//      completion (≤60s poll).
//   6. Assert GET /api/v5/manga row count > 0 and at least one row's
//      mangaDexId field is non-empty.
//   7. Navigate to /manga - assert >= 1 MangaIndex row visible.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
// State-not-rendering: API count + visual row both required (D-07 belt+suspenders).
//
// Why [Category("LiveService")] (NOT default-replay): the OAuth callback /
// paste-back UX happens in the user's browser tab, not the Playwright-
// controlled context — cassette infra is HttpClient DelegatingHandler-based
// and cannot capture browser-side state (D-08 fork-heritage caveat).
// Recording the post-OAuth data-fetch leg (`/user/follows/manga`) IS
// possible but requires the LIVE OAuth setup to succeed first.
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
[Explicit("Phase 28 deferred to v1.2 — see Plan 28-01 UAT-RESULTS.md + GH issue tracking LIVE OAuth-walk completion")]
public class MangaDexFollowsImportListLiveFixture : AutomationTest
{
    [Test]
    public async Task mangadex_follows_sync_produces_library_row_delta()
    {
        // This fixture is intentionally stubbed [Explicit] pending v1.2
        // completion. The walk shape above documents the canonical flow.
        // Pre-flight: verify the V5 substrate is alive.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var schema = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/importlist/schema");
        schema.Status.Should().Be(200, "Phase 27 MangaDex ImportList provider is registered");

        var schemaJson = await schema.JsonAsync();
        schemaJson.HasValue.Should().BeTrue();
        var hasMangaDex = false;
        foreach (var provider in schemaJson!.Value.EnumerateArray())
        {
            if (provider.TryGetProperty("implementation", out var impl)
                && impl.GetString() == "MangaDexImportList")
            {
                hasMangaDex = true;
                break;
            }
        }

        hasMangaDex.Should().BeTrue("MangaDexImportList is registered in /api/v5/importlist/schema");
    }
}
