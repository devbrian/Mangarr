using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 27.1 Plan 27.1-05 Task 7 — Smoke fixture asserting the Sonarr-parity
// PageToolbarButton pair (TestAllLists + ManageImportLists) renders on the
// /settings/importlists page shipped in Plan 27.1-05 Task 2.
//
// Pitfall 5 (RESEARCH §Pitfall 5): this fixture DOES NOT click TestAllLists.
// The inherited ProviderControllerBase /testall endpoint loops every enabled
// provider's Test() method synchronously; in a test environment with no
// HttpClient hardening that loop can OOM. Live click verification is the
// "Manual-Only Verifications" path documented in VALIDATION.md and is
// excluded from automation per executor_constraints.
//
// Analog: src/NzbDrone.Automation.Test/Tests/Settings/SettingsIndexersFixture.cs
// (page-load smoke) + ImportListsPageRenderFixture.cs (in-tree empty-state
// smoke for /settings/importlists). Comix disabled in OneTimeSetUp per the
// cross-fixture Pitfall 10 contract.
//
// Pattern κ enforcement (Phase 18 D-18): zero TV-shape testids in this
// fixture (no Sonarr-prefix tokens). All references use the v1.1+ canonical
// `settings-importlists-*` namespace from ImportListSettings.tsx Task 2.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportListsSettingsTestFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task page_renders_at_settings_importlists_route()
    {
        // 1. Navigate to /settings/importlists and assert the page container
        //    mounts. The `settings-importlists-page` testid is the canonical
        //    wrapper-div selector preserved from Phase 26 substrate per A10.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/settings/importlists$");
    }

    [Test]
    public async Task test_all_lists_toolbar_button_is_rendered()
    {
        // 2. Assert the Sonarr-parity TestAllLists PageToolbarButton is
        //    rendered. This is the load-bearing assertion for D-02 — Phase 26
        //    D-05 claimed Sonarr had no Test-All hook; v5-develop ground-truth
        //    proved that claim wrong; this button is the partial revocation.
        //
        //    Selector: data-testid="settings-importlists-test-all-button"
        //    (literal exposed by ImportListSettings.tsx Plan 27.1-05 Task 2;
        //    resolved via the SettingsImportListsPage.TestAllButton locator).
        //
        //    PITFALL 5: assert visibility only. DO NOT call ClickAsync —
        //    clicking would dispatch the testAllImportLists Redux thunk which
        //    fans out to the /api/v5/importlist/testall synchronous loop.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(page.TestAllButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // State assertion (per feedback_verify_ui_state_not_just_rendering +
        // scripts/audit-test-assertions.sh): pin the rendered label so a
        // future regression (button renamed, i18n key swapped, wrong
        // PageToolbarButton wired) fails loudly instead of silently passing
        // a visibility-only check.
        await Assertions.Expect(page.TestAllButton).ToHaveAttributeAsync(
            "title", "Test All Lists");
    }

    [Test]
    public async Task manage_import_lists_toolbar_button_is_rendered()
    {
        // 3. Assert the ManageImportLists PageToolbarButton is rendered.
        //    Wired to the Plan 27.1-04 ManageImportListsModal subtree.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(page.ManageButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // State assertion (per feedback_verify_ui_state_not_just_rendering +
        // scripts/audit-test-assertions.sh): pin the rendered label so a
        // future regression fails loudly instead of silently passing a
        // visibility-only check.
        await Assertions.Expect(page.ManageButton).ToHaveAttributeAsync(
            "title", "Manage Import Lists");
    }

    [Test]
    public async Task options_field_set_is_rendered()
    {
        // 4. Assert the Plan 27.1-02 ImportListOptions FieldSet wrapper is
        //    rendered (`settings-importlists-options` testid is the canonical
        //    wrapper-div selector from Plan 27.1-02 Task 2). The FieldSet
        //    only renders when Advanced Settings is enabled; the wrapper div
        //    is conditional on `showAdvancedSettings`, so this assertion runs
        //    only after toggling advanced ON.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await page.PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // Phase 27.1 27.1-REVIEW WR-03 fix-forward (2026-05-21): testid is
        // `settings-advanced-toggle` not `settings-advanced-button`
        // (frontend/src/Settings/AdvancedSettingsButton.tsx:27); the silent
        // `if (toggleCount > 0)` guard masked the typo by skipping the body
        // entirely. Assert visibility unconditionally so a missing toggle (or
        // any future testid regression) fails the test loudly per the
        // state-not-rendering anti-pattern gate.
        var advancedToggle = Page.GetByTestId("settings-advanced-toggle");
        await Assertions.Expect(advancedToggle).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await advancedToggle.ClickAsync();

        var optionsContainer = Page.GetByTestId("settings-importlists-options");
        await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
    }

    [Test]
    public async Task import_list_exclusions_section_is_rendered()
    {
        // 5. Assert the Plan 27.1-03 ImportListExclusions section is
        //    rendered. The wrapper-div testid `settings-importlist-exclusions`
        //    is the canonical selector preserved from Phase 26 + Plan 27.1-03.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        var exclusionsContainer = Page.GetByTestId("settings-importlist-exclusions");
        await Assertions.Expect(exclusionsContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // State assertion (per feedback_verify_ui_state_not_just_rendering +
        // scripts/audit-test-assertions.sh): assert the Sonarr-canonical Plan
        // 27.1-03 D-01 column headers are present. The 3 manga-ID columns
        // (`MangaDex ID` / `MyAnimeList ID` / `AniList ID`) replaced the
        // single Sonarr-shape TvdbId column — pin that contract so a future
        // regression (column dropped, header text drifted) fails loudly.
        var sectionText = await exclusionsContainer.TextContentAsync();
        sectionText.Should().Contain("MangaDex ID");
        sectionText.Should().Contain("MyAnimeList ID");
        sectionText.Should().Contain("AniList ID");
    }
}
