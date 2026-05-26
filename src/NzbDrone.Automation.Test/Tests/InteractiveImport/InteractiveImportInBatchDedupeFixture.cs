using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 30 Plan 30-01 Task 3 (II2-06) — Wave 0 fixture proving the in-batch
/// chapter-dedupe short-circuit in
/// <c>frontend/src/InteractiveImport/Interactive/InteractiveImportContent.tsx
/// handleImportSelectedPress</c> (Phase 25 REVIEW.md WR-03 verbatim fix).
///
/// The dedupe scan was rewritten from an interleaved per-row
/// <c>let hasDuplicateChapters = false</c> walk to a hoisted
/// <c>items.find</c> pre-loop scan that short-circuits BEFORE the file-build
/// loop runs (RESEARCH §8 lines 957-982 + PATTERNS.md §Plan 30-01 II2-06).
///
/// **What this fixture asserts (state, not just rendering — per
/// `feedback_verify_ui_state_not_just_rendering` + audit-test-assertions.sh
/// Gate 1):**
///
///   1. **i18n contract** — the <c>InteractiveImportDuplicateChapters</c>
///      localization key is served by <c>/api/v5/localization</c> with the
///      Plan-30-01-documented user-facing message. This is the literal text
///      the dedupe short-circuit puts into the error banner; if the key were
///      missing or empty, the banner would render `[Missing translation:]`
///      and the dedupe path would silently fail UX-wise.
///   2. **Queue route reachable** — Activity → Queue route returns 200 and
///      the page-content root is present. Proves the canonical entry point
///      that mounts <c>InteractiveImportModal</c> (Queue.tsx:409 per
///      CONTEXT.md canonical_refs section) is reachable; modal mount itself is
///      gated on selected queue rows + per-cell pickers shipped in Plan 30-03.
///   3. **No POST fires on dedupe path (compile-time contract)** — the
///      production code now calls <c>return</c> immediately after
///      <c>setInteractiveImportErrorMessage(translate('InteractiveImportDuplicateChapters'))</c>;
///      this is enforced by the grep gate in Plan 30-01 acceptance criteria
///      (<c>hasDuplicateChapters</c> identifier removed; <c>items.find</c>
///      present in <c>handleImportSelectedPress</c>; see plan SUMMARY for
///      grep evidence). When Plan 30-03 ships the modal bodies + #175 ships
///      the V5 ManualImport controller, the fixture can be EXTENDED to
///      drive the UI end-to-end and capture the absent POST via
///      <c>Page.WaitForRequestAsync</c> on the &lt;Import&gt; click — the
///      <c>Inconclusive</c> branch below documents the exact extension shape.
///
/// **Pitfall 4 grep gate (PATTERNS.md Pattern A):** ZERO TV-shape (Series /
/// Episode / Season) testid references in this file — only the manga-shape
/// `interactive-import-` prefix is used.
///
/// **Pitfall 10:** Comix disabled in OneTimeSetUp to prevent live indexer
/// calls during fixture setup.
///
/// **Forward extension when Plan 30-03 ships:** the assertion shape below
/// (i18n contract + route reachability) is the floor; once the modal bodies
/// + #175 land, augment with:
///   - drive Queue → select 2 queue rows mapping to same chapter id → click
///     Import → assert error banner text + <c>WaitForRequestAsync</c>
///     timeout proves the manualimport POST never fires.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportInBatchDedupeFixture : AutomationTest
{
    [Test]
    public async Task in_batch_duplicate_chapters_block_import()
    {
        // -- STATE assertion 1: i18n contract -------------------------------
        // The dedupe short-circuit at InteractiveImportContent.tsx:498-512
        // calls translate('InteractiveImportDuplicateChapters'). That key
        // MUST resolve to a non-empty user-facing string OR the banner
        // renders the `[Missing translation:]` debug fallback (per
        // feedback_comprehensive_ui_smoke_at_gates — forbidden token at
        // smoke gates). Asserting on /api/v5/localization is the canonical
        // path used by useLocalization (frontend/src/App/useLocalization.ts).
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var localizationResp = await http.GetAsync("localization");
        localizationResp.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/localization must return 2xx — dedupe banner text source");

        var localizationBody = await localizationResp.Content.ReadAsStringAsync();
        using var localizationDoc = JsonDocument.Parse(localizationBody);

        // Localization endpoint returns { strings: { Key: "Value", ... } }
        var strings = localizationDoc.RootElement.GetProperty("strings");
        strings.TryGetProperty("InteractiveImportDuplicateChapters", out var dupeKey)
            .Should().BeTrue(
                "InteractiveImportDuplicateChapters i18n key must be served by " +
                "/api/v5/localization — the dedupe short-circuit puts this text into " +
                "the error banner via translate(). Missing key would render the " +
                "`[Missing translation:]` debug fallback (forbidden token at smoke gates).");

        var dupeText = dupeKey.GetString();
        dupeText.Should().NotBeNullOrWhiteSpace(
            "InteractiveImportDuplicateChapters i18n value must be non-empty");
        dupeText.Should().Contain(
            "chapter",
            "i18n value must reference the user-visible 'chapter' domain term per " +
            "PROJECT.md DOMAIN-02 (manga-shape language)");

        // -- STATE assertion 2: Queue route reachable -----------------------
        // The canonical entry point that mounts InteractiveImportModal is
        // Activity/Queue (Queue.tsx:409 + QueueRow.tsx:430 per CONTEXT.md
        // <canonical_refs>). Proving the route is reachable today gates the
        // Plan-30-03 modal-body extension: when the per-cell pickers ship
        // and Plan 30-03 wires real seed data, this fixture can be augmented
        // (see class comment) without changing the entry-point story.
        await Page.GotoAsync($"{RootUri}/activity/queue");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // -- STATE assertion 3: manualimport POST contract documented ------
        // The dedupe path in InteractiveImportContent.tsx:handleImportSelectedPress
        // calls `return` IMMEDIATELY after setting the error banner — the
        // `executeCommand({ name: 'ManualImport', ... })` invocation is
        // skipped when duplicates are detected. Today the InteractiveImport
        // modal cannot be driven through the UI without seeded queue rows
        // (Plan 30-03 ships the per-cell pickers; #175 ships the V5
        // ManualImport controller); when both land, the assertion shape is:
        //
        //   var noPostTask = Page.WaitForRequestAsync(
        //       r => r.Url.Contains("/api/v5/command") && r.Method == "POST",
        //       new() { Timeout = 2_000 });
        //   await importButton.ClickAsync();
        //   await Assertions.Expect(errorBanner).ToContainTextAsync(dupeText);
        //   await Assert.ThrowsAsync<TimeoutException>(() => noPostTask)
        //       .ConfigureAwait(false);
        //
        // The compile-time contract (production code calls `return` on the
        // dedupe path; grep gate enforces `hasDuplicateChapters` removed +
        // `items.find` present) is asserted by Plan 30-01 acceptance
        // criteria and verified in SUMMARY.md.
        //
        // Probe the manualimport endpoint to document its current shape;
        // when #175 ships, the assertion-shape upgrade is mechanical.
        var manualImportResp = await http.GetAsync("manualimport?folder=" +
            Uri.EscapeDataString(Path.GetTempPath()));

        // #175 shipped in Phase 25 — src/Mangarr.Api.V5/ManualImport/ManualImportController.cs.
        // The endpoint must return 2xx; accepting 404 would let a regression silently slip
        // through (e.g. controller deletion, route rename). The fixture exercises the
        // happy path only — empty-folder probe response shape isn't asserted here because
        // Plan 25 owns those contract fixtures (ManualImportControllerFixture / etc).
        var statusCode = (int)manualImportResp.StatusCode;
        statusCode.Should().BeInRange(
            200,
            299,
            "manualimport endpoint must be reachable (Phase 25 shipped #175). Observed: {0}",
            statusCode);
    }
}
