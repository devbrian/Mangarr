using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// DeleteManga single-manga modal (DeleteMangaModalContent.tsx). Owned by Phase 18 Plan-04.
/// Opens from MangaDetailsPage.DeleteButton; on confirm, the React component closes
/// itself via the auto-close effect + MangaDetailsPage.tsx's redirect-on-vanish effect
/// navigates back to /manga (the index). Returns MangaIndexPage for the post-delete
/// state assertion (delete-removes-manga test).
/// </summary>
public class DeleteMangaModal : PageBase
{
    public DeleteMangaModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot     => Page.GetByTestId("delete-manga-modal");
    public ILocator ConfirmButton => Page.GetByTestId("delete-manga-modal-confirm-button");
    public ILocator CancelButton  => Page.GetByTestId("delete-manga-modal-cancel-button");

    /// <summary>
    /// Click Confirm Delete and wait for the post-delete navigation back to the manga
    /// index (Mangarr v1 routes the index at `/`, NOT `/manga` — see frontend/src/App/AppRoutes.tsx).
    /// </summary>
    public async Task<MangaIndexPage> ConfirmDeleteAsync()
    {
        await ConfirmButton.ClickAsync();

        // After delete, MangaDetailsPage redirects to / when the React Query cache
        // no longer contains the manga. Wait for the root URL.
        await Page.WaitForURLAsync(new Regex(@"^[^?#]*/(\?.*)?$"));
        return new MangaIndexPage(Page);
    }
}
