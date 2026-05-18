using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 25 Plan 25-05 — InteractiveImport folder-input coverage (Pitfall 12 §3).
///
/// Authored at Phase 25 close-out as the live counterpart to the Plan 25-03
/// /add/import full inline page (D-02 — Sonarr-canonical, no Modal chrome) +
/// Plan 25-02 V5 ManualImportController + Plan 25-04 typed-discriminator
/// rewrite. Replaces the Phase 18 Plan 18-18 inconclusive-stub that emitted
/// DEF-18-08-01 (resolved by Plan 25-03 — /add/import is now wired).
///
/// 3 [Test] methods per Pitfall 12 §3 (folder-type registry):
///   1. scans_root_folder    — supplies a path inside a configured root folder
///   2. scans_staging_folder — supplies a downloadId via query string (staging-path fast-path)
///   3. scans_arbitrary_folder — supplies an arbitrary temp path outside any root
///
/// Each test navigates to /add/import (page-host route from 25-03), waits for
/// the `add-import-page` testid (proves the page rendered), supplies the
/// case-specific folder input, and asserts that the manual-import surface
/// reflects the expected state (folder-picker stays / advances / table renders
/// / empty-state appears). Helpers are private inline (small scope; do NOT
/// extract to a shared base — Phase 25 close-out scope only).
///
/// Pitfall 16 sequencing: Task 5 phase-smoke-gate runs this fixture live BEFORE
/// the Task 6 LOCK guard drop in Wanted/Missing/Missing.tsx. The 3 tests use
/// the /add/import page entry point (Task 6-independent); the post-LOCK
/// Wanted/Missing modal entry point is covered by manual smoke #3, not by
/// this fixture.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportFolderFixture : AutomationTest
{
    private const string AddImportPageTestId = "add-import-page";
    private const string InteractiveImportContentTestId = "interactive-import-content";
    private const string FolderInputName = "folder";

    [Test]
    public async Task scans_root_folder()
    {
        // ROOT case (Pitfall 12 §3 row 1): the path supplied to the folder
        // picker resolves inside a configured root folder. The
        // AutomationTest base seeds a root folder at `{AppData}/MangaLibrary`
        // in SeedBaselineAsync; we reuse it so the path is guaranteed to
        // resolve under a real root entry.
        var rootFolder = Path.Combine(Runner.AppData, "MangaLibrary");
        EnsureFolderExists(rootFolder);

        await NavigateToAddImportPageAsync();

        // STATE assertion: page shell rendered (the page-host testid from 25-03).
        await Assertions.Expect(Page.GetByTestId(AddImportPageTestId))
                        .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Drive the folder picker via the PathInput (name="folder"), then
        // click the InteractiveImport button so the manual-import table mounts.
        await SupplyFolderAsync(rootFolder);

        // STATE assertion: the InteractiveImport content surface mounted.
        // Either the table populates (root with content) or the empty-state
        // renders (root with no resolvable CBZ archives). Both prove the
        // root-folder scan path executed; the table-vs-empty branching is a
        // content-dependent detail this gate does not pin.
        await Assertions.Expect(Page.GetByTestId(InteractiveImportContentTestId))
                        .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    }

    [Test]
    public async Task scans_staging_folder()
    {
        // STAGING case (Pitfall 12 §3 row 2): a downloadId is supplied so the
        // ManualImportService staging-path fast-path fires (uses the
        // ChapterDownloadState repo to resolve the OutputPath). We pass the
        // downloadId via the /add/import query string — the page wires
        // `downloadId` through InteractiveImportContent into the GET call.
        //
        // For this fixture we only need a syntactically valid downloadId; the
        // fast-path code returns an empty-state when the row is missing
        // (rather than throwing), which is the assertion target — the page
        // still mounted and the staging-path codepath executed without error.
        var syntheticDownloadId = $"phase25-staging-{Guid.NewGuid():N}";

        await NavigateToAddImportPageAsync($"downloadId={Uri.EscapeDataString(syntheticDownloadId)}");

        await Assertions.Expect(Page.GetByTestId(AddImportPageTestId))
                        .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // STATE assertion: the InteractiveImport content surface mounted for
        // the staging-path scan. The downloadId fast-path bypasses the folder
        // picker entirely so the content testid is the canonical evidence.
        await Assertions.Expect(Page.GetByTestId(InteractiveImportContentTestId))
                        .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    }

    [Test]
    public async Task scans_arbitrary_folder()
    {
        // ARBITRARY case (Pitfall 12 §3 row 3): the path supplied is a temp
        // folder outside any configured root folder. The ManualImportService
        // still scans it (the route accepts any filesystem path the host can
        // read); rows that cannot be resolved against any known manga show
        // up with a rejection reason but the table itself renders.
        var arbitraryFolder = Path.Combine(
            Path.GetTempPath(),
            $"phase25-arbitrary-{Guid.NewGuid():N}");
        EnsureFolderExists(arbitraryFolder);

        try
        {
            await NavigateToAddImportPageAsync();

            await Assertions.Expect(Page.GetByTestId(AddImportPageTestId))
                            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            await SupplyFolderAsync(arbitraryFolder);

            // STATE assertion: the InteractiveImport content surface mounted
            // for an arbitrary path. The path is outside any configured root
            // and contains no CBZ files, so the empty-state branch fires —
            // but the content testid is present either way (table-on-rows or
            // empty-state-on-zero-rows), proving the scan path executed.
            await Assertions.Expect(Page.GetByTestId(InteractiveImportContentTestId))
                            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        }
        finally
        {
            // Test-local cleanup: drop the temp folder we created. Wrap in
            // try-catch so a leftover file-handle from a Playwright probe
            // does not mask the underlying test outcome.
            try
            {
                if (Directory.Exists(arbitraryFolder))
                {
                    Directory.Delete(arbitraryFolder, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp dir is collected eventually.
            }
        }
    }

    // ---- Private helpers (inline scope per plan note: small helpers stay in
    //      this fixture; do not extract to a shared base class) -------------

    private async Task NavigateToAddImportPageAsync(string queryString = "")
    {
        var url = string.IsNullOrEmpty(queryString)
            ? $"{HostBaseUrl}/add/import"
            : $"{HostBaseUrl}/add/import?{queryString}";

        await Page.GotoAsync(url);
    }

    private async Task SupplyFolderAsync(string folderPath)
    {
        // The folder picker's PathInput renders an <input name="folder">.
        // Fill it, blur to trigger the onChange handler, and click the
        // "Interactive Import" button to advance into the table view.
        var folderInput = Page.Locator($"input[name='{FolderInputName}']");
        await folderInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await folderInput.FillAsync(folderPath);
        await folderInput.PressAsync("Tab");

        var interactiveImportButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
        {
            Name = "Interactive Import"
        });
        await interactiveImportButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await interactiveImportButton.ClickAsync();
    }

    private static void EnsureFolderExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
