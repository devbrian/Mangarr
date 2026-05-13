using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class PageBase
{
    protected readonly IPage Page;

    public PageBase(IPage page)
    {
        Page = page;
    }

    // Phase 17.3 swept TV-shape nav-icon names; Mangarr ships these six:
    public ILocator MangaNavIcon       => Page.GetByTestId("nav-manga");
    public ILocator CalendarNavIcon    => Page.GetByTestId("nav-calendar");
    public ILocator ActivityNavIcon    => Page.GetByTestId("nav-activity");
    public ILocator WantedNavIcon      => Page.GetByTestId("nav-wanted");
    public ILocator SettingsNavIcon    => Page.GetByTestId("nav-settings");
    public ILocator SystemNavIcon      => Page.GetByTestId("nav-system");

    public async Task<PageBase> WaitForNoSpinnerAsync(int timeoutMs = 30_000)
    {
        await Page.GetByTestId("loading-spinner")
                  .WaitForAsync(new LocatorWaitForOptions
                  {
                      State = WaitForSelectorState.Hidden,
                      Timeout = timeoutMs
                  });
        return this; // D-17 fluent return-this
    }
}
