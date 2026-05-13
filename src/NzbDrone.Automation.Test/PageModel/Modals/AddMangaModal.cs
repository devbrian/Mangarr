using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddManga side-panel modal (AddNewMangaModalContent.tsx). Owned by Phase 18 Plan-04.
/// Used by AddMangaFlow.AddByMangaDexIdAsync as the final-step modal where the user
/// confirms an add; returns the resulting MangaDetailsPage after the post-add navigation.
/// </summary>
public class AddMangaModal : PageBase
{
    public AddMangaModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot    => Page.GetByTestId("add-manga-modal");
    public ILocator AddButton    => Page.GetByTestId("add-manga-modal-add-button");
    public ILocator CancelButton => Page.GetByTestId("add-manga-modal-cancel-button");

    /// <summary>
    /// Click the modal's Add button and wait for the resulting navigation to
    /// /manga/{slug}. Returns the MangaDetailsPage object for chaining.
    /// </summary>
    public async Task<MangaDetailsPage> ConfirmAddAsync()
    {
        await AddButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"));
        return new MangaDetailsPage(Page);
    }
}
