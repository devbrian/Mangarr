using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/activity/queue` (MangaQueue).
public class MangaQueuePage : PageBase
{
    public MangaQueuePage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-queue-page");

    public async Task<MangaQueuePage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/manga/activity/queue");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaQueuePage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/manga/activity/queue$"));
        }

        return this; // D-17 fluent return-this
    }
}
