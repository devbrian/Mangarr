using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/general PageObject. SettingsSaveRoundTripFixture
// (UI-07) toggles the UrlBase field; that input keeps its name="urlBase" attribute
// and is selected via Page.Locator("input[name='urlBase']") since the TextInput
// wrapper data-testid passthrough is owned by Plan-04. UrlBase chosen because it is
// a free-text TextInput visible by default (NOT gated by showAdvancedSettings); the
// instanceName field is advanced-only and would require a separate toggle.
public class SettingsGeneralPage : PageBase
{
    public SettingsGeneralPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-general-page");
    public ILocator SaveButton    => Page.GetByTestId("settings-save-button");

    /// <summary>
    /// UrlBase free-text input, non-advanced, safe to mutate. Used by
    /// SettingsSaveRoundTripFixture as the UI-07 round-trip target.
    /// </summary>
    public ILocator UrlBaseInput  => Page.Locator("input[name='urlBase']");

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
