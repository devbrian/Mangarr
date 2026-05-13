using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditManga single-manga modal (EditMangaModalContent.tsx). Owned by Phase 18 Plan-04.
/// Opens from MangaDetailsPage.EditButton; on Save, the React component closes itself
/// via the auto-close effect once useSaveManga.onSuccess fires. State assertion:
/// modal hides + URL still on /manga/{slug}.
/// </summary>
public class EditMangaModal : PageBase
{
    public EditMangaModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot    => Page.GetByTestId("edit-manga-modal");
    public ILocator SaveButton   => Page.GetByTestId("edit-manga-modal-save-button");
    public ILocator CancelButton => Page.GetByTestId("edit-manga-modal-cancel-button");

    /// <summary>
    /// Click Save and wait for the modal to close (the React component auto-closes
    /// on save-success via the wasSaving/isSaving guard). Returns MangaDetailsPage
    /// because that's where the user lands after the modal dismisses.
    /// </summary>
    public async Task<MangaDetailsPage> SaveAsync()
    {
        await SaveButton.ClickAsync();
        await ModalRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        return new MangaDetailsPage(Page);
    }
}
