using System.Threading.Tasks;
using Microsoft.Playwright;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Flows;

// Phase 18 Plan-08 (D-08 pre-cataloged) -- cross-page sequence helper.
//
// Encapsulates the open-search-then-grab-first-release flow so fixture tests
// can express the chained user journey in a single call. Used by
// InteractiveSearchGrabFixture (D-14 PRSmoke -- Interactive-Search-and-Grab).
public static class SearchAndGrabFlow
{
    /// <summary>
    /// Open the InteractiveSearch surface for a manga (by titleSlug), then click
    /// Grab on the first listed release row. Throws InvalidOperationException
    /// when zero releases are visible -- the caller is expected to seed the
    /// MangaDex cassette or otherwise ensure deterministic results.
    /// </summary>
    public static async Task OpenForMangaAndGrabFirstReleaseAsync(IPage page, string rootUri, string mangaSlug)
    {
        var modal = await new InteractiveSearchModal(page).OpenForMangaAsync(rootUri, mangaSlug);

        var count = await modal.GetReleaseCountAsync();
        if (count == 0)
        {
            throw new System.InvalidOperationException(
                "Interactive search returned 0 releases -- cassette state or live backend issue");
        }

        await modal.GrabAsync(0);
    }
}
