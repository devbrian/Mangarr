using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemStatusPage : PageBase
{
    public SystemStatusPage(IPage page)
        : base(page)
    {
    }

    public ILocator StatusPanel => Page.GetByTestId("system-status-page");
    public ILocator VersionRow  => Page.GetByTestId("system-status-version-row");

    public async Task<SystemStatusPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/status");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemStatusPage> WaitForLoadedAsync()
    {
        // Best-effort: wait for the panel testid (short timeout so we fail-fast to the URL fallback
        // when the testid hasn't been wired yet); if not present in current frontend, fall back
        // to URL match.
        try
        {
            await StatusPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/status — testid will be added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/status$"));
        }

        return this; // D-17 fluent return-this
    }
}
