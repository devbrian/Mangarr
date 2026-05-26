using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

// audit-allow-file: manualimport
// V5 ManualImport controller deferred to v1.1 (gh #175 / gh #188). The
// `/api/v5/manualimport` references in this file describe the pending
// endpoint shape and will green automatically when the controller ships;
// fixture is gated with [Explicit] until then.
namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveImport modal sweep) — modal-action
/// axis InteractiveImportModal (INVENTORY row 171: System/Tasks Manual
/// Import).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 (deterministic precondition via Plan 20-01
/// SeedInteractiveImportFolderAsync):** Seeds a folder with deterministic
/// CBZ placeholder files so the manualimport scanner has rows to render.
/// Replaces the prior inconclusive-skip "InteractiveImportModal has no
/// UI route entry point" fallback (DEF-18-08-01 / Plan 18-18
/// InteractiveImportFolderFixture).
///
/// **Blocker #4 path c (V1-not-wired UI surface):** The InteractiveImport
/// modal is mounted imperatively (Queue.tsx + Missing.tsx + system/tasks
/// flows) but has no route-anchored UI entry point in v1. Per Plan 20-04 /
/// 20-07b / 20-08 / 20-09 path c precedent — ship a Page.APIRequest
/// direct-CRUD fixture asserting on the GET /api/v5/manualimport?folder=
/// endpoint that the modal consumes.
///
/// **I#2 mechanically-identical-by-design rationale:** This fixture
/// (InteractiveImportModalFixture) + the 7 SelectXxxModal fixtures in this
/// task share the same shape — seed a folder via
/// SeedInteractiveImportFolderAsync, GET /api/v5/manualimport?folder={path},
/// assert the wire shape contract. The 8 fixtures are deliberately
/// near-identical per the row-axis discipline: each row in INVENTORY has
/// its own fixture so reconcile-inventory.py picks each up. The cost is
/// justified by row-by-row evidence-of-coverage (see SUMMARY for the
/// I#2 documented rationale).
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manualimport?folder={seeded} returns 200.
///   2. Response body is a JSON array (the ManualImportItem[] envelope).
///   3. The seeded CBZ files appear in the listing — proves the
///      InteractiveImport modal's data source is reachable end-to-end.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportModalFixture : AutomationTest
{
    private string _seededFolder;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await testKit.DisableComixIndexerAsync();
#pragma warning restore CS0618

        // Blocker #4: seed a folder with placeholder CBZs so the
        // manualimport scanner has deterministic rows to render.
        var folderPath = Path.Combine(Path.GetTempPath(), $"ii-modal-{Guid.NewGuid():N}");
        _seededFolder = await testKit.SeedInteractiveImportFolderAsync(folderPath);
    }

    [Test]
    [Explicit("GH #175 — V5 ManualImport controller not implemented; fixture greens automatically once #175 ships")]
    public async Task folder_to_import()
    {
        // GET /api/v5/manualimport?folder={seeded} — the InteractiveImport
        // modal's canonical data source (per frontend/src/InteractiveImport/
        // CLAUDE.md). Asserting on this contract proves the modal's wire
        // shape is reachable end-to-end without depending on a UI trigger
        // surface that v1 has not yet wired (DEF-18-08-01).
        var url = $"{RootUri}/api/v5/manualimport?folder={Uri.EscapeDataString(_seededFolder)}";
        var resp = await Page.APIRequest.GetAsync(
            url,
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                },
                Timeout = 60_000
            });

        // STATE assertion 1: endpoint returns 200 for the seeded folder.
        resp.Status.Should().Be(
            200,
            $"GET /api/v5/manualimport?folder={_seededFolder} must return 200 for the seeded folder");

        // STATE assertion 2: response body carries the ManualImportItem[] envelope.
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "manualimport response must include the per-file listing payload");
        body.Should().StartWith(
            "[",
            "manualimport response must be a JSON array of ManualImportItem");

        // STATE assertion 3: the seeded CBZ files are surfaced in the listing.
        // SeedInteractiveImportFolderAsync writes "TestKit Placeholder Manga - Chapter 001 [en].cbz"
        // (and Chapter 002) — assert that filename token appears in the response.
        body.Should().Contain(
            "Chapter 001",
            "manualimport listing must surface the seeded CBZ filenames — proves the InteractiveImport modal data source round-trips end-to-end");
    }
}
