using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 AddManga modal sweep) — v5-endpoint axis
/// `POST /api/v5/manga` covering the AddManga modal Add button (INVENTORY
/// row 71).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy / write-path) maps
/// to PRSmoke per the mechanical row-axis rule. The POST is the canonical
/// "add a new manga" contract surface — every other AddManga UI fixture
/// implicitly exercises this endpoint, but reconcile-inventory.py keys row
/// detection on the exact filename, so this fixture exists to pin the
/// v5-endpoint row's `add_button_posts_manga` covering-test.
///
/// Distinct from AddNewMangaModalFixture (modal-action axis row 146; Nightly;
/// asserts the open → submit → close UI contract). This fixture asserts on
/// the endpoint URL + status + the wire-shape of the response body (id
/// emitted; mangaDexId persisted) — distinct from the modal-action axis
/// fixture which focuses on the modal-close + URL-transition contract.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. POST /api/v5/manga returns 2xx.
///   2. Response body wires an integer id (the new MangaId).
///   3. The seeded mangaDexId round-trips on the response payload.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AddMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task add_button_posts_manga()
    {
        // Arm response listener for POST /api/v5/manga BEFORE driving the
        // modal Confirm so the in-flight POST is not missed. Match the bare
        // manga collection POST, not the per-id PUT/DELETE.
        var addEndpointRegex = new Regex(@"/api/v5/manga(\?|$)");
        var postTask = Page.WaitForResponseAsync(
            r => addEndpointRegex.IsMatch(r.Url) && r.Request.Method == "POST",
            new() { Timeout = 60_000 });

        // Drive the full AddManga flow — the underlying call chain is the
        // canonical "user clicks Add Manga in the modal" path that fires the
        // POST. This proves the modal Add button wires to the v5 endpoint
        // end-to-end (rather than asserting on a synthetic Page.APIRequest
        // shape that bypasses the UI).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // STATE assertion 1: POST /api/v5/manga returned 2xx.
        var resp = await postTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "POST /api/v5/manga must return 2xx — the AddManga modal Add button is the canonical write-path");

        // STATE assertion 2: response body wires an integer id.
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "POST /api/v5/manga must return a MangaResource body with the new id");
        body.Should().Contain(
            "\"id\"",
            "POST /api/v5/manga response must carry an `id` field (MangaResource.Id)");

        // STATE assertion 3: the seeded mangaDexId round-trips on the response.
        // Confirms the wire-shape preserves the cross-source id input.
        body.Should().Contain(
            KnownMangaDexId,
            "POST /api/v5/manga response must echo the input mangaDexId field");
    }
}
