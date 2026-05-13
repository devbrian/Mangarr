using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 18 Plan-04 AddManga cluster — Tier 3 (cassette-replay) fixture.
/// Verifies the /add/manga search input renders a result row for a known
/// MangaDex ID. State assertion (per feedback_verify_ui_state_not_just_rendering):
/// inspects both row presence AND the per-row add-button visibility.
///
/// Cassette deferral (Plan-04 Task 3): the MangaDex cassette JSON is recorded
/// once against api.mangadex.org in Record mode, then committed alongside this
/// fixture. Until a cassette is recorded in this run, the fixture is marked
/// [Explicit] so it does not break the test suite under default CI replay mode.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
[Explicit("Phase 18 Plan-04 Task 3 cassette deferral — MangaDex cassette not yet recorded. Run with MANGARR_TEST_CASSETTE_MODE=Record (manual one-shot) to capture, then commit the resulting .json files under Fixtures/Cassettes/MangaDex/. Plan-10 CI wiring re-attempts when the cassette directory ships green.")]
public class AddMangaSearchFixture : AutomationTest
{
    // Komi Can't Communicate — long-running, stable popular manga; large enough
    // chapter list to exercise paginated chapter loads in Plan-05+ fixtures.
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task search_by_mangadex_id_shows_result_row()
    {
        var addPage = await new AddMangaPage(Page).OpenAsync(RootUri);

        await addPage.SearchInput.FillAsync(KnownMangaDexId);

        var resultRow = addPage.ResultRowByKey(KnownMangaDexId);
        await resultRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // STATE assertion: row visible + scoped add-button visible (not just
        // row container rendered — per feedback_verify_ui_state_not_just_rendering).
        await Assertions.Expect(resultRow).ToBeVisibleAsync();

        var addButton = resultRow.GetByTestId("add-manga-add-button");
        await Assertions.Expect(addButton).ToBeVisibleAsync();
    }
}
