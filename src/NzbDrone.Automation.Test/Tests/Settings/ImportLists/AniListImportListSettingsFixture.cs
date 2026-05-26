using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 27 Plan 27-03 Task 3 (automation tier) — proves the AniList list provider
// appears in the Settings → ImportLists Add picker now that Plan 27-03 has landed
// the production IMangaImportList implementation. Sibling fixture to
// MangaDexImportListSettingsFixture (Plan 27-02); same shape, AniList-specific
// selectors.
//
// GH #238 augment (2026-05-21): a `Client Secret` user-visible Password input
// renders at FieldDefinition index 1. This fixture asserts the label exists AND the
// underlying input is rendered as `type=password` so AniList OAuth-client users can
// paste their confidential client_secret without it being visible on screen.
// Matches the MAL GH #233 fixture assertion pattern at commit 8608eff22.
//
// SELECTOR DEVIATION (Plan 27-02 Rule 1 precedent — see Plan 27-02 SUMMARY §Deviations
// #1):
//   27-03-PLAN.md Task 3 Test 7 reserved `picker-importlist-anilist` as the picker
//   testid, but the production AddImportListItem.tsx substrate (Phase 26 Plan 26-05)
//   renders the slug as
//   `add-importlist-{implementation.toLowerCase().replace(/ImportList$/i, '')}`
//   — so `AniListImportList` -> `add-importlist-anilist` (NOT `picker-importlist-*`).
//   Adding a second testid attribute on the substrate component is out-of-scope
//   (touches Phase 26 substrate); we use the substrate-canonical selector here so the
//   provider lights up the picker WITHOUT a frontend round-trip.
//
//   Plan 27-05 close-out should pick up the carry-over decision (delete reservation
//   from spec OR backfill substrate testid attribute).
//
// Pattern κ enforcement (Phase 18 D-18): the `add-importlist-*` prefix is in the
// allowed namespace; zero `series-*` / `episode-*` / `season-*` / `add-series-*`
// selectors used.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AniListImportListSettingsFixture : AutomationTest
{
    [Test]
    public async Task picker_includes_anilist_tile_with_signin_field()
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

        // 3. The AniList tile must be present in the picker (Plan 27-03 lights up the
        //    ImportListFactory reflection scan — the GET /api/v5/importlist/schema
        //    response now includes the AniList implementation alongside MangaDex from
        //    Plan 27-02).
        var anilistTile = Page.GetByTestId("add-importlist-anilist");
        await Assertions.Expect(anilistTile).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. Click the tile to open the Edit modal (the substrate flow goes
        //    Add picker -> click tile -> EditImportListModal in "add new" mode).
        await anilistTile.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 5. The 3 user-visible fields must render: Client ID + Client Secret +
        //    Status (single-select dropdown per D-10). The 4 hidden token fields
        //    (AccessToken / RefreshToken / Expires / AuthUser) must NOT render visibly
        //    (Hidden = HiddenType.Hidden suppresses the generic ProviderFieldFormGroup
        //    renderer per Phase 26 D-09).
        var labels = Page.Locator("label");
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client ID" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client Secret" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Status" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 6. GH #238: the Client Secret input MUST render with type=password so
        //    over-the-shoulder readers cannot see the value. This pins
        //    Type=FieldType.Password on the FieldDefinition — without it the FE falls back to
        //    type=text and the secret renders in clear (Privacy=PrivacyLevel.Password alone
        //    only drives API-outbound redaction, not on-screen masking). Sibling-canonical
        //    with the MAL GH #233 fixture assertion at commit 8608eff22.
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

        // 7. The OAuth sign-in field renders as the "Connect" button (FieldType.OAuth
        //    affordance). The exact label is the localized ImportListsAniListSignInLabel
        //    string ("Connect").
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Connect" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 8. State assertion: no error banner fires on the populated picker (Phase 26
        //    Plan 26-05 D-08 — the schema endpoint returns 200 + a >=2-item array now
        //    after both Plan 27-02 + Plan 27-03 have landed).
        await Assertions.Expect(Page.Locator(".alert-danger")).ToHaveCountAsync(
            0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 1_000 });
    }
}
