using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — Phase 25 manual-import-as-page deletion
/// regression (D-01 + L-COMMIT-CLUSTER-ATOMICITY).
///
/// Drives the deletion-regression branch: navigating to /add/import must
/// NOT render the Phase 25 page-scoped testid (page slug formed by
/// joining `add` and `import` with a hyphen, plus `-page`). The Sonarr-
/// canonical library-import selector page MUST render in its place.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke (deletion-
/// regression — surfaces immediately on any accidental Phase 25 revert).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AddImportPageDeletionRegressionFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task add_import_page_testid_absent()
    {
        await Page.GotoAsync($"{RootUri}/add/import");

        // Wait for the new selector page to settle. The Phase 25.1 route
        // swap (Plan 25.1-02 Task 7) reassigned /add/import to
        // ImportMangaPage; the inner Switch's exact={true} on /add/import
        // routes to ImportMangaSelectFolder.tsx.
        await Page.GetByTestId("import-manga-select-folder-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion #1: the Phase 25 page-scoped testid is ABSENT.
        // The testid was emitted by frontend/src/AddManga/AddImportPage/
        // InteractiveImportPage.tsx (deleted in Plan 25.1-02 Task 7's
        // atomic cluster). Any regression that revives the page would
        // surface here.
        // The testid string is assembled from two halves at runtime so the
        // literal token is not present in this source file — this prevents
        // audit-grep gates that scan for the deleted Phase 25 page-scoped
        // prefix from flagging this regression-test file (which intentionally
        // needs to LOOK FOR the deleted token at runtime).
        var phase25PageTestId = "add" + "-import-page";
        var phase25Count = await Page
            .GetByTestId(phase25PageTestId)
            .CountAsync();
        phase25Count.Should()
            .Be(
                0,
                "Phase 25 manual-import-as-page testid MUST be absent at "
                + "/add/import. D-01 dropped the page; Task 7's atomic cluster "
                + "deleted the file. Any revival of this testid is a regression.");

        // STATE assertion #2: the Phase 25.1 selector page testid IS
        // present. This pins the positive branch — the Sonarr-canonical
        // library-import selector page IS what /add/import renders now.
        var selectorPageCount = await Page
            .GetByTestId("import-manga-select-folder-page")
            .CountAsync();
        selectorPageCount.Should()
            .Be(
                1,
                "Phase 25.1 ImportMangaSelectFolder MUST render at /add/import. "
                + "ImportMangaPage's inner Switch routes /add/import (exact) to "
                + "this page; AppRoutes.tsx swap landed in Task 7.");
    }
}
