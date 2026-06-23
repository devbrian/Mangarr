using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket A) — page-render smoke for
// /settings/importlists.
//
// The configured-list table is empty (no import lists added in the baseline
// seed), but Phase 27 landed the production IMangaImportList providers
// (MangaDex / AniList / MAL), so the Add picker now enumerates >= 1 schema card.
// This fixture verifies the render is clean: the page mounts, the Add card opens
// the picker modal with the production providers, and no error banners fire.
//
// Analog: src/NzbDrone.Automation.Test/Tests/Settings/SettingsIndexersFixture.cs
// (page-load smoke).
//
// Pattern κ enforcement (Phase 18 D-18): zero `series-*` / `episode-*` /
// `season-*` / `add-series-*` testids. The `add-importlist-*` /
// `settings-importlist-*` prefixes are the v1.1+ canonical Mangarr namespace
// (PATTERNS.md Plan 26-06 row — exact analog match).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportListsPageRenderFixture : AutomationTest
{
    [Test]
    public async Task page_renders_with_empty_list_and_empty_add_picker()
    {
        // 1. Navigate to /settings/importlists and assert the page container mounts.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/settings/importlists$");

        // 2. Assert the canonical Add card is visible (the FieldSet renders
        //    even when the provider list is empty — D-08).
        var addCard = Page.GetByTestId("settings-importlist-add-card");
        await Assertions.Expect(addCard).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 3. Click the Add card. The Add picker modal opens regardless of
        //    schema length — Phase 26 substrate returns [] from
        //    /api/v5/importlist/schema per D-08.
        await addCard.ClickAsync();

        var addModal = Page.GetByTestId("add-importlist-modal");
        await Assertions.Expect(addModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. State assertion (not just rendering — per
        //    `feedback_verify_ui_state_not_just_rendering`): Phase 27 landed the
        //    production import-list providers (MangaDex / AniList / MAL), so the
        //    Add picker now contains >= 1 `add-importlist-*` schema card. (Before
        //    Phase 27 the substrate shipped zero providers and this asserted 0 —
        //    the assertion was always specified to "flip to >= 1" once providers
        //    landed; this updates the stale Phase-26 expectation.)
        var schemaItems = Page.Locator("[data-testid^='add-importlist-']:not([data-testid='add-importlist-modal'])");
        await Assertions.Expect(schemaItems.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        (await schemaItems.CountAsync()).Should().BeGreaterThan(
            0,
            "Phase 27 landed production import-list providers; the Add picker is no longer empty");

        // 5. Assert no error banner fires (the empty-schema render path must
        //    not surface an Alert kind=DANGER). The AddImportListModalContent
        //    renders an Info alert (`SupportedImportLists`) when isSchemaFetched
        //    and !schemaError — verify no DANGER banner sneaks in.
        await Assertions.Expect(Page.Locator(".alert-danger")).ToHaveCountAsync(
            0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 1_000 });
    }
}
