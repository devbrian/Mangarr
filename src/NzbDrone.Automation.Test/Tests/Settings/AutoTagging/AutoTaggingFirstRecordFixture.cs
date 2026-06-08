using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — LOAD-BEARING AT-09 First-record-creation smoke
/// fixture. Greens INVENTORY req row `AT-09 | First-record-creation smoke
/// (fresh DB → AutoTagging rule with DemographicSpec=Shonen → matching manga
/// → tag applied + survives non-match + opt-in remove)`.
///
/// Captures all 4 behavior axes from 24-CONTEXT.md §specifics lines 631-639:
///   STEP 1 — Fresh DB: create AutoTagging rule with DemographicSpecification
///            value=Shonen (MangaDemographic.Shonen=1) + Tags=[seedTagId] via
///            POST /api/v5/autotagging.
///   STEP 2 — Add Komi Can't Communicate via AddMangaFlow.AddByMangaBakaIdAsync
///            (MangaDex publicationDemographic=shounen, maps to Shonen=1 per
///            Plan 24-02 MapDemographic helper). Plan note (CONTEXT.md line
///            634) called for Solo Leveling but Komi is the canonical
///            cassette-replayed test anchor (KnownMangaBakaId on
///            AddMangaFlow.cs:36) — same Shonen demographic, deterministic
///            and network-OFF per Phase 18 D-06. The MangaAutoTaggingApplier
///            IHandle of MangaAddedEvent fires on the add and applies the
///            rule (D-06 — per-manga single-update path). Verify Komi
///            carries the seed tag.
///   STEP 3 — PUT the rule with DemographicSpec value=Josei (Komi no longer
///            matches). Verify Komi STILL has the seed tag — D-05
///            Sonarr-canonical default `RemoveTagsAutomatically=false` means
///            user-applied tags (and rule-applied tags from rules that
///            previously matched) are NOT auto-removed on rule-non-match.
///   STEP 4 — PUT the rule with removeTagsAutomatically=true. The
///            MangaAutoTaggingApplier IHandle of AutoTagsUpdatedEvent fires
///            on the rule save and re-evaluates the full library (D-06 —
///            retroactive eval on save). Verify Komi no longer has the seed
///            tag — Sonarr-canonical opt-in path: rule removes only the
///            tags THAT rule itself applied when the rule stops matching.
///
/// This fixture is the canonical AT-09 smoke per ROADMAP §Phase 24 SC #5 +
/// 24-CONTEXT.md §specifics. The unit-level decomposition lives across
/// AutoTaggingServiceFixture (algorithm) + MangaAutoTaggingApplierFixture
/// (event-binding) + TagInUseValidatorAutoTaggingFixture (Slot 8 flip);
/// this fixture is the LIVE end-to-end gate.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingFirstRecordFixture : AutomationTest
{
    [Test]
    public async Task full_recipe_4_step_at09()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var autoTaggingApi = $"{RootUri}/api/v5/autotagging";
        var mangaApi = $"{RootUri}/api/v5/manga";

        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagLabel = $"plan-24-05-at09-{Guid.NewGuid():N}".Substring(0, 28);
        var seedTagId = await tk.SeedTagAsync(tagLabel);
        seedTagId.Should().BeGreaterThan(0);

        // ===================================================================
        // STEP 1 — Create AutoTagging rule with DemographicSpec value=Shonen.
        // MangaDemographic.Shonen=1 per src/NzbDrone.Core/Manga/MangaDemographic.cs.
        // ===================================================================
        var ruleName = $"at09-{Guid.NewGuid():N}".Substring(0, 18);
        var createResp = await Page.APIRequest.PostAsync(autoTaggingApi, new APIRequestContextOptions
        {
            DataObject = new
            {
                name = ruleName,
                removeTagsAutomatically = false,
                tags = new[] { seedTagId },
                specifications = new[]
                {
                    new
                    {
                        name = "Shonen Demographic",
                        implementation = "DemographicSpecification",
                        implementationName = "Demographic",
                        negate = false,
                        required = false,
                        fields = new[]
                        {
                            new { name = "value", value = 1 } // Shonen
                        }
                    }
                }
            }
        });
        createResp.Status.Should().BeInRange(
            200,
            299,
            $"STEP 1 POST rule must succeed; body: {await createResp.TextAsync()}");
        var ruleId = (await createResp.JsonAsync())!.Value.GetProperty("id").GetInt32();

        // ===================================================================
        // STEP 2 — Add Komi Can't Communicate (MangaDex publicationDemographic=
        // shounen → MangaDemographic.Shonen=1). MangaAutoTaggingApplier's
        // IHandle<MangaAddedEvent> fires and applies the rule. Verify Komi
        // carries the seed tag.
        // ===================================================================
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, AddMangaFlow.KnownMangaBakaId);

        // Allow the IHandle<MangaAddedEvent> dispatch to settle.
        await WaitForKomiToCarryTagAsync(
            mangaApi,
            seedTagId,
            expectedToCarry: true,
            stepLabel: "STEP 2 — MangaAddedEvent path: Komi (Shonen) MUST carry the seed tag");

        // ===================================================================
        // STEP 3 — PUT the rule with DemographicSpec value=Josei (Josei=4).
        // Komi (Shonen=1) no longer matches. Sonarr-canonical D-05:
        // RemoveTagsAutomatically=false default keeps user-applied AND rule-
        // applied tags sticky. Verify Komi STILL has the seed tag.
        // ===================================================================
        var step3PutResp = await Page.APIRequest.PutAsync($"{autoTaggingApi}/{ruleId}", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = ruleId,
                name = ruleName,
                removeTagsAutomatically = false,
                tags = new[] { seedTagId },
                specifications = new[]
                {
                    new
                    {
                        name = "Josei Demographic",
                        implementation = "DemographicSpecification",
                        implementationName = "Demographic",
                        negate = false,
                        required = false,
                        fields = new[]
                        {
                            new { name = "value", value = 4 } // Josei
                        }
                    }
                }
            }
        });
        step3PutResp.Status.Should().BeInRange(
            200,
            299,
            $"STEP 3 PUT must succeed; body: {await step3PutResp.TextAsync()}");

        // Allow the IHandle<AutoTagsUpdatedEvent> applier re-eval to settle.
        // Per D-05 + Sonarr-canonical algorithm: with RemoveTagsAutomatically=
        // false, the applier MUST leave the tag in place even though Komi no
        // longer matches the (now Josei-spec'd) rule.
        await WaitForKomiToCarryTagAsync(
            mangaApi,
            seedTagId,
            expectedToCarry: true,
            stepLabel: "STEP 3 — RemoveTagsAutomatically=false default: Komi (no longer matching) MUST STILL carry the seed tag");

        // ===================================================================
        // STEP 4 — PUT rule with removeTagsAutomatically=true. The applier
        // re-evaluates on AutoTagsUpdatedEvent + with the opt-in flag set
        // removes the rule's own tags from manga the rule no longer matches.
        // Komi (Shonen, no longer matching Josei spec) should now LOSE the
        // seed tag.
        // ===================================================================
        var step4PutResp = await Page.APIRequest.PutAsync($"{autoTaggingApi}/{ruleId}", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = ruleId,
                name = ruleName,
                removeTagsAutomatically = true,
                tags = new[] { seedTagId },
                specifications = new[]
                {
                    new
                    {
                        name = "Josei Demographic",
                        implementation = "DemographicSpecification",
                        implementationName = "Demographic",
                        negate = false,
                        required = false,
                        fields = new[]
                        {
                            new { name = "value", value = 4 } // Josei (still no match for Komi=Shonen)
                        }
                    }
                }
            }
        });
        step4PutResp.Status.Should().BeInRange(
            200,
            299,
            $"STEP 4 PUT must succeed; body: {await step4PutResp.TextAsync()}");

        await WaitForKomiToCarryTagAsync(
            mangaApi,
            seedTagId,
            expectedToCarry: false,
            stepLabel: "STEP 4 — Sonarr-canonical opt-in path with RemoveTagsAutomatically=true: Komi MUST LOSE the seed tag");
    }

    /// <summary>
    /// Polls GET /api/v5/manga, finds Komi by MangaBakaId, asserts whether its
    /// `tags` array contains the seed tag. Bounded retry loop (≤10s) avoids
    /// flake when the applier's IHandle dispatch lands on a different thread
    /// than the API request; do NOT use an unbounded until-loop per memory
    /// note feedback_bash_until_loop_pitfall (PID-leak risk).
    /// </summary>
    private async Task WaitForKomiToCarryTagAsync(
        string mangaApi,
        int seedTagId,
        bool expectedToCarry,
        string stepLabel)
    {
        var lastCarried = !expectedToCarry; // start opposite so the loop runs at least once
        for (var i = 0; i < 20; i++)
        {
            var listResp = await Page.APIRequest.GetAsync(mangaApi);
            listResp.Status.Should().Be(200);
            var root = (await listResp.JsonAsync())!.Value;

            var komi = root.EnumerateArray()
                .FirstOrDefault(e =>
                    e.TryGetProperty("mangaBakaId", out var mbId)
                    && mbId.ValueKind == global::System.Text.Json.JsonValueKind.Number
                    && mbId.GetInt32() == int.Parse(AddMangaFlow.KnownMangaBakaId));

            komi.ValueKind.Should().NotBe(global::System.Text.Json.JsonValueKind.Undefined,
                $"{stepLabel} — Komi MUST exist in the library (added via AddMangaFlow in STEP 2)");

            var tagsArr = komi.GetProperty("tags");
            tagsArr.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);
            lastCarried = tagsArr.EnumerateArray().Any(t => t.GetInt32() == seedTagId);

            if (lastCarried == expectedToCarry)
            {
                return;
            }

            await Task.Delay(500); // 20 iterations × 500ms = 10s bounded ceiling
        }

        lastCarried.Should().Be(
            expectedToCarry,
            $"{stepLabel}. After 10s of polling, the actual tag-carry state did not match the AT-09 expectation. " +
            $"Komi MangaBakaId={AddMangaFlow.KnownMangaBakaId}; seedTagId={seedTagId}.");
    }
}
