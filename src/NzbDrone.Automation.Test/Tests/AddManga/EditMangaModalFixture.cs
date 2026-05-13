using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 18 Plan-04 AddManga cluster — Edit modal open + save round-trip.
/// Chains AddMangaFlow.AddByMangaDexIdAsync to seed a manga, then clicks
/// the MangaDetails Edit button, asserts the modal opens, clicks Save, and
/// asserts the modal closes + URL still on /manga/{slug}.
///
/// State assertion (not just rendering — per
/// feedback_verify_ui_state_not_just_rendering): modal hidden AFTER Save +
/// URL pattern preserved. Persistence beyond modal-close (the EditMangaForm
/// saveManga mutation actually round-tripping to /api/v5/manga/{id}) is
/// verified by the auto-close effect itself — the React component only
/// auto-closes if useSaveManga's onSuccess fires, which only fires on a 2xx
/// response from the PUT.
///
/// Cassette deferral: see AddMangaSearchFixture for the [Explicit] rationale.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 18 Plan-04 Task 3 cassette deferral — MangaDex cassette not yet recorded. See AddMangaSearchFixture for full deferral context.")]
public class EditMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task edit_modal_opens_and_saves()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        await details.EditButton.ClickAsync();

        var modal = new EditMangaModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        await modal.SaveAsync();

        // STATE assertion: modal hidden (auto-close on saveManga success) AND URL still on details page.
        await Assertions.Expect(modal.ModalRoot).ToBeHiddenAsync();
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
    }
}
