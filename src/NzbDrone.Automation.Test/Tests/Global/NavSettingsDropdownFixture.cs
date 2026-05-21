using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Global/ nav-settings sidebar entry
/// (modal-action row 204 in INVENTORY.md). Settings parent expands 11
/// children (MediaManagement / Profiles / CustomFormats / Indexers /
/// DownloadClients / ImportLists / Connect / Metadata / MetadataSource /
/// Tags / General / Ui) when /settings/* is active.
///
/// State assertion: ToHaveAttributeAsync(href) on parent + .CountAsync()
/// on a representative subset of expected child anchors.
///
/// gh-226 absorption (2026-05-21): the dead-code
/// frontend/src/Components/Page/Sidebar/PageSidebar.test.tsx Jest fixture
/// asserted that no child link with `to === '/settings/quality'` exists in the
/// sidebar `links` array (Phase 15 Plan 15-07 D-12 Sonarr divergence — Quality
/// nav was DELETED entirely when the TV-shape quality model was forked out for
/// TranslationProfile). With the Jest fixture deleted under Option B from
/// devbrian/Mangarr#226, the live regression marker for that divergence moves
/// here: `quality_settings_nav_child_is_absent`.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavSettingsDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        // Settings parent (PageSidebar.tsx L125-L183) `to` = "/settings".
        // Navigate there so it becomes the active parent and its children mount.
        await Page.GotoAsync($"{RootUri}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: parent nav-settings anchor.
        var navSettings = Page.GetByTestId("nav-settings");
        await Assertions.Expect(navSettings).ToHaveAttributeAsync("href", new Regex(@"/settings$"));

        // STATE assertion 2: representative child links present. We check the
        // 3 most-load-bearing sub-routes (mediamanagement / profiles /
        // customformats); enumerating all 11 would be brittle if a child is
        // re-ordered in a future plan.
        var mediaMgmtChild = Page.Locator("a[href$='/settings/mediamanagement']");
        var profilesChild = Page.Locator("a[href$='/settings/profiles']");
        var customFormatsChild = Page.Locator("a[href$='/settings/customformats']");

        (await mediaMgmtChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await profilesChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await customFormatsChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
    }

    [Test]
    public async Task quality_settings_nav_child_is_absent()
    {
        // gh-226 absorption — the deleted PageSidebar.test.tsx asserted that
        // the Sonarr-canonical Quality nav child is NOT present in
        // PageSidebar.tsx's Settings children list. Phase 15 Plan 15-07 D-12
        // (Sonarr divergence) removed the Quality nav entry when the TV-shape
        // quality model was forked out for TranslationProfile. This live
        // assertion against a real boot replaces the Jest fixture's static
        // array inspection.
        //
        // We navigate to /settings so the Settings dropdown is the active
        // parent (children only render when active per the sidebar's
        // hasActiveChildLink gate at PageSidebar.tsx L229-L240).
        await Page.GotoAsync($"{RootUri}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Sanity: at least one Settings child IS visible — proves the dropdown
        // is actually expanded and our absence assertion below is meaningful
        // (a sibling-children-not-rendered bug would silently green-light a
        // bare absence assertion).
        var customFormatsChild = Page.Locator("a[href$='/settings/customformats']");
        (await customFormatsChild.CountAsync()).Should().BeGreaterOrEqualTo(1,
            "Settings dropdown must be expanded so the Quality-absence assertion is meaningful");

        // Absence assertion: no anchor under /settings/quality may exist
        // anywhere in the rendered sidebar. The Sonarr ancestry of this app
        // would have rendered this link; Mangarr's TranslationProfile +
        // CustomFormat substitution makes it dead routing.
        var qualityChild = Page.Locator("a[href$='/settings/quality']");
        (await qualityChild.CountAsync()).Should().Be(0,
            "Mangarr forks out the TV-shape Quality model (Phase 15 Plan 15-07 D-12); "
            + "the Quality nav child must NOT render in the Settings sidebar dropdown");
    }
}
