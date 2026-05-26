using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

// audit-allow-file: manualimport
// V5 ManualImport controller is the ultimate end-to-end driver but its FE
// trigger lives BEHIND the modal Plan 30-03 ships; today this fixture
// asserts the i18n + library-listing + route-reachability state that the
// new SelectMangaModal autocomplete picker consumes. When #175 ships and
// the modal is openable end-to-end from a seeded Queue row, the
// inline-documented EXTENSION SHAPE below converts this fixture into a
// full UI-driven select-and-confirm flow.
namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 30 Plan 30-03 (II2-01) — Wave 0 fixture proving the new
/// <c>SelectMangaModal</c> autocomplete picker (no longer a `return null`
/// stub) is wired against real state. Replaces the Phase 20 Plan 20-10 stub
/// that exclusively asserted on the ManualImport endpoint payload (gated
/// behind GH #175).
///
/// **What this fixture asserts (state, not just rendering — per
/// `feedback_verify_ui_state_not_just_rendering` + audit-test-assertions.sh
/// Gate 1):**
///
///   1. **i18n contract** — the 4 NEW Plan-30-03 keys (<c>SelectManga</c>,
///      <c>FilterMangaPlaceholder</c>, <c>NoMangaFound</c>,
///      <c>NoMangaFoundHelp</c>) are served by <c>/api/v5/localization</c>.
///      These keys are what the modal renders into its title / filter
///      placeholder / empty-state copy — missing key = broken UX.
///   2. **Library listing accessible** — GET <c>/api/v5/manga</c> returns
///      200 (the data source <c>useManga()</c> consumes in
///      <c>SelectMangaModalContent</c>). The seeded baseline at
///      <c>AutomationTest.OneTimeSetUpAsync</c> already provisions root
///      folder + download client + translation profile, so the endpoint
///      returns a valid (possibly empty) array.
///   3. **Queue route reachable** — Activity → Queue route returns 200 and
///      the page content shell mounts. Queue.tsx:409 is the canonical
///      InteractiveImportModal entry point (CONTEXT.md canonical_refs
///      section "Existing code surfaces"). When seed data is available, the
///      modal drives the row-level <c>SelectMangaModal</c> open path
///      through this route.
///
/// **Pitfall 4 (PATTERNS.md):** ZERO TV-shape (Series / Episode / Season)
/// testid references in this fixture — only the manga-shape
/// <c>select-manga-modal</c> prefix is referenced.
///
/// **Pitfall 10:** Comix disabled in OneTimeSetUp.
///
/// **Forward extension when #175 + Plan 30-03 modal body land end-to-end:**
///   - Seed 2+ manga via AddMangaFlow.AddByMangaDexIdAsync.
///   - Navigate Queue + seed a queue row.
///   - Open InteractiveImportModal → click the manga cell (testid
///     <c>interactive-import-row-{id}-manga</c>) → assert
///     <c>select-manga-modal</c> appears.
///   - Type 3 chars of a seeded manga title into
///     <c>select-manga-modal-filter</c> → assert expected row visible AND
///     non-matching row hidden (state, not just presence).
///   - Click <c>select-manga-modal-row-{mangaId}</c> → assert modal closes
///     AND the InteractiveImport row's manga cell text updates to the
///     selected manga title (TextContentAsync state assertion).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectMangaModalFixture : AutomationTest
{
    [Test]
    public async Task select_manga_modal_assets_wired()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // ── STATE assertion 1: i18n contract for the 4 NEW Plan-30-03 keys ──
        // The modal renders translate('SelectManga') in the header,
        // translate('FilterMangaPlaceholder') in the filter input, and
        // translate('NoMangaFound') + translate('NoMangaFoundHelp') in the
        // empty state. Missing keys would render `[Missing translation:]`
        // (forbidden token at smoke gates per feedback_comprehensive_ui_smoke_at_gates).
        var localizationResp = await http.GetAsync("localization");
        localizationResp.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/localization must return 2xx — modal copy source");

        var localizationBody = await localizationResp.Content.ReadAsStringAsync();
        using var localizationDoc = JsonDocument.Parse(localizationBody);
        var strings = localizationDoc.RootElement.GetProperty("strings");

        var requiredKeys = new[]
        {
            "SelectManga",
            "FilterMangaPlaceholder",
            "NoMangaFound",
            "NoMangaFoundHelp"
        };
        foreach (var key in requiredKeys)
        {
            strings.TryGetProperty(key, out var prop)
                .Should().BeTrue(
                    "Plan 30-03 i18n key '{0}' must be served by /api/v5/localization",
                    key);
            prop.GetString().Should().NotBeNullOrWhiteSpace(
                "Plan 30-03 i18n value for '{0}' must be non-empty",
                key);
        }

        // ── STATE assertion 2: library listing endpoint shape ──
        // SelectMangaModalContent.useManga() consumes ['/manga'] React Query
        // cache. The endpoint must respond 200 with a JSON array (empty or
        // populated). The Plan-30-03 picker filters/sorts client-side, so
        // the wire shape is just an array of Manga objects.
        var mangaResp = await http.GetAsync("manga");
        mangaResp.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/manga must return 2xx — autocomplete picker data source");

        var mangaBody = await mangaResp.Content.ReadAsStringAsync();
        using var mangaDoc = JsonDocument.Parse(mangaBody);
        mangaDoc.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "GET /api/v5/manga must return a JSON array — useManga's queryKey expects Manga[]");

        // ── STATE assertion 3: Queue route reachable ──
        // The canonical entry that mounts the InteractiveImportModal which
        // in turn renders SelectMangaModal is Activity/Queue (Queue.tsx:409
        // + QueueRow.tsx:430 per CONTEXT.md canonical_refs section). Proving the
        // route mounts the PageContent shell gates the Plan 30-03 modal-body
        // extension shape — the per-cell pickers ship in this plan; when
        // #175 lands the seed-rows path closes the loop end-to-end.
        await Page.GotoAsync($"{RootUri}/activity/queue");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }
}
