using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — RootFolders PageObject. Mangarr's RootFolders live on the
// /settings/mediamanagement route (no separate /settings/rootfolders slug exists; see
// frontend/src/App/AppRoutes.tsx + frontend/src/Settings/MediaManagement/MediaManagement
// .tsx). The page-level Save is the global SettingsToolbar Save button.
public class SettingsRootFoldersPage : PageBase
{
    public SettingsRootFoldersPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-root-folders-page");
    public ILocator SaveButton    => Page.GetByTestId("settings-save-button");
    public ILocator AddButton     => Page.GetByTestId("settings-root-folders-add-button");

    public async Task<SettingsRootFoldersPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/mediamanagement");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsRootFoldersPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/mediamanagement$"));
        }

        return this;
    }
}
