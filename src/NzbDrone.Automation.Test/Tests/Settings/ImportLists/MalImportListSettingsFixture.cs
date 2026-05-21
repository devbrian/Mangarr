using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 27 Plan 27-04 Task 3 (automation tier) — proves the MAL list provider
// appears in the Settings → ImportLists Add picker now that Plan 27-04 has landed
// the production IMangaImportList implementation. Sibling fixture to
// MangaDexImportListSettingsFixture (Plan 27-02) + AniListImportListSettingsFixture
// (Plan 27-03); same shape, MAL-specific selectors.
//
// GH #233 augment (2026-05-21): a `Client Secret` user-visible Password input
// now renders at FieldDefinition index 1 (between Client ID and Status). This
// fixture asserts the label exists AND the underlying input is rendered as
// `type=password` so MAL App Type "web" users can paste their confidential
// client_secret without it being visible on screen.
//
// SELECTOR DEVIATION (Plan 27-02 + Plan 27-03 Rule 1 precedent — see Plan 27-02
// SUMMARY §Deviations #1):
//   27-04-PLAN.md Task 3 Test 10 reserved `picker-importlist-mal` as the picker
//   testid, but the production AddImportListItem.tsx substrate (Phase 26 Plan 26-05)
//   renders the slug as
//   `add-importlist-{implementation.toLowerCase().replace(/ImportList$/i, '')}`
//   — so `MalImportList` -> `add-importlist-mal` (NOT `picker-importlist-*`).
//   Adding a second testid attribute on the substrate component is out-of-scope
//   (touches Phase 26 substrate); we use the substrate-canonical selector here so
//   the provider lights up the picker WITHOUT a frontend round-trip.
//
//   Plan 27-05 close-out should pick up the carry-over decision (delete reservation
//   from spec OR backfill substrate testid attribute) — already flagged by Plans
//   27-02/27-03 SUMMARYs.
//
// Pattern κ enforcement (Phase 18 D-18): the `add-importlist-*` prefix is in the
// allowed namespace; zero `series-*` / `episode-*` / `season-*` / `add-series-*`
// selectors used.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MalImportListSettingsFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Pitfall 10 / cross-fixture contract: Comix indexer is un-cassetted and would
        // escape to the live network on PuppeteerSharp warm-up if its schema endpoint
        // were enumerated. Mirror sibling MangaDex + AniList ImportList automation fixtures.
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task picker_includes_mal_tile_with_signin_field()
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

        // 3. The MAL tile must be present in the picker (Plan 27-04 lights up the
        //    ImportListFactory reflection scan — the GET /api/v5/importlist/schema
        //    response now includes the MyAnimeList implementation alongside MangaDex
        //    (Plan 27-02) + AniList (Plan 27-03)).
        var malTile = Page.GetByTestId("add-importlist-mal");
        await Assertions.Expect(malTile).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. Click the tile to open the Edit modal (the substrate flow goes
        //    Add picker -> click tile -> EditImportListModal in "add new" mode).
        await malTile.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 5. The 3 user-visible fields must render: Client ID (index 0), Client Secret
        //    (index 1 — GH #233; optional Password input for MAL App Type "web"), Status
        //    (index 2 — single-select dropdown per D-10). Hidden token + PendingPkceState
        //    + AuthUser fields (indices 3-7) must NOT render visibly (Hidden =
        //    HiddenType.Hidden suppresses the generic ProviderFieldFormGroup renderer).
        //    Per-user model — Mangarr does NOT ship a compiled-in MAL client_id;
        //    consistent with MangaDex + AniList Settings shapes.
        var labels = Page.Locator("label");
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client ID" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // GH #233: Client Secret label must render (the localized
        // ImportListsMalClientSecretLabel string -> "Client Secret"). The HelpText
        // describes the App Type "web" requirement so users discover it without docs.
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Client Secret" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Status" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 6. GH #233: the Client Secret input MUST render with type=password so
        //    over-the-shoulder readers cannot see the value. This pins
        //    Privacy=PrivacyLevel.Password on the FieldDefinition — without it the FE
        //    falls back to type=text and the secret renders in clear.
        //
        //    The substrate renders user-visible Password fields via TextInput's
        //    PasswordInput branch (Components/Form/TextInput.tsx). The input selector
        //    inside the edit modal scopes to the Client Secret form group via the
        //    label-for/id pair the generic renderer emits.
        var clientSecretInput = editModal.Locator("input[name='clientSecret']");
        await Assertions.Expect(clientSecretInput).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(clientSecretInput).ToHaveAttributeAsync(
            "type",
            "password",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });

        // 7. The OAuth sign-in field renders as the "Connect" button (FieldType.OAuth
        //    affordance). The exact label is the localized ImportListsMalSignInLabel
        //    string ("Connect").
        await Assertions.Expect(labels.Filter(new LocatorFilterOptions { HasText = "Connect" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // 8. State assertion: no error banner fires on the populated picker (Phase 26
        //    Plan 26-05 D-08 — the schema endpoint returns 200 + a >=3-item array now
        //    after Plans 27-02 + 27-03 + 27-04 have all landed).
        await Assertions.Expect(Page.Locator(".alert-danger")).ToHaveCountAsync(
            0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 1_000 });
    }
}
