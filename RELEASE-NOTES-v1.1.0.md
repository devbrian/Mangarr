# Mangarr v1.1.0 — Settings Completion + ImportLists Vertical

Released: 2026-05-21

## Overview

Mangarr v1.1.0 is the **Settings Completion + ImportLists Vertical** milestone. Building on v1.0.0's bedrock (Manga domain, MangaDex + Comix sources, Komga + Kavita reader fanout, GHCR Docker pipeline), v1.1.0 fills in the seven Settings verticals that were either broken, schema-mismatched, or absent at the v1.0.0 cut — and lands a complete ImportLists subsystem with three first-party providers (MangaDex follows / AniList list / MAL list).

The vertical was scoped and shipped across seven sequential phases (22 → 27.1) between 2026-05-16 and 2026-05-21, plus the Phase 28 close-out that produced this release (UAT walkthrough of all 5 v1.1 verticals, three LIVE provider Playwright fixtures with cassette recording, `sonarr-consistency-audit` Pattern κ sweep returning 0 HIGH / 0 MED / 0 LOW, `TEST-V11-01` milestone-aggregate gate, full `DIVERGENCE.md` audit-pass with new v1.1.0 Release Snapshot section, GH cross-reference verification on the 4 closed v1.1 issues).

## Highlights

### Settings → Tags (Phase 22)

Repaired the Settings/Tags 404 cascade — `TagInUseValidator` defense-in-depth + `TagController` route restoration + frontend Redux surgical cleanup (`Tags.tsx`, `TagDetailsModalContent.tsx`). Closes the Tags leg of #83. Zero-console-errors invariant locked in via `TagsPageConsoleCleanFixture`. Full Sonarr-canonical Tags backend + UI parity restored.

### Settings → Profiles → Delay Profile (Phase 23)

DelayProfile schema trimmed from Sonarr's 8-column TV-shape to Mangarr's 4-column manga-shape (Migration 002 dropped 4 columns: `enableUsenet`, `enableTorrent`, `usenetDelay`, `torrentDelay`). `PreferredProtocol` field preserved dormant per defense-in-depth. The mapper bridge introduced as a transitional Phase 23 artifact was atomically closed in Phase 26 Plan 26-03 (DP-02) — all `// PHASE-23 BRIDGE` mapper hardcodes deleted, only the live 7-field projection remains.

### Settings → Tags → Auto Tagging (Phase 24)

Auto Tagging restore-rebuild with **user-additive precedence** semantics: rule-applied tags are additive on top of the user's manual tags, never destructive. Cascade fires on `MangaAddedEvent` (Phase 24 D-04). New 11-spec manga-shape specification catalog: 3 TV-shape specs intentionally absent (`NetworkSpec`, `SeriesTypeSpec`, `OriginalCountrySpec`, `QualityProfileSpec`, `OriginalLanguageSpec`), 1 TV-shape spec split (`MonitoredSpec` → manga peer), 3 NEW manga-only specs added. Migration 002 also absorbed the `Manga.Artist` + `Demographic` column adds. `MangaDemographic` + `MangaContentRating` enums shipped.

### Interactive Import V5 controller + `/add/import` route + InteractiveImport subdir rename (Phase 25 + 25.1)

V5 `ManualImportController` port lands the long-deferred Interactive Import REST surface (closes **#175**). Top-level `/add/import` UI route mounts the InteractiveImport flow as a Library/Add entry point (closes **#110**). InteractiveImport `/{Episode,Series,Season}/` subdir cascade renamed to manga-canonical shape (closes **#86**). Wanted/Missing "Manual Import" button LOCK guard dropped — feature is now generally available across the Wanted vertical. Phase 25.1 added the library-import flow route relocation as a fast-follow.

### ImportList substrate + Migration 003 + AniList GraphQL transport extract (Phase 26)

ImportList subsystem scaffolding: HTTP surface, `IImportList` plugin contract, `ImportListExclusionService` with `IHandle<MangaDeletedEvent>` auto-exclusion (manga deletes auto-create exclusion rows so re-syncs don't re-add the same manga), 24-hour `TaskManager.defaultTasks.ImportListSyncCommand` cadence. **Migration 003** lands the `ImportLists` + `ImportListExclusions` tables post-baseline (the pre-v1 dev-migration policy ENDED at Phase 21 — Migration 003 is a sequential post-baseline migration, NOT an in-place edit to `001_mangarr_baseline.cs`). `AniListGraphQLProxy` extracted as a reusable transport ahead of the AniList provider plugin (Phase 27).

### 3 ImportList provider plugins (Phase 27)

Three provider plugins with provider-specific OAuth shapes:

- **MangaDex follows** — OAuth2 password-grant Test-and-Connect UX (`RequestAction("startOAuth")`). User clicks Test & Connect; provider creds in the form are exchanged for an access+refresh token pair server-side.
- **AniList list** — Pin paste-back UX. User opens `anilist.co/api/v2/oauth/pin` in a new tab, authenticates, copies the displayed Pin into the Mangarr modal; server-side Pin → token exchange completes the handshake.
- **MAL list** — Paste-the-callback-URL UX with PKCE + state. Server returns a MAL authorize URL with `code_challenge=plain` + random `state`; user opens in new tab, MAL redirects to the localhost callback URL; user copies the redirected URL back into the modal; server validates `state` + exchanges `code` for tokens.

All three providers wired into the ImportList substrate's `ImportListSyncCommand` cadence; manga discovered via list sync auto-add to the library (subject to ImportList settings + ExclusionList membership).

### /settings/importlists Sonarr-canonical parity sweep (Phase 27.1)

Full `/settings/importlists` parity sweep closing **#220**: Options FieldSet (ListSyncLevel + ListSyncIntervalDays + CleanLibraryLevel + Tags), Exclusions Table with 3-column sort + bulk select + pager, Manage subtree (8 files for the per-list Manage modal), Test-All toolbar. Sonarr-canonical `data-testid` Pattern κ enforcement — zero `series-*` / `episode-*` / `season-*` testids on any v1.1 surface.

## Upgrade Notes

- **Migrations 002 + 003 are sequential post-baseline migrations.** The pre-v1 dev-migration policy (which allowed in-place edits to `001_mangarr_baseline.cs`) ENDED at Phase 21 / v1.0.0. Migrations 002 (`002_v1_1_manga_artist_demographic.cs`) and 003 (`003_v1_1_importlist_substrate_delayprofile_trim.cs`) apply automatically on first startup of a v1.1.0 container against a v1.0.0 database — **fresh-DB wipe is NOT required** for the v1.0.0 → v1.1.0 upgrade path.
- `ImportListExclusions.TvdbId` (TV-shape leftover) migrates to a `(MangaDexId, MalId, AniListId)` triplet via Migration 003. Existing TV-shape exclusion rows auto-convert; no manual data migration step required.
- `ImportList.QualityProfileId` (TV-shape) auto-maps to `ImportList.TranslationProfileId` (Mangarr's manga-shape peer). Configurations that referenced a Sonarr-era QualityProfile auto-map to the closest TranslationProfile.
- 24-hour `ImportListSyncCommand` cadence is registered in `TaskManager.defaultTasks` (Phase 26 D-15); manual triggers via `POST /api/v5/command {name:"ImportListSync"}` remain available.

## Breaking Changes

- **`ImportListExclusions.TvdbId` → MangaDex/MAL/AniList ID triplet.** Code or external tooling that queried the `TvdbId` column on `ImportListExclusions` must switch to the new triplet columns (Phase 27.1 D-01).
- **`ImportList.QualityProfileId` (TV-shape) → `ImportList.TranslationProfileId` (Manga-shape).** Cumulative from v1.0; v1.1 makes this the only path for ImportList rows.
- **DelayProfile 4-column drop.** The TV-shape DelayProfile columns `enableUsenet`, `enableTorrent`, `usenetDelay`, `torrentDelay` were dropped from the schema in Phase 23 Migration 002. ImportList settings that referenced a now-deleted Sonarr QualityProfile (legacy) require manual re-attachment to a TranslationProfile.

## Issues Closed

- **#175** — V5 ManualImport controller (Phase 25)
- **#110** — `/add/import` top-level UI route (Phase 25)
- **#86** — InteractiveImport `/{Episode,Series,Season}/` subdir rename (Phase 25)
- **#83** — Settings 404 cascade collectively (Phase 22 Tags + Phase 23 DelayProfile + Phase 24 AutoTagging + Phase 26 ImportLists substrate + Phase 27 provider plugins)
- **#220** — ImportList Options FieldSet + ListSyncLevel (Phase 27.1)

## Container Images

The full hotio-style tag stack is pushed per release:

| Tag                                       | Use when you want…                                |
| ----------------------------------------- | ------------------------------------------------- |
| `ghcr.io/devbrian/mangarr:1.1.0.NNN`      | Exact reproducibility — pin to a specific build  |
| `ghcr.io/devbrian/mangarr:1.1.0`          | Latest 1.1.0 patch level                          |
| `ghcr.io/devbrian/mangarr:1.1`            | Latest 1.1.x                                      |
| `ghcr.io/devbrian/mangarr:1`              | Latest 1.x                                        |
| `ghcr.io/devbrian/mangarr:latest`         | Bleeding edge — rolls forward across major versions |

`NNN` is populated by CI at tag push (the GH workflow run number) per Phase 21 D-07 / D-08.

## DIVERGENCE Notes

See [`DIVERGENCE.md`](./DIVERGENCE.md) → `## v1.1.0 Release Snapshot — 2026-05-21` (line 851) for the full structural delta vs v1.0.0 across all 7 v1.1 phases. The snapshot also names two **anti-divergences** — explicit decisions to NOT diverge from Sonarr-canonical patterns:

- **OAuth token encryption-at-rest** — Sonarr-canonical plaintext per Phase 27 D-01..D-04. Mirrors Trakt `TraktSettings.cs:27-37` (`Hidden = HiddenType.Hidden` on `AccessToken` / `RefreshToken`). NO `EncryptedSettings` BLOB column, NO DPAPI. GH #216 closed `wontfix`.
- **Per-ImportList `SemaphoreSlim` for OAuth token refresh** — Sonarr-canonical correctness fix per Phase 27 D-05. Mirrors Sonarr Trakt `RefreshTokenIfNecessary()` shape. Reactive-on-401 refresh; per-`ImportListId` keyed semaphore. NOT a Mangarr-only enhancement.

---

*Mangarr v1.1.0 — released 2026-05-21. Git tag: `v1.1.0.NNN` (4-part, pushed by orchestrator per Phase 28 D-20 after PR merge + green CI on merge commit). GHCR image tag: `1.1.0.<run_number>` (4-part, computed by CI). Branch: `Mangarr-v0`. Built via `.github/workflows/build_v5.yml` → `deploy.yml` (Phase 21 D-04..D-12; Phase 28 D-19 release-notes resolver generalized so future v1.2.0 / v2.0.0 releases only need to author the corresponding `RELEASE-NOTES-v<MAJOR>.<MINOR>.0.md` file at repo root — no further workflow edits required). Built commit SHA is recorded automatically by `ncipollo/release-action@v1` in the GitHub Release metadata.*
