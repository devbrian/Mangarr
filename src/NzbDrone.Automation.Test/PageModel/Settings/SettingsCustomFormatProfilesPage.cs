using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings/customformatprofiles PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings/customformatprofiles | CustomFormatProfile settings page renders | 🟢`.
// Manga-canonical sibling per Phase 7 D-05 (Phase 5 Plan 05-03 entity); the route renders
// frontend/src/Settings/Profiles/CustomFormatProfile/CustomFormatProfileSettings.tsx.
// testid `settings-customformatprofiles-page` added inline to that component in the same
// commit (Phase 19 D-08 fix-inline-when-contained precedent).
public class SettingsCustomFormatProfilesPage : PageBase
{
    public SettingsCustomFormatProfilesPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-customformatprofiles-page");

    public async Task<SettingsCustomFormatProfilesPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/customformatprofiles");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsCustomFormatProfilesPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/customformatprofiles$"));
        }

        return this;
    }
}
