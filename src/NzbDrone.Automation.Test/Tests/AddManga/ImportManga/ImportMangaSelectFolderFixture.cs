using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga.ImportManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — root-folder selector page coverage.
///
/// Drives the /add/import selector page that <c>ImportMangaSelectFolder.tsx</c>
/// renders. Per <c>feedback_verify_ui_state_not_just_rendering</c>, every
/// assertion pins state (CountAsync / MatchRegex) rather than bare visibility.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportMangaSelectFolderFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task renders_root_folder_list()
    {
        await Page.GotoAsync($"{RootUri}/add/import");

        // Wait for the selector page shell to render. The TestKit baseline
        // seeds ≥1 Root Folder under AutomationTest.SeedBaselineAsync (the
        // canonical Phase 18 D-07 seed shape), so we expect at least one
        // row to mount under the page testid.
        await Page.GetByTestId("import-manga-select-folder-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion: count of root-folder navigation links under
        // the page is ≥1. Post debug-add-import-ui-mismatch fix the page
        // delegates row rendering to the shared <RootFolders/> component
        // (frontend/src/RootFolder/RootFolderRow.tsx), which renders each
        // accessible root folder as an <a href="/add/import/:id"> link
        // — that anchor IS the canonical row contract.
        var rows = await Page
            .Locator("a[href*='/add/import/']")
            .CountAsync();
        rows.Should()
            .BeGreaterThan(
                0,
                "TestKit baseline seeds ≥1 Root Folder per Phase 18 D-07; "
                + "the selector page must render one /add/import/:id link "
                + "per Root Folder (via the shared <RootFolders/> table).");
    }

    [Test]
    public async Task click_root_folder_routes_to_scan()
    {
        await Page.GotoAsync($"{RootUri}/add/import");

        await Page.GetByTestId("import-manga-select-folder-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // Click the first per-row link (the path display). The shared
        // <RootFolders/> component renders each root folder's path as an
        // <a href="/add/import/:id"> anchor — that's the canonical row
        // selector post debug-add-import-ui-mismatch fix.
        var firstLink = Page
            .Locator("a[href*='/add/import/']")
            .First;
        await firstLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await firstLink.ClickAsync();

        // Wait for the URL to settle on the per-folder scan path. RR v5
        // <Link> navigation is synchronous against history; the URL
        // transitions immediately on click without round-tripping through
        // the server.
        await Page.WaitForURLAsync(new Regex(@"/add/import/\d+$"),
            new PageWaitForURLOptions { Timeout = 10_000 });

        // STATE assertion: the URL matches the per-rootFolderId scan
        // pattern (numeric id segment). The URL IS the state evidence —
        // ImportMangaPage's inner Switch will only mount ImportManga.tsx
        // for /add/import/:rootFolderId paths.
        Page.Url.Should()
            .MatchRegex(
                @"/add/import/\d+$",
                "Clicking a Root Folder row must navigate to /add/import/:rootFolderId.");
    }
}
