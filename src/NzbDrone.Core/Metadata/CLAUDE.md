# NzbDrone.Core/Metadata

## Purpose

Phase 30 Plan 30-04 D-02 — `IMetadata` ThingiProvider substrate for chapter-archive
metadata writers. Sibling to `MediaFiles/ChapterArchiving/Metadata/` (legacy Phase 4
`IMetadataWriter` plugin contract; preserved as the internal delegation target for
the Option 4b wrap).


## Key Files

| File | Purpose |
|------|---------|
| `IMetadata.cs` | ThingiProvider contract — `FormatKey`, `AppliesTo`, `WriteAsync`. Manga-shape per R-14 (CBZ-internal sidecar; NOT Sonarr's Series/Episode-keyed on-disk shape). |
| `MetadataBase.cs` | Abstract base `MetadataBase<TSettings> : IMetadata`. Mirrors `ImportListBase<TSettings>` (Phase 26) + `NotificationBase<TSettings>` (Phase 6). |
| `MetadataDefinition.cs` | 1-line POCO `MetadataDefinition : ProviderDefinition { }`. No extra fields per D-04 single-toggle UX. |
| `MetadataFactory.cs` | `IMetadataFactory` + `MetadataFactory : ProviderFactory<IMetadata, MetadataDefinition>` with Sonarr-canonical `InitializeProviders` override + `Enabled()` method. |
| `MetadataRepository.cs` | `IMetadataRepository : IProviderRepository<MetadataDefinition>` + concrete `MetadataRepository : ProviderRepository<MetadataDefinition>`. |
| `ComicInfo/ComicInfoMetadata.cs` | Single v1.2 ThingiProvider — D-02 Option 4b delegating to `MediaFiles/ChapterArchiving/Metadata/ComicInfo/ComicInfoMetadataWriter` so Phase 4 golden-fixture XML tests stay green. |
| `ComicInfo/ComicInfoMetadataSettings.cs` | `IProviderConfig` with zero `[FieldDefinition]` attributes per D-04 (enable-only). |
| `Stax/StaxMetadata.cs` | Stax `IMetadata` provider (quick-260701-e71) — the FIRST on-disk **series-level** writer. Writes `stax.json` = `{ mangabakaId }` into the manga folder via `IDiskProvider` on refresh/add; self-heal per D-02 (create-if-missing, overwrite-on-diff, no-op-on-match), skip-when-no-`MangaBakaId`/empty-`Path` per D-03. NOT a CBZ writer (`AppliesTo` = false, `WriteAsync` no-op). |
| `Stax/StaxMetadataSettings.cs` | `IProviderConfig` with zero `[FieldDefinition]` attributes, enable-only — mirrors `ComicInfoMetadataSettings` verbatim. |

## Patterns / Conventions

- **Mirrors `IImportListFactory` Phase 26 substrate per D-13.** `ProviderFactory<TProvider, TDefinition>` + `InitializeProviders` override gives DryIoc auto-discovery + idempotent boot-time seeding.
- **D-02 archiver invocation flip:** `CbzChapterArchiver` + `FolderImagesChapterArchiver` enumerate `_metadataFactory.Enabled()` instead of `IEnumerable<IMetadataWriter>` (DryIoc auto-discovery). The Enable toggle on `MetadataDefinition` IS the canonical signal; `Config.MetadataFormats` is no longer read by archivers post-flip.
- **D-03 user-state preservation:** Migration 004 (Plan 30-05) seeded an enabled `ComicInfoMetadata` row IF `Config.MetadataFormats` contained `"comicinfo"` (the Phase 4 default). Users who previously cleared `MetadataFormats` to `[]` get the row but disabled.
- **D-04 single-toggle UX:** `ComicInfoMetadataSettings` ships with ZERO `[FieldDefinition]` attributes — Settings/Metadata page shows only an enable/disable toggle. v1.3+ adds the rich Settings shape when a 2nd writer ships.
- **D-02 Option 4b delegation:** `ComicInfoMetadata` does NOT absorb the existing `ComicInfoMetadataWriter`. It holds an injected `ComicInfoMetadataWriter` and delegates `WriteAsync` verbatim — preserves Phase 4 golden-fixture tests (the `ComicInfoMetadataWriterFixture` + `ComicInfoXmlBuilderFixture` continue to test the writer directly).
- **On-disk series-level write shape (quick-260701-e71):** the optional `IMetadata.WriteMangaMetadata(Manga)` method carries a default no-op in `MetadataBase`. CBZ-internal writers (ComicInfo) leave it a no-op — they emit their sidecar INSIDE the CBZ via `WriteAsync`; on-disk series-level writers (Stax) override it to write a standalone sidecar next to the manga folder. `RefreshMangaService` enumerates `IMetadataFactory.Enabled()` and calls `WriteMangaMetadata(manga)` on each ONCE per successful refresh (immediately after `RescanManga`), which covers add + refresh (`MangaAddedHandler` funnels adds through `RefreshMangaCommand`). The two lifecycles (CBZ-internal + on-disk series-level) now coexist on the one `IMetadata` contract.
- **D-11 atomic-pair merge order:** Plan 30-05 (Migration 004) merges BEFORE Plan 30-04 within Wave 2 on the same Phase 30 branch. The `MetadataFactory.InitializeProviders` query reads the pre-existing `Metadata` table (created in `001_mangarr_baseline.cs:129-134` — R-10 verified) seeded by Migration 004.

## Manga Adaptation Notes

R-14 divergence (recorded in `DIVERGENCE.md` Phase 30 Plan 30-04 entry):

- Sonarr's `IMetadata` writes standalone on-disk sidecars (Kodi NFO, Roksbox, Wdtv) keyed by Series/Episode/Season. Method signatures take `Series` + `EpisodeFile` and return `MetadataFileResult` describing a NEW disk file.
- Mangarr's `IMetadata` writes INTO the CBZ via `ArchiveOutputContext.OpenSidecar` (Phase 4 D-14 in-CBZ pattern). Method signature takes `ChapterArchiveRequest` + `ArchiveOutputContext` + `CancellationToken` and returns `Task` — no `MetadataFileResult` because the file is an entry inside an open ZipArchive, not a new disk file the caller tracks.
- **DONE for the series-level shape (quick-260701-e71):** the deferred Sonarr-canonical series-level on-disk write shape was reintroduced via the optional `IMetadata.WriteMangaMetadata(Manga)` method (default no-op in `MetadataBase`), and **Stax** (`Stax/StaxMetadata.cs`) is its first consumer — writing `stax.json` = `{ mangabakaId }` into each manga folder on refresh/add. ComicInfo (and any future CBZ-only writer) is unaffected because the base default is a no-op. See the quick-260701-e71 entry in `DIVERGENCE.md`. Further v1.3+ on-disk writers (Kodi NFO, Komga series.json, Kavita-flavored) now build on this same optional method.

## Cross-References

- [`../ImportLists/CLAUDE.md`](../ImportLists/CLAUDE.md) — Phase 26 D-13 substrate template that Plan 30-04 mirrors 1:1.
- [`../MediaFiles/ChapterArchiving/Metadata/CLAUDE.md`](../MediaFiles/ChapterArchiving/Metadata/CLAUDE.md) — Phase 4 D-14 `IMetadataWriter` plugin contract (the internal delegation target for `ComicInfoMetadata` per D-02 Option 4b).
- [`../../../.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-CONTEXT.md`](../../../.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-CONTEXT.md) — D-02/D-03/D-04/D-11 + R-14 + R-1 mitigation.
- [`../../../.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-PATTERNS.md`](../../../.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-PATTERNS.md) — §Plan 30-04 line-anchored substrate analogs.
- [`../../../DIVERGENCE.md`](../../../DIVERGENCE.md) — Phase 30 Plan 30-04 entry citing R-14 IMetadata manga-shape divergence; quick-260701-e71 entry recording the series-level `WriteMangaMetadata(Manga)` reintroduction + Stax.

---
*Last updated: 2026-07-01 after quick-260701-e71 — added the `Stax/StaxMetadata.cs` + `Stax/StaxMetadataSettings.cs` Key Files rows (first on-disk series-level writer), documented the new optional `IMetadata.WriteMangaMetadata(Manga)` method + `MetadataBase` no-op default + `RefreshMangaService` hook, and updated the R-14 divergence note to "DONE for the series-level shape". See DIVERGENCE.md quick-260701-e71.*
