using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/general PageObject. SettingsSaveRoundTripFixture
// (UI-07) toggles the InstanceName field; that input keeps its name="instanceName"
// attribute and is selected via Page.Locator("input[name='instanceName']") since the
// TextInput wrapper data-testid passthrough is owned by Plan-04.
public class SettingsGeneralPage : PageBase
{
    public SettingsGeneralPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer    => Page.GetByTestId("settings-general-page");
    public ILocator SaveButton       => Page.GetByTestId("settings-save-button");
    public ILocator InstanceNameInput => Page.Locator("input[name='instanceName']");

    public async Task<SettingsGeneralPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/general");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsGeneralPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/general$"));
        }

        return this;
    }
}
