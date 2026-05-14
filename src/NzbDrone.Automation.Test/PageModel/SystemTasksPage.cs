using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemTasksPage : PageBase
{
    public SystemTasksPage(IPage page)
        : base(page)
    {
    }

    public ILocator TasksPanel => Page.GetByTestId("system-tasks-page");

    public async Task<SystemTasksPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/tasks");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemTasksPage> WaitForLoadedAsync()
    {
        // Best-effort: wait for the panel testid (short timeout so we fail-fast to the URL fallback
        // when the testid hasn't been wired yet); if not present in current frontend, fall back
        // to URL match.
        try
        {
            await TasksPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/tasks — testid added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/tasks$"));
        }

        return this; // D-17 fluent return-this
    }
}
