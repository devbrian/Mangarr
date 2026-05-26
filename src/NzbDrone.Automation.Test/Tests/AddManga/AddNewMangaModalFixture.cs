using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 AddManga modal sweep) — modal-action axis
/// AddNewMangaModal (INVENTORY row 146).
///
/// Tier (D-04): Nightly default — modal-action axis (Add/Edit/Test/Delete
/// cycle write-path) per D-04 mechanical row-axis rule.
///
/// Drives the AddManga search → result-row Add → modal Confirm flow that
/// AddMangaFlow.AddByMangaDexIdAsync encapsulates. Distinct from
/// AddMangaFlowFixture (the end-to-end happy path PRSmoke) — this fixture is
/// pinned to the INVENTORY modal-action row's `open_submit_close` covering-test
/// to satisfy reconcile-inventory.py's row-pinning contract.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. POST /api/v5/manga returns 2xx.
///   2. AddManga modal closes after Confirm (auto-close on useAddManga.onSuccess).
///   3. Post-add navigation lands on /manga/{slug}.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AddNewMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task open_submit_close()
    {
        // Arm response listener for POST /api/v5/manga BEFORE the modal Confirm
        // click so the in-flight POST cannot be missed (mirrors Plan 20-09's
        // "armed listener BEFORE navigation" pattern). Match the bare manga
        // collection POST (the Add path), not the per-id PUT/DELETE
        // (`/api/v5/manga/{id}`) and not the lookup GET (`/api/v5/manga/lookup`).
        var addEndpointRegex = new Regex(@"/api/v5/manga(\?|$)");
        var postTask = Page.WaitForResponseAsync(
            r => addEndpointRegex.IsMatch(r.Url) && r.Request.Method == "POST",
            new() { Timeout = 60_000 });

        // Drive the full AddManga flow via the canonical D-08 helper. The flow
        // navigates to /add/manga, searches, clicks the row Add button, waits
        // for the modal Add button to be enabled, clicks Confirm, and asserts
        // the modal hides — which is exactly the open_submit_close contract.
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // STATE assertion 1: POST /api/v5/manga returned 2xx.
        var resp = await postTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "POST /api/v5/manga must succeed on modal Confirm (open_submit_close contract)");

        // STATE assertion 2: AddManga modal is hidden after Confirm (the flow
        // already waits for the modal to be hidden, but assert explicitly here
        // so the test name's `close` predicate is verified).
        var modal = new AddMangaModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeHiddenAsync();

        // STATE assertion 3: post-add navigation lands on /manga/{slug}.
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
        await Assertions.Expect(details.PageRoot).ToBeVisibleAsync();
    }
}
