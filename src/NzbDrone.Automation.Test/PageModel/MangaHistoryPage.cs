using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/activity/history` (MangaHistory).
public class MangaHistoryPage : PageBase
{
    public MangaHistoryPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-history-page");

    public async Task<MangaHistoryPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/manga/activity/history");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaHistoryPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/manga/activity/history$"));
        }

        return this; // D-17 fluent return-this
    }
}
