using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.V11Closeout;

// Phase 28 Plan 28-01 Task 2 — V11Closeout per-vertical fixture.
//
// Vertical 4: Interactive Import (Phase 25 — ManualImport V5 controller +
// /add/import top-level route + Wanted/Missing LOCK guard removal).
// 1. /add/import (top-level Import-from-disk entry, GH #110) renders
//    `import-manga-select-folder-page`.
// 2. /manga/wanted/missing renders the `manga-missing-page` and the
//    "Manual Import" toolbar button is enabled (Phase 25 Pitfall 16 LOCK
//    guard `mediaType !== 'manga'` dropped as the final commit of Phase 25
//    per Plan 12-11 LOCK guard removal sequenced last).
// 3. /api/v5/manualimport answers 200 (Phase 25 ManualImport V5 controller,
//    GH #175) — even with no `folder` query the endpoint returns 200 + []
//    per RestController<T> contract.
//
// Pattern κ enforcement: zero `series-*` / `episode-*` / `season-*` /
// `add-series-*` testids in this fixture or in the routes it walks.
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportClosingFixture : AutomationTest
{
    [Test]
    public async Task add_import_and_wanted_missing_routes_render_with_lock_guard_dropped()
    {
        // Leg 1: /add/import top-level (Phase 25 #110)
        await Page.GotoAsync($"{RootUri}/add/import");
        await Assertions.Expect(Page.GetByTestId("import-manga-select-folder-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/add/import$");

        // Leg 2: /manga/wanted/missing (Phase 25 LOCK guard dropped)
        await Page.GotoAsync($"{RootUri}/manga/wanted/missing");
        await Assertions.Expect(Page.GetByTestId("manga-missing-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Manual Import toolbar button MUST be enabled — the `mediaType !== 'manga'`
        // LOCK guard at Wanted/Missing/Missing.tsx was dropped as Plan 12-11's last
        // commit per ROADMAP Pitfall 16.
        // Manual Import surfaces as a Sonarr-style toolbar PageToolbarButton — a
        // <button> with text+title "Manual Import", NOT an <a role=link>. Use
        // AriaRole.Button so the locator matches Mangarr's actual element shape.
        var manualImportButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Manual Import" });
        await Assertions.Expect(manualImportButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var isDisabled = await manualImportButton.IsDisabledAsync();
        isDisabled.Should().BeFalse(
            "Phase 25 dropped the mediaType !== 'manga' LOCK guard — button must be interactable");

        // Leg 3: ManualImport V5 controller answers (GH #175)
        var mimResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/manualimport");
        mimResp.Status.Should().Be(200,
            "Phase 25 ManualImport V5 controller — empty-folder GET returns 200 + [] per RestController contract");
    }
}
