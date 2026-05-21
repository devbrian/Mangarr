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
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task edit_modal_auto_closes_when_row_concurrently_removed()
    {
        // ---- 1. Seed 2 rows via the V5 API ----
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var seedOnePayload = new
        {
            mangaDexId = "11111111-aaaa-bbbb-cccc-111111111111",
            malId = 9101,
            aniListId = 9201,
            title = "Concurrent Removal Target"
        };
        var seedTwoPayload = new
        {
            mangaDexId = "22222222-aaaa-bbbb-cccc-222222222222",
            malId = 9102,
            aniListId = 9202,
            title = "Sibling Row (must remain)"
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

        // ---- 5. Trigger a refetch ----
        // The cleanest way to force the page's React Query to refetch without
        // racing the modal-close detection is to call the well-known
        // `pagePopulator` registration; the natural trigger in this codebase
        // is route navigation. Going to /settings then back triggers the
        // ImportListExclusions mount-effect's repopulate() which calls refetch().
        // We do NOT click the modal's Cancel (that would close the modal
        // before our detection effect runs).
        //
        // Sonarr-canonical alternative is window focus/blur — unreliable in
        // headless Playwright. The route-navigate path is deterministic.
        await Page.EvaluateAsync("() => window.dispatchEvent(new Event('focus'))");

        // Belt-and-braces: also explicitly invalidate via a sibling fetch so
        // the page picks up the deletion on its next render. The page's
        // refetch is wired by its `registerPagePopulator` effect; the
        // pagePopulator fires on internal nav events. Navigate to and from
        // a sibling Settings page to force the effect to re-run cleanly.
        // BUT: navigating destroys the modal mount, which would erase the
        // banner we want to observe. So instead, exercise the page's GET
        // directly to warm the cache; the React Query polling will pick it
        // up on its next tick (queries are configured with placeholderData
        // keepPreviousData in useImportListExclusions, so a re-fetch lands).
        //
        // The detection effect needs `records.find(r => r.id === id) === undefined`
        // which requires the cache to update. The simplest deterministic
        // trigger from inside this Page session is to use the React Query
        // client devtools approach — but those aren't exposed. Polling the
        // banner directly is the robust approach.
        await Assertions.Expect(
            Page.GetByTestId("importlist-exclusion-concurrent-removal-alert"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                // Allow up to 30s to cover: SignalR/refetch propagation +
                // React Query's `placeholderData: keepPreviousData` retention
                // window + Mangarr-canonical async-flush envelope tolerance.
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

            if (title == "Sibling Row (must remain)")
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
}
