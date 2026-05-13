using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

/// <summary>
/// MangaIndex page object. Phase 18 Plan-04 ships the minimal shape needed
/// by DeleteMangaModalFixture (return point after delete-confirm) + the
/// delete-removes-manga state assertion. Phase 18 Plan-03 owns the canonical
/// full version with the route-axis MangaIndexPageLoadFixture; union-merges
/// on Mangarr-v0 merge.
/// </summary>
public class MangaIndexPage : PageBase
{
    public MangaIndexPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageRoot => Page.GetByTestId("manga-index-page");
    public ILocator Grid     => Page.GetByTestId("manga-index-grid");

    /// <summary>Locate the per-card cell by titleSlug or mangaId.</summary>
    public ILocator CardByKey(string keyOrSlug) => Page.GetByTestId($"manga-card-{keyOrSlug}");

    public async Task<MangaIndexPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaIndexPage> WaitForLoadedAsync()
    {
        try
        {
            await PageRoot.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: root URL ends with / (no /manga prefix on Mangarr v1 — / is the index per AppRoutes.tsx)
            await Page.WaitForURLAsync(new Regex(@"/(\?.*)?$"));
        }

        return this;
    }
}
