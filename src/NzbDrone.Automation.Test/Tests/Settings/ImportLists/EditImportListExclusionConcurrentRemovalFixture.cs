using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// GH-225 (Phase 27.1 REVIEW WR-02) — concurrent-removal regression coverage
// for the EditImportListExclusion modal. Reproduces the "row vanished
// mid-edit" race that previously caused the modal to silently rebind to a
// blank record (resulting in a blank-titled POST on Save), now blocked by:
//   1. The parent component (`ImportListExclusions.tsx`) detects the
//      disappearance transition (modal open + bound id + records refetched +
//      row gone) and auto-closes the modal while surfacing a WARNING Alert
//      with testid `importlist-exclusion-concurrent-removal-alert`.
//   2. The backend SharedValidator at `ImportListExclusionController.cs:41`
//      rejects an empty-Title POST/PUT with a 400 — defense-in-depth covered
//      by the companion unit-tier fixture `ImportListExclusionControllerValidatorFixture`.
//
// Test scenario:
//   1. Seed 2 ImportListExclusion rows via the V5 API.
//   2. Open the Edit modal for row 1 by clicking the row's Edit IconButton.
//   3. Concurrently `DELETE /api/v5/importlistexclusion/{id}` for the row
//      being edited (simulates another session deleting the row).
//   4. Trigger the React Query refetch by navigating to a sibling Settings
//      route then back (idempotent; the page's `useEffect`-driven pagePopulator
//      also fires on focus/visibility but cross-tab is unreliable in CI).
//   5. Assert the modal auto-closes (its testid no longer matches).
//   6. Assert the concurrent-removal Alert is visible with the expected text.
//   7. Assert no POST was emitted with an empty Title — verified by polling
//      the V5 endpoint and confirming no new empty-titled row landed.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
// Mirrors the seed → exercise → assert shape of `ImportListExclusionCrudFixture`
// (Phase 26 Plan 26-06 sibling fixture). NOT tagged `PRSmoke` so it runs in
// the broader nightly automation tier without bloating the PR smoke window.
[TestFixture]
[Category("AutomationTest")]
public class EditImportListExclusionConcurrentRemovalFixture : AutomationTest
{
    [Test]
    public async Task edit_modal_auto_closes_when_row_concurrently_removed()
    {
        // ---- 1. Seed 2 rows via the V5 API ----
        // CodeRabbit PR #235 P2: payloads use per-run-unique mangaDexId/title
        // suffixed with `Guid.NewGuid()` so reruns against any persisted DB
        // don't collide on the V5 backend's unique-id check; seeded IDs are
        // captured and removed in the finally block.
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var runTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var siblingTitle = $"Sibling Row (must remain) [{runTag}]";

        var seedOnePayload = new
        {
            mangaDexId = $"11111111-aaaa-bbbb-cccc-{runTag}{runTag[..4]}",
            malId = 9101,
            aniListId = 9201,
            title = $"Concurrent Removal Target [{runTag}]"
        };
        var seedTwoPayload = new
        {
            mangaDexId = $"22222222-aaaa-bbbb-cccc-{runTag}{runTag[..4]}",
            malId = 9102,
            aniListId = 9202,
            title = siblingTitle
        };

        var seedOneResp = await http.PostAsJsonAsync("importlistexclusion", seedOnePayload);
        seedOneResp.IsSuccessStatusCode.Should().BeTrue(
            "POST seed row 1 must succeed (body: {0})",
            await seedOneResp.Content.ReadAsStringAsync());
        using var seedOneDoc = JsonDocument.Parse(await seedOneResp.Content.ReadAsStringAsync());
        var targetRowId = seedOneDoc.RootElement.GetProperty("id").GetInt32();

        var seedTwoResp = await http.PostAsJsonAsync("importlistexclusion", seedTwoPayload);
        seedTwoResp.IsSuccessStatusCode.Should().BeTrue(
            "POST seed row 2 must succeed (body: {0})",
            await seedTwoResp.Content.ReadAsStringAsync());
        using var seedTwoDoc = JsonDocument.Parse(await seedTwoResp.Content.ReadAsStringAsync());
        var siblingRowId = seedTwoDoc.RootElement.GetProperty("id").GetInt32();

        var targetDeletedByConcurrentStep = false;

        try
        {
            // ---- 2. Navigate to /settings/importlists and confirm both rows render ----
            var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var targetRow = Page.GetByTestId($"settings-importlist-exclusion-row-{targetRowId}");
            await Assertions.Expect(targetRow).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // ---- 3. Open the Edit modal for the target row ----
            // The row has one Edit IconButton and one Delete IconButton in its
            // actions cell. Edit comes first (icons.EDIT before icons.REMOVE per
            // ImportListExclusionRow.tsx).
            var editIcon = targetRow.GetByRole(AriaRole.Button, new() { Name = "Edit" });
            await editIcon.ClickAsync();

            var editModal = Page.GetByTestId("edit-importlist-exclusion-modal");
            await Assertions.Expect(editModal).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            // ---- 4. Concurrently DELETE the row being edited ----
            // This simulates another session removing the row out from under us.
            // The mutation does NOT propagate via SignalR to the same browser
            // automatically; we still need a refetch trigger (step 5).
            var deleteResp = await http.DeleteAsync($"importlistexclusion/{targetRowId}");
            deleteResp.IsSuccessStatusCode.Should().BeTrue(
                "DELETE the row mid-edit must succeed (body: {0})",
                await deleteResp.Content.ReadAsStringAsync());
            targetDeletedByConcurrentStep = true;

            // ---- 5. Trigger a refetch ----
            // Codex PR #235 P2: prior approach `dispatchEvent(new Event('focus'))`
            // is a no-op in TanStack Query v5 (its focusManager subscribes to
            // `visibilitychange` only, not `focus`). Instead, reach into the
            // React Query client through the React fiber tree and call
            // `invalidateQueries({ queryKey: ['/importlistexclusion'] })` —
            // exactly what `useDeleteImportListExclusion.onSuccess` does, just
            // from a different (test-driven) entry point. This is deterministic,
            // does NOT navigate away (which would unmount the modal before our
            // detection effect runs), and does NOT depend on focus/visibility
            // heuristics that vary in headless Playwright.
            //
            // FRAGILE TECHNIQUE — VERSION PIN: the `Page.EvaluateAsync` snippet
            // below relies on private React internal keys
            // (`__reactContainer$<random>` / `__reactFiber$<random>`) and the
            // TanStack QueryClient duck-shape (`.invalidateQueries` +
            // `.getQueryCache`). Validated against:
            //   - React 18.3.1 (see `frontend/package.json`)
            //   - TanStack Query 5.61.0 (`@tanstack/react-query`, see
            //     `frontend/package.json`)
            // If either is upgraded across a MAJOR version (React 19, TanStack
            // 6, etc.), re-verify both the fiber-key prefixes and the
            // QueryClient method shape before assuming this fixture still
            // dispatches. The CodeRabbit PR #235 review (2026-05-21) flagged
            // this fragility; the version pin lives in this comment so the
            // next maintainer is forewarned.
            await Page.EvaluateAsync(@"
                async () => {
                    const root = document.getElementById('root') || document.body.firstElementChild;
                    if (!root) throw new Error('React root not found');
                    const rootKey = Object.keys(root).find(k =>
                        k.startsWith('__reactContainer$') || k.startsWith('__reactFiber$'));
                    if (!rootKey) throw new Error('React fiber root key not found');
                    let fiber = root[rootKey];
                    if (fiber && fiber.stateNode && fiber.stateNode.current) {
                        fiber = fiber.stateNode.current;
                    }
                    let qc = null, visited = 0;
                    const visit = (n) => {
                        if (!n || visited > 5000 || qc) return;
                        visited++;
                        if (n.memoizedProps) {
                            for (const k of Object.keys(n.memoizedProps)) {
                                const v = n.memoizedProps[k];
                                if (v && typeof v === 'object'
                                    && typeof v.invalidateQueries === 'function'
                                    && typeof v.getQueryCache === 'function') {
                                    qc = v; return;
                                }
                            }
                        }
                        if (n.child) visit(n.child);
                        if (n.sibling) visit(n.sibling);
                    };
                    visit(fiber);
                    if (!qc) throw new Error('TanStack QueryClient not located in React fiber tree');
                    await qc.invalidateQueries({ queryKey: ['/importlistexclusion'] });
                }");

            await Assertions.Expect(
                Page.GetByTestId("settings-importlist-exclusion-concurrent-removal-alert"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
                {
                    // Allow up to 30s for the refetch round-trip plus React
                    // commit flush. The Mangarr backend is local; the refetch
                    // itself returns in <100ms.
                    Timeout = 30_000
                });

            // ---- 6. The Edit modal should be auto-closed ----
            await Assertions.Expect(editModal).ToBeHiddenAsync(
                new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

            // ---- 7. No empty-titled POST landed ----
            // The FE guard prevents the POST; the BE validator is the
            // defense-in-depth. Either way, after this sequence, the V5 GET must
            // contain only the original sibling row — no extra blank-titled row.
            var listResp = await http.GetAsync("importlistexclusion");
            listResp.IsSuccessStatusCode.Should().BeTrue("GET after concurrent-removal sequence must return 2xx");
            using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            var records = listDoc.RootElement.GetProperty("records");

            var emptyTitleCount = 0;
            var siblingPresent = false;
            var targetStillPresent = false;
            foreach (var rec in records.EnumerateArray())
            {
                var title = rec.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                    ? titleProp.GetString()
                    : null;
                var id = rec.GetProperty("id").GetInt32();

                if (string.IsNullOrWhiteSpace(title))
                {
                    emptyTitleCount++;
                }

                if (id == targetRowId)
                {
                    targetStillPresent = true;
                }

                if (title == siblingTitle)
                {
                    siblingPresent = true;
                }
            }

            emptyTitleCount.Should().Be(0,
                "no empty-titled exclusion may have been POSTed — the FE auto-close MUST run before the user can hit Save on the rebound blank record");
            targetStillPresent.Should().BeFalse(
                "the concurrent DELETE should have removed the target row");
            siblingPresent.Should().BeTrue(
                "the unrelated sibling row must remain — only the targeted row was removed");
        }
        finally
        {
            // CodeRabbit PR #235 P2: ensure no seeded row survives the test.
            // The target row is normally deleted by the concurrent-removal step,
            // but if the test fails before that step runs we still want to
            // clean it up. Sibling row is always cleaned up. Both deletes are
            // best-effort (404 is fine if the row is already gone).
            if (!targetDeletedByConcurrentStep)
            {
                _ = await http.DeleteAsync($"importlistexclusion/{targetRowId}");
            }

            _ = await http.DeleteAsync($"importlistexclusion/{siblingRowId}");
        }
    }
}
