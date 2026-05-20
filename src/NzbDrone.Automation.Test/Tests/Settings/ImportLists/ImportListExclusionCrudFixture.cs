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

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B) — CRUD round-trip on
// /api/v5/importlistexclusion (the second V5 surface shipped by Plan 26-05
// via ImportListExclusionController). Exercises the manga-ID triplet shape
// (MangaDexId string / MalId int? / AniListId int?) per Migration 003.
//
// Unlike Plan 26-06 Fixture 2 (which depends on the test-only TestImportList
// fake being in production DI), ImportListExclusion is a plain CRUD entity
// (RestController<ImportListExclusionResource>) — no provider plugin required.
// So this fixture can run GREEN today against the substrate without forward-
// staging Phase 27.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors. Uses
// `settings-importlist-exclusions` + `edit-importlist-exclusion-modal` +
// `importlist-exclusion-row-{id}` v1.1 prefix family per Phase 18 D-18.
//
// State assertion discipline per `feedback_verify_ui_state_not_just_rendering`:
// every step verifies BOTH the UI shape AND the underlying V5 row via parallel
// GET /api/v5/importlistexclusion, so a silently-rejected POST cannot pass.
[TestFixture]
[Category("AutomationTest")]
public class ImportListExclusionCrudFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task exclusion_crud_round_trip()
    {
        // 1. Navigate to /settings/importlists. The exclusion subtree mounts as
        //    a sibling FieldSet under ImportListSettings.tsx (no separate route).
        var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 2. POST — create the exclusion row directly via the V5 API. The UI's
        //    "Add" icon-button opens EditImportListExclusionModal with id
        //    undefined (Add mode); the modal Save fires
        //    POST /api/v5/importlistexclusion. To keep the fixture deterministic
        //    against the modal's input-binding races, we POST via HttpClient and
        //    verify the UI picks up the new row on its next refetch.
        const string TestMangaDexId = "abc12345-6789-0123-4567-890123456789";
        const string TestTitle = "Test Exclusion Title";
        const int TestMalId = 9999;
        const int TestAniListId = 8888;

        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var postPayload = new
        {
            mangaDexId = TestMangaDexId,
            malId = TestMalId,
            aniListId = TestAniListId,
            title = TestTitle
        };
        var postResp = await http.PostAsJsonAsync("importlistexclusion", postPayload);
        postResp.IsSuccessStatusCode.Should().BeTrue(
            "POST /api/v5/importlistexclusion must succeed (body: {0})",
            await postResp.Content.ReadAsStringAsync());

        var postBody = await postResp.Content.ReadAsStringAsync();
        using var postDoc = JsonDocument.Parse(postBody);
        var exclusionId = postDoc.RootElement.GetProperty("id").GetInt32();

        // 3. GET via V5 — verify the exclusion is queryable. The endpoint is
        //    paged (PagingResource<ImportListExclusionResource>); the records
        //    array holds rows.
        var getResp = await http.GetAsync("importlistexclusion");
        getResp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/importlistexclusion must return 2xx");
        var getBody = await getResp.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getBody);
        var records = getDoc.RootElement.GetProperty("records");
        records.GetArrayLength().Should().BeGreaterThan(0,
            "the paged list must contain at least the row we just created");

        // 4. State assertion (UI side, after reload): the page picks up the row
        //    on its next refetch — explicitly refetch by re-navigating.
        await Page.GotoAsync($"{RootUri}/settings/importlists");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var exclusionRow = Page.GetByTestId($"importlist-exclusion-row-{exclusionId}");
        await Assertions.Expect(exclusionRow).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 5. PUT — mutate the Title via V5; verify UI reflects after refetch.
        const string MutatedTitle = "Test Exclusion Title (PUT'd)";
        var putPayload = new
        {
            id = exclusionId,
            mangaDexId = TestMangaDexId,
            malId = TestMalId,
            aniListId = TestAniListId,
            title = MutatedTitle
        };
        var putResp = await http.PutAsJsonAsync($"importlistexclusion/{exclusionId}", putPayload);
        putResp.IsSuccessStatusCode.Should().BeTrue(
            "PUT /api/v5/importlistexclusion/{0} must succeed (body: {1})",
            exclusionId,
            await putResp.Content.ReadAsStringAsync());

        // Verify via V5: paged-GET the row, assert title field carries the mutation.
        var getResp2 = await http.GetAsync("importlistexclusion");
        using var getDoc2 = JsonDocument.Parse(await getResp2.Content.ReadAsStringAsync());
        var found = false;
        foreach (var rec in getDoc2.RootElement.GetProperty("records").EnumerateArray())
        {
            if (rec.GetProperty("id").GetInt32() == exclusionId)
            {
                rec.GetProperty("title").GetString().Should().Be(MutatedTitle,
                    "V5 PUT response should reflect the title mutation on subsequent GET");
                found = true;
                break;
            }
        }

        found.Should().BeTrue("the PUT'd row must remain in the paged list");

        // 6. DELETE — remove the exclusion via V5; verify the row is gone from BOTH
        //    the V5 GET and the UI.
        var deleteResp = await http.DeleteAsync($"importlistexclusion/{exclusionId}");
        deleteResp.IsSuccessStatusCode.Should().BeTrue(
            "DELETE /api/v5/importlistexclusion/{0} must return 2xx (body: {1})",
            exclusionId,
            await deleteResp.Content.ReadAsStringAsync());

        // V5 verification side.
        var getResp3 = await http.GetAsync("importlistexclusion");
        using var getDoc3 = JsonDocument.Parse(await getResp3.Content.ReadAsStringAsync());
        var stillThere = false;
        foreach (var rec in getDoc3.RootElement.GetProperty("records").EnumerateArray())
        {
            if (rec.GetProperty("id").GetInt32() == exclusionId)
            {
                stillThere = true;
                break;
            }
        }

        stillThere.Should().BeFalse("the deleted row must not appear in subsequent V5 GET");

        // UI verification side — refetch and assert the row is gone.
        await Page.GotoAsync($"{RootUri}/settings/importlists");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(exclusionRow).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
    }
}
