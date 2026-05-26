using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga.ImportManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — RR v5 inner-Switch exact-prop ordering
/// regression (L-RR5-EXACT-ORDERING per 25.1-RESEARCH §9).
///
/// Drives the negative-test branch: navigating to /add/import/123 (the
/// per-folder scan path) must NOT render ImportMangaSelectFolder. The
/// inner Switch's exact-prop on /add/import is the load-bearing guard —
/// without it, RR v5's prefix-matching Route would shadow the
/// /:rootFolderId child route.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke (routing-
/// shadow regression — any regression here breaks the entire library-
/// import flow).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportMangaPageRoutingFixture : AutomationTest
{
    [Test]
    public async Task rootFolderId_path_does_not_match_selector()
    {
        // Navigate to a synthetic /:rootFolderId path. The id 99999 is
        // chosen to be high enough that no TestKit-seeded root folder
        // matches; the assertion is about routing-shadow, NOT about scan
        // content. ImportManga.tsx will render its "RootFolderNotFound"
        // empty-state branch, but crucially the
        // ImportMangaSelectFolder testid MUST NOT appear.
        await Page.GotoAsync($"{RootUri}/add/import/99999");

        // Wait for ImportMangaPage's inner Switch to settle on the
        // ImportManga (per-folder scan) branch. The scan view always
        // mounts its body testid even in the RootFolderNotFound branch.
        await Page.GetByTestId("import-manga-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion: the selector page testid is ABSENT. WITHOUT
        // the inner Switch's exact={true} on /add/import, the first
        // route would prefix-match /:rootFolderId and the selector page
        // would render here too — a routing-shadow regression.
        var selectorPageCount = await Page
            .GetByTestId("import-manga-select-folder-page")
            .CountAsync();
        selectorPageCount.Should()
            .Be(
                0,
                "L-RR5-EXACT-ORDERING — exact={true} on the inner /add/import "
                + "route prevents shadowing of /:rootFolderId. Selector page "
                + "MUST NOT render at /add/import/:rootFolderId paths.");

        // STATE assertion #2: the scan body testid IS present. This pins
        // the positive branch — the per-folder scan view IS the route's
        // canonical render at /:rootFolderId paths.
        var scanPageCount = await Page
            .GetByTestId("import-manga-page")
            .CountAsync();
        scanPageCount.Should()
            .Be(
                1,
                "L-RR5-EXACT-ORDERING positive — ImportManga.tsx body MUST "
                + "render at /add/import/:rootFolderId paths.");
    }
}
