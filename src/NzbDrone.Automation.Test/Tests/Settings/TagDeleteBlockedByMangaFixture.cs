using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 22 Plan 22-06 — TAG-03 D-06 Manga consumer leg.
///
/// Pins the TagInUseValidator (Plan 22-04) DELETE-path 400 gate when a tag
/// is held by a Manga. Sonarr-canon mirrors the joined-consumer-summary error
/// format; Mangarr v1.1 implements it as a Mangarr-only enhancement (sibling
/// Mangarr/Sonarr v5-develop has NO TagInUseValidator.cs — verified via
/// `git cat-file -e v5-develop:src/NzbDrone.Core/Validation/TagInUseValidator.cs`
/// returning "path does not exist" — see DIVERGENCE.md Phase 22 entry).
///
/// Surface coverage (TEST-V11-01 INVENTORY row, staged for Plan 22-06 roll-up):
///   axis: v5-endpoint
///   id:   DELETE /api/v5/tag/{id}
///   surface: Settings/Tags TagInUseValidator path (Manga consumer)
///   covering-test: this fixture (delete_blocked_when_in_use_by_manga)
///
/// Analog: TagDetailPanelFixture.cs (PRSmoke / OneTimeSetUp seed + APIRequest
/// status + body assertion). Pattern: PATTERNS.md lines 244..265 verbatim.
///
/// STATE assertions per feedback_verify_ui_state_not_just_rendering:
///   1. DELETE response status == 400 (validator-driven 4xx, not a 500 / no-op)
///   2. response body contains literal "1 manga" (D-06 frontend label —
///      ConnectionResource convention is "1 connection"; manga peer is
///      lowercase "1 manga")
///   3. subsequent GET /api/v5/tag/{tagId} returns 200 — the tag still exists
///      because the DELETE was rejected PRE-SIDE-EFFECT (validator runs before
///      `_tagService.Delete(id)` in TagController.DeleteTag per Plan 22-04
///      Pattern B explicit pre-check wiring).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagDeleteBlockedByMangaFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    private int _tagId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        // 1. Seed a manga via the canonical UI flow (cassette-replayed under
        //    default offline mode; live under LiveService matrix). This places
        //    `Komi Can't Communicate` in the library with a known mangaId
        //    available through the post-add API listing.
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // 2. Seed a tag with a per-run-unique label (avoids cross-run cache
        //    collision; label-length capped at 24 chars to keep the human-
        //    readable suffix within the validator's regex `^[a-z0-9-]+$`).
        var label = $"phase-22-block-mng-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        _tagId = await tk.SeedTagAsync(label);
        _tagId.Should().BeGreaterThan(0);

        // 3. Attach the tag to the manga via GET-mutate-PUT (the MangaController
        //    PUT path accepts the full resource body; we mutate only the `tags`
        //    field and round-trip the rest). The TestKit does not expose a
        //    dedicated tag-attach helper today; the inline pattern follows
        //    PATTERNS.md §Step 2 ("attach via PUT /api/v5/manga/{id}").
        var listResp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        listResp.Status.Should().Be(200);
        var listBody = await listResp.TextAsync();

        using var doc = JsonDocument.Parse(listBody);
        var mangaArray = doc.RootElement;
        mangaArray.GetArrayLength().Should().BeGreaterThan(0,
            "AddMangaFlow.AddByMangaBakaIdAsync should have produced at least one manga row");
        var first = mangaArray[0];
        var mangaId = first.GetProperty("id").GetInt32();

        // Re-serialize the manga resource with the `tags` field overwritten.
        // Use a System.Text.Json round-trip so all the existing fields are
        // preserved; only `tags` is mutated to the seeded id.
        var rawMangaText = first.GetRawText();
        var mangaNode = JsonNode.Parse(rawMangaText);
        mangaNode["tags"] = new JsonArray(_tagId);
        var updatedMangaJson = mangaNode.ToJsonString();

        var putResp = await Page.APIRequest.FetchAsync(
            $"{RootUri}/api/v5/manga/{mangaId}",
            new APIRequestContextOptions
            {
                Method = "PUT",
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey,
                    ["Content-Type"] = "application/json"
                },
                DataString = updatedMangaJson
            });
        var putBody = await putResp.TextAsync();
        new[] { 200, 202, 204 }.Should().Contain(putResp.Status,
            $"PUT /api/v5/manga/{{id}} should accept the tag-attach update; observed: {putResp.Status} — body: {putBody}");
    }

    [Test]
    public async Task delete_blocked_when_in_use_by_manga()
    {
        // STATE assertion #1 — DELETE returns 400 (validator path engaged).
        var deleteResp = await Page.APIRequest.DeleteAsync(
            $"{RootUri}/api/v5/tag/{_tagId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        deleteResp.Status.Should().Be(400);

        // STATE assertion #2 — error body names the consumer cohort.
        // D-06 frontend label per RESEARCH.md §Sonarr Canon §Error message
        // format: joined-consumer-summary "{N} manga" (lowercase, no trailing
        // "(s)"). The TagInUseValidator (Plan 22-04) emits this string.
        var deleteBody = await deleteResp.TextAsync();
        deleteBody.Should().Contain("1 manga");

        // STATE assertion #3 — pre-side-effect: the tag still exists on the
        // backend (Pattern B explicit-pre-check rejected the DELETE before
        // `_tagService.Delete(id)` ran).
        var getResp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/tag/{_tagId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        getResp.Status.Should().Be(200);
    }
}
