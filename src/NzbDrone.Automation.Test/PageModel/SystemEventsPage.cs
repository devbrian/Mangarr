using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemEventsPage : PageBase
{
    public SystemEventsPage(IPage page)
        : base(page)
    {
    }

    public ILocator EventsPanel => Page.GetByTestId("system-events-page");

    public async Task<SystemEventsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/events");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemEventsPage> WaitForLoadedAsync()
    {
        try
        {
            await EventsPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/events — testid added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/events$"));
        }

        return this; // D-17 fluent return-this
    }
}
