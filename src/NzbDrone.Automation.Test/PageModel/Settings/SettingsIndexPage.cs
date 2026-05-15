using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings (root) PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings | Settings index page renders | 🟢`.
//
// The parent SettingsPage (PageModel/SettingsPage.cs) already exposes tab-navigator
// locators for cross-page navigation; this thin sibling lives under PageModel/Settings/
// to match the per-route convention used by the other route-load fixtures and exposes
// only the page-container locator that the route-load fixture needs.
public class SettingsIndexPage : PageBase
{
    public SettingsIndexPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-index-page");

    public async Task<SettingsIndexPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsIndexPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/?$"));
        }

        return this;
    }
}
