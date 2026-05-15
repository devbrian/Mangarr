using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings/tags PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings/tags | Tag settings page renders | 🟢`.
// testid `settings-tags-page` added inline to frontend/src/Settings/Tags/TagSettings.tsx
// in the same commit (Phase 19 D-08 fix-inline-when-contained precedent).
public class SettingsTagsPage : PageBase
{
    public SettingsTagsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-tags-page");

    public async Task<SettingsTagsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/tags");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsTagsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/tags$"));
        }

        return this;
    }
}
