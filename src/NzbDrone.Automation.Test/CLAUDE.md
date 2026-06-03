# NzbDrone.Automation.Test

## Purpose

End-to-end UI integration tests for Mangarr. Every test boots a real backend instance via `NzbDroneRunner` (data dir isolated under `_tests/` and DB wiped per fixture per Phase 18 D-05), drives the Mangarr frontend via `Microsoft.Playwright` (.NET binding), and asserts on state — never just rendering — against the live React+API stack.

**Phase 18 (2026-05-13) replaced the inherited Selenium-WebDriver harness with Playwright .NET.** Sonarr upstream is still on Selenium 3.141.0 (see [DIVERGENCE.md](../../DIVERGENCE.md)).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Automation.Test`

## Key Files

| File | Purpose |
|------|---------|
| `Mangarr.Automation.Test.csproj` | NuGet pins. `Microsoft.Playwright` + `Microsoft.Playwright.NUnit` (Phase 18 D-01); no Selenium references. |
| `AutomationTest.cs` | Base class. Preserves `NzbDroneRunner.KillAll/Start(true)` lifecycle (D-05); exposes `IPage Page`, `IBrowserContext Context`, `string RootUri` to fixtures. Calls `TestKit.SeedBaselineAsync()` (parameterless since Phase 39 Plan 39-07 — the GH#268 indexer-disable virtual was removed with the in-process retirement). |
| `PageModel/PlaywrightSetUpFixture.cs` | NUnit `[SetUpFixture]` — assembly-scoped `IPlaywright` + `IBrowser` lifecycle. Analog: `src/NzbDrone.Core.Test/Framework/DbTestCleanup.cs`. |
| `PageModel/PageBase.cs` | Playwright `IPage` wrapper. Auto-waiting replaces Selenium's manual `WaitForNoSpinner` + `WaitForId`; fluent return-this pattern (D-17) preserved from the Sonarr shape. |
| `PageModel/*Page.cs` | One PageObject per top-level route (`MangaIndexPage`, `MangaDetailsPage`, `AddMangaPage`, `CalendarPage`, `MangaQueuePage`, `MangaHistoryPage`, `MangaBlocklistPage`, `MangaMissingPage`, `MangaCutoffUnmetPage`, `SystemStatusPage`, etc). Sonarr-shape names (`SeriesIndexPage`, `AddSeriesPage`) are forbidden — those frontend dirs were deleted in Phase 17.3. |
| `PageModel/Modals/*Modal.cs` | One PageObject per modal (`AddMangaModal`, `EditMangaModal`, `DeleteMangaModal`, `InteractiveSearchModal`, `InteractiveImportModal`, etc). |
| `PageModel/Settings/Settings*Page.cs` | One PageObject per Settings sub-page. |
| `Flows/*.cs` | Reusable cross-page sequences (D-08). Pre-cataloged: `AddMangaFlow.AddByMangaDexIdAsync`, `SearchAndGrabFlow.GrabFirstReleaseAsync`. New flows added when a sequence is used by ≥2 fixtures. |
| `TestKit/TestKit.cs` | Pre-seed baseline (D-07) — API-driven root folder + `GatewayDownloadClient` (Phase 39 Plan 39-07 — the sole download client after the in-process image downloader was retired in Plan 39-02; seeded with `?skipTesting=true` + a non-empty `apiKey`) + default `TranslationProfile` before any browser opens. Fixtures testing settings flows override the seed. |
| `TestKit/CassetteHandler.cs` | Record/replay `DelegatingHandler` for external services (D-09/D-10). Sentinel 1×1 PNG substitution for image binaries (D-12). |
| `Tests/Routes/*LoadFixture.cs` | Route-axis coverage (Plan-03 / Plan-09 — every entry in `frontend/src/App/AppRoutes.tsx` has one). |
| `Tests/AddManga/*Fixture.cs` | AddManga cluster (Plan-04). |
| `Tests/Activity/*Fixture.cs` | Queue/History/Blocklist (Plan-05). |
| `Tests/Wanted/*Fixture.cs` | Missing/CutoffUnmet/Calendar (Plan-06). |
| `Tests/Settings/*Fixture.cs` | Settings sub-pages (Plan-07). |
| `Tests/InteractiveSearch/*Fixture.cs` + `Tests/InteractiveImport/*Fixture.cs` | Interactive search + import (Plan-08). |
| `Tests/LiveService/*LiveFixture.cs` | LiveService tier — hits real MangaDex/AniList/MAL with contract-invariant assertions. Nightly only via `[Category("LiveService")]` (D-10). |

## Patterns / Conventions

### Category attributes

Every `[Test]` carries `[Category("AutomationTest")]`. Tagging:
- `[Category("PRSmoke")]` — included in the per-PR smoke job (target ~10–20 total tests across all clusters; D-14). Use for top-nav route loads, the AddManga happy path, the SearchAndGrab happy path, one Settings save/load round-trip.
- `[Category("LiveService")]` — runs nightly only against real services (D-10). Use for upstream-contract probes (`api.mangadex.org`, `comix.to` runtime signer, AniList GraphQL, MAL).

Tests with neither tag are part of the full nightly offline-cassette suite.

### Selector strategy (D-18)

Use `Page.GetByTestId("...")` for all interactive elements. The `data-testid` attribute is the contract — frontend annotation sweep happens inside the cluster plan that authors the tests.

**Allowed `data-testid` prefixes:**
- `manga-*` (Manga library, detail, edit, delete)
- `chapter-*` (Chapter row / cell / actions)
- `add-manga-*` (Add Manga flow + modal)
- `interactive-search-modal-*` (interactive search modal)
- `interactive-import-modal-*` (interactive import modal)
- `queue-*` / `history-*` / `blocklist-*` (Activity)
- `missing-*` / `cutoff-unmet-*` / `calendar-*` (Wanted)
- `settings-*` (Settings root + sub-pages; e.g. `settings-translation-profiles-*`)
- `system-*` (System pages: status, tasks, backup, updates, events, logs)
- `nav-*` (top-nav sidebar items)

**Forbidden prefixes (Sonarr-shape — these would break sonarr-consistency-audit):**
- `series-*` — Mangarr uses `manga-*`
- `episode-*` — Mangarr uses `chapter-*`
- `add-series-*` — Mangarr uses `add-manga-*`
- `season-*` — Mangarr has no Season peer

For pure content assertions where no interaction happens, `Page.GetByRole(role, new() { Name = ... })` is acceptable at the author's discretion (D-18).

### State-not-rendering assertions

Per `feedback_verify_ui_state_not_just_rendering.md`: visible rows can hide silent rejection icons. Every `[Test]` must assert on STATE (decision, count, value, API response, error class) in addition to (or instead of) bare `ToBeVisibleAsync()`. The Plan-10 CI gate `scripts/audit-test-assertions.sh` enforces this — pure visibility tests fail at PR time.

### NUnit parallelism

`[NonParallelizable]` (assembly-wide default per Phase 18 RESEARCH §Parallelism — documented incompatibility on Microsoft.Playwright.NUnit + cross-fixture parallel). Do not opt fixtures into parallel without a spike that revisits microsoft/playwright-dotnet#1787 + #2218.

### Authoring loop (D-04)

Capture: `playwright codegen --target csharp http://localhost:8989/<route>` produces a C# starter.

Post-codegen edit checklist:
1. Wrap output in `[Test]` with `[Category("AutomationTest")]` (and optionally `[Category("PRSmoke")]`).
2. Replace absolute URL with `$"{RootUri}/<route>"` — never hardcode `http://localhost:8989`.
3. Replace string/CSS selectors with `Page.GetByTestId(...)` per the allowed prefixes above.
4. Add at least one state assertion.
5. Confirm `bash scripts/audit-test-assertions.sh` passes locally before pushing.

### Offline-safe-by-construction indexer baseline (Phase 39)

**Phase 39 (RETIRE-01..03) retired the entire in-process offline-tier apparatus that
Phase 33 (COMIX2-01) shipped.** Both Phase-33 seam choices are gone:

- **Seam A — the cassetting signer** (the `IComixSigner` record/replay implementation
  that produced the recorded payloads under the now-deleted `Fixtures/Cassettes/`
  in-process-scraper subtree) was deleted with the in-process site-scraper indexer +
  signer stack in Plan 39-03. The orphaned recordings were deleted in Plan 39-07.
- **Seam B — the GH#268 indexer-disable runtime step** (the `TestKit.SeedBaselineAsync`
  final step + the `AutomationTest` virtual that cleared the auto-seeded in-process
  indexer's Enable* flags) was removed in Plan 39-07.

The harness is now **offline-safe by construction, not by a runtime disable step**: the
only surviving `IIndexer` is the `GatewayIndexer` (Phase 37), which is seeded
DISABLED-by-default, so no enabled indexer exists to fan out to the live network. The
3 InteractiveSearch grab/history e2e fixtures that exercised the in-process mixed-source
fan-out (`InteractiveSearchModalFixture`, `InteractiveSearchGrabFixture`,
`InteractiveSearchOpenFixture`) were deleted in Plan 39-07 — their premise (an in-process
indexer producing scraper-sourced result rows) is structurally gone. An
enabled-`GatewayIndexer` interactive-search fixture is a clean future addition (owned by
the Phase-37 gateway work), out of scope for the Phase-39 retirement.

**Indexer-fixture cluster repointed to the gateway (Plan 39-07 gap-closure).**
`TestKit.SeedIndexerAsync` now seeds the `GatewayIndexer` (`implementation="GatewayIndexer"`,
`configContract="GatewaySettings"`, `baseUrl`+`apiKey`, `?skipTesting=true`) under the
distinct name `"Gateway (test seed)"` (the auto-seeded default disabled "Manga Gateway" row
keeps a distinct name). The generic provider-CRUD/test/action indexer fixtures were repointed
(NOT deleted — repointing RESTORES coverage against the sole indexer): `IndexerAddModalSchemaFixture`
+ `AddIndexerModal.GatewayCard` target `add-indexer-gateway`; `IndexerAddEditDeleteFixture` +
`DownloadClientCrudFixture` call `SettingsProviderFlow.BypassConnectionTestAsync` before the save
(the gateway `Test()` makes a live outbound call to the unreachable gateway host, so the bypass
injects `?skipTesting=true` server-side); the offline test/testall/action fixtures assert on the
browser→Mangarr POST so they repoint cleanly. `DownloadClientNegativeValidationFixture` targets
`add-downloadclient-gateway`.

### Failure artifacts (D-15)

`PageBase.cs` starts a Playwright trace on `[SetUp]` and stops it on `[TearDown]`. On failure, GHA uploads:
- `_tests/net10.0/traces/**/*.zip` (full trace; open with `playwright show-trace trace.zip`)
- `_tests/net10.0/screenshots/**/*.png` (per-step screenshots)
- `~/.config/Mangarr/logs/mangarr.txt` (backend NLog tail)

Trace viewer makes failures self-explanatory — never debug a CI failure without pulling the trace.

## Manga Adaptation Notes

| Decision | Source | Applied to |
|----------|--------|------------|
| D-01 — Driver swap Selenium → Playwright | [.planning/phases/18-.../18-CONTEXT.md](../../.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-CONTEXT.md) | `Mangarr.Automation.Test.csproj`, `AutomationTest.cs`, `PageModel/PageBase.cs` |
| D-04 — Claude-MCP → C# authoring loop | same | This entire file's "Authoring loop" section |
| D-05 — Fresh DB per fixture | same | `AutomationTest.cs` `[OneTimeSetUp]` calls `NzbDroneRunner.KillAll/Start(true)` |
| D-07 — Pre-seeded baseline | same | `TestKit/TestKit.cs` — Phase 39 Plan 39-07 repointed the baseline download client from the retired in-process image downloader to the `GatewayDownloadClient` (the sole download client; seeded `?skipTesting=true` + non-empty `apiKey`). |
| D-08 — Shared Flow helpers as first-class deliverables | same | `Flows/*.cs` directory |
| D-09 / D-10 — Cassette replay + LiveService tier | same | `TestKit/CassetteHandler.cs` + `Tests/LiveService/` |
| D-11 — Comix offline-tier seam | **RETIRED — Phase 39 Plan 39-07.** Phase 33's cassetting-signer seam + the GH#268 indexer-disable runtime step were both deleted with the in-process site-scraper indexer (Plan 39-03). The harness is now offline-safe by construction (sole `IIndexer` is the disabled-by-default `GatewayIndexer`). See `### Offline-safe-by-construction indexer baseline (Phase 39)`. |
| D-12 — Sentinel PNG + ≤3 real-byte image fixtures | same | `TestKit/Fixtures/` (sentinel) + `TestKit/Fixtures/RealBytes/` (bucketed) |
| D-13 / D-16 — Tiered CI: per-PR smoke + nightly full SQLite+Postgres matrix | same | `.github/workflows/build_v5.yml` jobs `automation_test_pr_smoke` / `_nightly` / `_liveservice` |
| D-14 — PRSmoke subset via `[Category("PRSmoke")]` | same | Tag a test as `PRSmoke` only when its route or flow is core to a top-nav user path |
| D-15 — Failure artifacts | same | `PageBase.cs` trace start/stop + GHA artifact upload |
| D-17 — PageObject per page, fluent return-this | same | `PageModel/*Page.cs` shape |
| D-18 — `data-testid` selector strategy | same | This file's "Selector strategy" section |
| D-19 — `MainPagesTest.cs` deleted outright | same | File does not exist (`! test -f MainPagesTest.cs`) |

## Cross-References

- [`DIVERGENCE.md`](../../DIVERGENCE.md) — Selenium → Playwright swap recorded under `## Already-Merged Divergences (on Mangarr-v0)`
- [`.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-CONTEXT.md`](../../.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-CONTEXT.md) — D-01..D-19 (locked decisions driving every convention above)
- [`.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-RESEARCH.md`](../../.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-RESEARCH.md) — Playwright .NET 1.59.0 pin, parallelism analysis, CI integration deltas
- [`.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-PATTERNS.md`](../../.planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-PATTERNS.md) — analog file map (28 files classified)
- [`.planning/phases/18-automated-ui-integration-test-suite-playwright-net/INVENTORY.md`](../../.planning/phases/18-automated-ui-integration-test-suite-playwright-net/INVENTORY.md) — four-axis inventory; every row must have ≥1 green test here
- [`.claude/skills/mangarr-phase-smoke-test/SKILL.md`](../../.claude/skills/mangarr-phase-smoke-test/SKILL.md) §"Task 7" + §"Task 8" — capture-into-suite loop driving sustainment of inventory coverage
- [`scripts/audit-ui-inventory.sh`](../../scripts/audit-ui-inventory.sh) — PR-time coverage gate (Plan-10 product)
- [`scripts/audit-test-assertions.sh`](../../scripts/audit-test-assertions.sh) — PR-time state-not-rendering anti-pattern gate (Plan-10 product)
- [`src/NzbDrone.Test.Common/NzbDroneRunner.cs`](../NzbDrone.Test.Common/NzbDroneRunner.cs) — reused unchanged; supplies the `KillAll/Start(true)` lifecycle pattern
- [`.planning/phases/39-retire-in-process/39-07-PLAN.md`](../../.planning/phases/39-retire-in-process/39-07-PLAN.md) — Phase 39 Plan 39-07 (automation-harness retirement to the gateway-only baseline; deletes the Phase-33 cassetting-signer seam + GH#268 indexer-disable step + the 3 in-process-premise InteractiveSearch fixtures + orphaned in-process-scraper cassette recordings)
