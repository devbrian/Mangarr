using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/connect PageObject (route slug 'connect' is Sonarr's
// historic name for the Notifications cluster; the page title is "Notifications"). No
// page-level Save button exists — SettingsToolbar.showSave={false} — per Task 1 note;
// per-provider rows save inline via their own modal flows.
public class SettingsNotificationsPage : PageBase
{
    public SettingsNotificationsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-notifications-page");

    public async Task<SettingsNotificationsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/connect");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsNotificationsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/connect$"));
        }

        return this;
    }
}
