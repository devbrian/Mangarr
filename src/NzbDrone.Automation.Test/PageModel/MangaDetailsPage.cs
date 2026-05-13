using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/:titleSlug` (MangaDetails).
// NOT exercised by Plan-03 route load fixtures (requires seeded manga via TestKit;
// fixture lives in Plan-04 AddManga cluster where the seeding flow exists).
// Template established here for Plan-04 to consume.
//
// Special case: route requires a titleSlug param; provides OpenAsync(rootUri, titleSlug)
// overload rather than the bare OpenAsync(rootUri) shape used elsewhere.
public class MangaDetailsPage : PageBase
{
    public MangaDetailsPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-details-page");

    public async Task<MangaDetailsPage> OpenAsync(string rootUri, string titleSlug)
    {
        await Page.GotoAsync($"{rootUri}/manga/{titleSlug}");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaDetailsPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL matches /manga/{slug} shape (no further nesting).
            await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"));
        }

        return this; // D-17 fluent return-this
    }
}
