using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/` (MangaIndex).
// Canonical template established in Plan-02 SystemStatusPage.cs:
//   * Constructor takes IPage; locators are ILocator getters via GetByTestId.
//   * OpenAsync(rootUri) + WaitForLoadedAsync chain returns this (D-17 fluent).
//   * Testid-with-URL-fallback: the manga-index-page testid will be wired by
//     Plan-04/Plan-05 cluster work; until then the URL-regex fallback gates
//     the load.
public class MangaIndexPage : PageBase
{
    public MangaIndexPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-index-page");

    public async Task<MangaIndexPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaIndexPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL is root (no trailing path) — testid lands in a cluster plan.
            await Page.WaitForURLAsync(new Regex(@"/$"));
        }

        return this; // D-17 fluent return-this
    }
}
