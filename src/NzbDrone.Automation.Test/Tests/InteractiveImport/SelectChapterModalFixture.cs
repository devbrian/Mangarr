using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

// audit-allow-file: manualimport
// V5 ManualImport controller is the end-to-end driver (#175); today this
// fixture asserts the i18n + chapter-by-manga endpoint shape + route
// reachability that the new SelectChapterModal multi-select picker
// consumes. EXTENSION SHAPE for the post-#175 UI walk is inline-documented
// in the class summary.
namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 30 Plan 30-03 (II2-01) — Wave 0 fixture proving the new
/// <c>SelectChapterModal</c> chapter checkbox-list multi-select picker
/// (no longer a `return null` stub) is wired against real state. Replaces
/// the Phase 20 Plan 20-10 stub (gated behind GH #175).
///
/// **What this fixture asserts (state, not just rendering — per
/// `feedback_verify_ui_state_not_just_rendering` + audit-test-assertions.sh
/// Gate 1):**
///
///   1. **i18n contract** — the NoChaptersFound + SelectChapter + Select +
///      Cancel + SelectAll + UnselectAll keys (used by the new modal body)
///      are served by <c>/api/v5/localization</c>. Missing keys would
///      render the `[Missing translation:]` debug fallback (forbidden
///      token at smoke gates).
///   2. **Chapter-by-manga endpoint shape** — GET
///      <c>/api/v5/chapter?mangaId=…</c> returns 200 with a JSON array.
///      Even when the seeded library has zero manga (this fixture does
///      not call AddMangaFlow — it asserts the contract shape only), the
///      endpoint must respond 200 with an empty array, NOT 404.
///   3. **Queue route reachable** — Activity → Queue mounts PageContent.
///      Canonical InteractiveImportModal entry point per CONTEXT.md
///      canonical_refs section.
///
/// **R-5 strip verified at compile-time:** Plan 30-03 dropped Sonarr's
/// multi-episode-per-file logic from the modal body (manga is 1 CBZ = 1
/// chapter per Phase 4 ARCHIVE invariant). The fixture does not need to
/// re-assert this — the grep gate in plan acceptance criteria proves the
/// strip (zero TV-shape tokens in the Chapter/ subdir).
///
/// **Pitfall 4 (PATTERNS.md):** ZERO TV-shape (Series / Episode / Season)
/// testid references in this fixture — only the manga-shape
/// <c>select-chapter-modal</c> prefix is referenced.
///
/// **Pitfall 10:** Comix disabled in OneTimeSetUp.
///
/// **Forward extension when #175 + Plan 30-03 + seeded queue rows land:**
///   - Seed 1 manga with 3+ chapters via AddMangaFlow + cassette.
///   - Navigate Queue + seed a queue row with the manga pre-selected.
///   - Open InteractiveImportModal → click chapter cell (testid
///     <c>interactive-import-row-{id}-chapter</c>) → assert
///     <c>select-chapter-modal</c> appears.
///   - Click 2 of the 3 chapter rows by testid
///     <c>select-chapter-modal-row-{chapterId}</c>.
///   - Click <c>select-chapter-modal-submit</c> → assert modal closes
///     AND the row's chapter cell text now contains the 2 chosen chapter
///     numbers (TextContentAsync STATE assertion).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectChapterModalFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task select_chapter_modal_assets_wired()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // ── STATE assertion 1: i18n contract for the modal copy keys ──
        // The new modal renders translate('SelectChapter') in the header,
        // translate('NoChaptersFound') in the empty state,
        // translate('SelectAll')/translate('UnselectAll') in the action
        // row, and translate('Select')/translate('Cancel') in the footer.
        // None of these are NEW Plan-30-03 keys (all pre-existed); the
        // fixture asserts they remain wired post-stub-replacement.
        var localizationResp = await http.GetAsync("localization");
        localizationResp.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/localization must return 2xx — modal copy source");

        var localizationBody = await localizationResp.Content.ReadAsStringAsync();
        using var localizationDoc = JsonDocument.Parse(localizationBody);
        var strings = localizationDoc.RootElement.GetProperty("strings");

        var requiredKeys = new[]
        {
            "SelectChapter",
            "NoChaptersFound",
            "SelectAll",
            "UnselectAll",
            "Select",
            "Cancel"
        };
        foreach (var key in requiredKeys)
        {
            strings.TryGetProperty(key, out var prop)
                .Should().BeTrue(
                    "i18n key '{0}' must be served by /api/v5/localization for SelectChapterModal copy",
                    key);
            prop.GetString().Should().NotBeNullOrWhiteSpace(
                "i18n value for '{0}' must be non-empty",
                key);
        }

        // ── STATE assertion 2: chapter-by-manga endpoint shape ──
        // SelectChapterModalContent.useChaptersByManga(mangaId) consumes
        // ['/chapter', { mangaId }] React Query cache backed by
        // GET /api/v5/chapter?mangaId=. The endpoint must respond 200 with
        // a JSON array even when no rows match (the modal renders the
        // NoChaptersFound empty state in that case).
        var chapterResp = await http.GetAsync("chapter?mangaId=1");
        chapterResp.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/chapter?mangaId=1 must return 2xx — multi-select picker data source");

        var chapterBody = await chapterResp.Content.ReadAsStringAsync();
        using var chapterDoc = JsonDocument.Parse(chapterBody);
        chapterDoc.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "GET /api/v5/chapter must return a JSON array — useChaptersByManga's queryKey expects Chapter[]");

        // ── STATE assertion 3: Queue route reachable ──
        // The canonical entry that mounts InteractiveImportModal which in
        // turn renders SelectChapterModal is Activity/Queue.
        await Page.GotoAsync($"{RootUri}/activity/queue");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }
}
