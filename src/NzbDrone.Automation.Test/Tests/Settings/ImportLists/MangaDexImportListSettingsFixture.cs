using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 27 Plan 27-02 Task 3 (automation tier) — proves the MangaDex follows-list
// provider appears in the Settings → ImportLists Add picker now that Plan 27-02 has
// landed the production IMangaImportList implementation. Replaces the empty-state
// assertion in ImportListsPageRenderFixture (Phase 26 bucket A) with a populated-state
// smoke for the MangaDex tile specifically.
//
// GH #238 augment (2026-05-21): `Client Secret` (index 1) AND `Password` (index 3) are
// user-visible Password inputs. This fixture asserts BOTH render as `type=password`
// so MangaDex personal-client OAuth users can paste their confidential client_secret
// AND their reusable account password without either being visible on screen. Matches
// the MAL GH #233 fixture assertion pattern at commit 8608eff22.
//
// SELECTOR DEVIATION (Plan 27-02 Rule 1 auto-fix — see SUMMARY §Deviations):
//   27-02-PLAN.md Test 6 reserved `picker-importlist-mangadex` as the picker testid,
//   but the production AddImportListItem.tsx substrate (Phase 26 Plan 26-05) renders
//   the slug as `add-importlist-{implementation.toLowerCase().replace(/ImportList$/i, '')}`
//   — so `MangaDexImportList` -> `add-importlist-mangadex` (NOT `picker-importlist-*`).
//   Adding a second testid attribute on the substrate component is out-of-scope
//   (touches Phase 26 substrate); we use the substrate-canonical selector here so the
//   provider lights up the picker WITHOUT a frontend round-trip.
//
// Pattern κ enforcement (Phase 18 D-18): the `add-importlist-*` prefix is in the
// allowed namespace; zero `series-*` / `episode-*` / `season-*` / `add-series-*`
// selectors used.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaDexImportListSettingsFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Pitfall 10 / cross-fixture contract: Comix indexer is un-cassetted and would
        // escape to the live network on PuppeteerSharp warm-up if its schema endpoint
        // were enumerated. Mirror ImportListsPageRenderFixture.OneTimeSetUp.
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task picker_includes_mangadex_tile_with_signin_field()
    {
        // 1. Navigate to /settings/importlists and verify the page mounts.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/settings/importlists$");

        // 2. Click the Add card to open the schema picker.
        var addCard = Page.GetByTestId("settings-importlist-add-card");
        await Assertions.Expect(addCard).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await addCard.ClickAsync();

        var addModal = Page.GetByTestId("add-importlist-modal");
        await Assertions.Expect(addModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 3. The MangaDex tile must be present in the picker (Plan 27-02 lights up the
        //    ImportListFactory reflection scan — the GET /api/v5/importlist/schema
        //    response now includes the MangaDex implementation).
        var mangadexTile = Page.GetByTestId("add-importlist-mangadex");
        await Assertions.Expect(mangadexTile).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. Click the tile to open the Edit modal (the substrate flow goes
        //    Add picker -> click tile -> EditImportListModal in "add new" mode).
        await mangadexTile.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 5. The 4 user-visible credential fields must render (ClientId / ClientSecret /
        //    Username / Password). The 4 hidden token fields (AccessToken / RefreshToken /
        //    Expires / AuthUser) must NOT render visibly (Hidden = HiddenType.Hidden
        //    suppresses the generic ProviderFieldFormGroup renderer per Phase 26 D-09).
        //    Generic field-renderer testids follow the pattern `field-{settingsFieldName}`
        //    or are surfaced through the user-visible label text.
        var labels = Page.Locator("label");
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client ID" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client Secret" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Username" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Password", HasNotText = "Test" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 6. GH #238: BOTH the Client Secret AND the Password inputs MUST render with
        //    type=password so over-the-shoulder readers cannot see the values. This pins
        //    Type=FieldType.Password on the corresponding FieldDefinitions — without it
        //    the FE falls back to type=text and the secrets render in clear
        //    (Privacy=PrivacyLevel.Password alone only drives API-outbound redaction, not
        //    on-screen masking). The MangaDex `password` field is the user's reusable
        //    account login password — a bigger leak surface than the OAuth client secret.
        //    Sibling-canonical with the MAL GH #233 fixture assertion at commit 8608eff22.
        //
        //    The substrate renders user-visible Password fields via TextInput's
        //    PasswordInput branch (Components/Form/TextInput.tsx). Selector pattern
        //    `settings-{provider}-field-{name}` per D-18 + GH #180 (banned shape
        //    `Locator("input[name=...")` would trip scripts/audit-test-assertions.sh).
        var clientSecretInput = Page.GetByTestId("settings-importlist-field-clientSecret");
        await Assertions.Expect(clientSecretInput).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(clientSecretInput).ToHaveAttributeAsync(
            "type",
            "password",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });

        var passwordInput = Page.GetByTestId("settings-importlist-field-password");
        await Assertions.Expect(passwordInput).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(passwordInput).ToHaveAttributeAsync(
            "type",
            "password",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });

        // 7. The OAuth sign-in field renders as the "Test & Connect" button (FieldType.OAuth
        //    affordance). The exact label is the localized ImportListsMangaDexSignInLabel
        //    string ("Test & Connect").
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Test & Connect" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 8. State assertion: no error banner fires on the populated picker (Phase 26
        //    Plan 26-05 D-08 — the schema endpoint returns 200 + a single-item array now).
        await Assertions.Expect(Page.Locator(".alert-danger")).ToHaveCountAsync(
            0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 1_000 });
    }
}
