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
    ///
    /// Issue #102 fix: arm the URL wait BEFORE the click triggers the
    /// navigation. The previous shape (`await click; await WaitForURL`)
    /// raced — in cassette-replay / fast-POST mode the navigation event
    /// fired before the wait was armed and every AddManga flow fixture
    /// timed out at this line. RunAndWaitForUrlAsync wraps both sides
    /// of the navigation in a single atomic wait.
    /// </summary>
    public async Task<MangaDetailsPage> ConfirmAddAsync()
    {
        // Arm the URL wait BEFORE triggering the click. WaitForURLAsync returns a
        // hot Task immediately; Task.WhenAll then runs the click in parallel with
        // the wait so the navigation event can't fire before the watcher exists.
        await Task.WhenAll(
            Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$")),
            AddButton.ClickAsync());
        return new MangaDetailsPage(Page);
    }
}
