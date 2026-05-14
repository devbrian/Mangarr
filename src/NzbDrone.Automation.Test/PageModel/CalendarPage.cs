using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/calendar` (CalendarPage).
public class CalendarPage : PageBase
{
    public CalendarPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("calendar-page");

    public async Task<CalendarPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/calendar");
        return await WaitForLoadedAsync();
    }

    public async Task<CalendarPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/calendar$"));
        }

        return this; // D-17 fluent return-this
    }
}
