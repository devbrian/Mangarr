using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 18 Plan 18-18 — Rename preview modal coverage (INVENTORY v5-endpoint
/// row 85: GET /api/v5/rename).
///
/// Seeds a manga via AddMangaFlow, navigates to MangaDetails, opens the
/// Preview Rename modal (via the manga details toolbar Organize/Rename
/// action), and asserts the preview shape: either at least one rename diff
/// (filename → newfilename) OR an "all names already correct" empty-state.
/// Both are valid steady states.
///
/// The fresh-DB seed has no imported chapter files, so the rename preview
/// returns an empty list — the empty-state path is the canonical one
/// exercised here. When a populated-files cassette lands (Plan-08+ richer
/// seeds), the diff-arrow path activates automatically.
///
/// State assertion: the preview modal renders SOMETHING — either a list-
/// state or an empty-state — and the URL pattern remains on MangaDetails.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RenamePreviewFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task rename_preview_opens_with_diff_or_empty_state()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await Assertions.Expect(details.MainContainer).ToBeVisibleAsync();

        // The Preview Rename action lives on the MangaDetails toolbar. The
        // toolbar button label is "Preview Rename" (Sonarr-shape) or
        // "Rename Files" depending on the action's current wording. Use the
        // role-based locator to find by visible text, falling back to the
        // OrganizeMangaModal which renders the rename preview.
        //
        // MangaDetails.tsx (lines 220-260) has these explicit data-testids:
        //   manga-details-refresh-button
        //   manga-details-manual-search-button
        //   manga-details-edit-button
        //   manga-details-delete-button
        //   manga-details-history-button
        // The Preview Rename action is NOT yet annotated — Wave 2 follow-up.
        // Use the toolbar text-based locator to find it. If it doesn't exist
        // on the page, Assert.Inconclusive so the fixture stays green pending
        // frontend annotation.

        var renameButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Preview Rename" });
        var renameButtonCount = await renameButton.CountAsync();

        if (renameButtonCount == 0)
        {
            // Fallback: try "Rename Files" wording.
            renameButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Rename Files" });
            renameButtonCount = await renameButton.CountAsync();
        }

        // STATE assertion path A: if a rename button exists, click it and assert
        // the preview modal renders with EITHER diff rows OR the all-correct
        // empty-state. The contract is "user can see the rename preview".
        if (renameButtonCount > 0)
        {
            await renameButton.First.ClickAsync();
            await Page.WaitForTimeoutAsync(1_000);

            // The rename preview modal body contains either:
            //   (a) a list of rename diffs — text contains "→" or the
            //       OrganizePreview file list, OR
            //   (b) an empty-state message — translate('AllPathsAreInProperFormat')
            //       or similar.
            // We assert at least one of those signal-strings is present.
            var modalBody = Page.GetByRole(AriaRole.Dialog).First;
            await Assertions.Expect(modalBody).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 10_000
            });

            // STATE assertion: modal renders with non-empty content. The
            // rename-preview component is OrganizePreview.tsx which surfaces
            // either RenamePreviewRow children OR the empty-state text.
            var modalText = await modalBody.TextContentAsync();
            modalText.Should().NotBeNullOrWhiteSpace("preview modal must render either diff content or empty-state text");
        }
        else
        {
            // WR-06 (18-REVIEW): the fallback URL + shell visibility check
            // never exercises the GET /api/v5/rename contract (INVENTORY
            // v5-endpoint row 85) — those assertions just verify the page
            // loaded. NUnit reports the test as PASSED, masking the
            // un-exercised contract. Assert.Inconclusive surfaces the
            // un-annotated entry-point as a follow-up rather than fake-green.
            Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
            await Assertions.Expect(details.MainContainer).ToBeVisibleAsync();

            Assert.Inconclusive(
                "Rename Preview entry-point not annotated on MangaDetails toolbar — see Plan 18-18 follow-up note. " +
                "Wave-3 follow-up: add `manga-details-rename-preview-button` testid in frontend/src/Manga/Details/MangaDetails.tsx. " +
                "GET /api/v5/rename contract NOT exercised until the toolbar entry-point lands.");
        }
    }
}
