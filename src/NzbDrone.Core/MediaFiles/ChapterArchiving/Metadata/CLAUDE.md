# MediaFiles/ChapterArchiving/Metadata

## Purpose

Phase 4 metadata-writer plugin contract (D-14). Pluggable sibling to `IChapterArchiver` so v2 plug-ins can stack additional reader-metadata sidecars (Mihon-format, Calibre OPF, etc.) without touching the downloader, archiver, or import layers.


## Key Files

| File | Purpose |
|------|---------|
| `IMetadataWriter.cs` | Plugin contract — `FormatKey`, `AppliesTo(request)`, `WriteAsync(request, ctx, ct)`. |
| `ComicInfo/ComicInfoMetadataWriter.cs` | v1 default writer (FormatKey=`"comicinfo"`). Dual-write v2.0 + v2.1 ComicInfo.xml. |
| `ComicInfo/ComicInfoXmlBuilder.cs` | Pure XDocument construction; testable in isolation against golden XML. |
| `ComicInfo/AgeRatingMapper.cs` | MangaDex `contentRating` → ComicInfo `<AgeRating>` enum mapping (RESEARCH.md Q-5). |

## Patterns / Conventions

- **Phase 30 Plan 30-04 D-02 wrap pattern (2026-05-23+):** the `IMetadataWriter` plugin contract here is now the LEGACY in-CBZ writer surface; `ComicInfoMetadataWriter.cs` is wrapped by the NEW `ComicInfoMetadata` ThingiProvider at `src/NzbDrone.Core/Metadata/ComicInfo/ComicInfoMetadata.cs` (Option 4b delegation). The legacy file is UNCHANGED — Phase 4 golden-fixture tests (`ComicInfoMetadataWriterFixture` + `ComicInfoXmlBuilderFixture`) continue to construct the writer directly. The delegation chain is: archiver → `IMetadataFactory.Enabled()` → `ComicInfoMetadata.WriteAsync` → `ComicInfoMetadataWriter.WriteAsync`.
- **Archiver invocation flip (Plan 30-04 Task 5):** `CbzChapterArchiver` + `FolderImagesChapterArchiver` enumerate `_metadataFactory.Enabled()` instead of a DryIoc-auto-discovered metadata-writer enumeration. Result: the metadata-writer-implementing types are no longer registered as DI singletons consumed by archivers; only `ComicInfoMetadataWriter` survives as a directly-injected ctor parameter of `ComicInfoMetadata` for the delegation chain. The legacy global metadata-formats key in `ConfigService` is no longer read by archivers — `MetadataDefinition.Enable` is the canonical signal.
- **Pre-Phase-30 historical context (preserved for git-blame archeology):** prior to Plan 30-04, archivers iterated the DryIoc-auto-discovered metadata-writer enumeration and the writer's `AppliesTo` checked the global `ConfigService` formats key. The check is still present in the writer file but bypassed at the archiver layer (Plan 30-04 `ComicInfoMetadata.AppliesTo` returns `Definition?.Enable ?? false` — the legacy formats key stays in the Config table per `project_dev_migration_policy` but is no longer functional for archivers).
- **Dual-write for ComicInfo** (Pitfall 3): emits BOTH v2.0 `<ScanInformation>` AND v2.1 `<Translator>` so v2.0-only readers (Kavita ambiguous) see fallback AND v2.1-aware readers (Komga 1.10+) see preferred field.
- **InvariantCulture for numerics** (Pitfall 7): `<Number>` always uses `chapter.Number.ToString("0.###", CultureInfo.InvariantCulture)` so `de-DE` doesn't format `132.5` as `"132,5"`.
- **No xmlns**: anansi-project XSDs declare no `targetNamespace`; emitted XML is plain `<ComicInfo>...</ComicInfo>`.

## Manga Adaptation Notes

- **Mangarr divergence (Phase 4 D-14):** Mangarr's chapter-archive metadata writes through the `IMetadataWriter` plugin contract here, NOT through Sonarr's `Extras/`-style on-disk sidecar pattern. See `DIVERGENCE.md`.
- **Phase 30 Plan 30-04 D-02 update:** Plan 30-04 wraps `IMetadataWriter` under the new `IMetadata` ThingiProvider substrate at `src/NzbDrone.Core/Metadata/`. The underlying writer logic is preserved; the substrate gives Settings/Metadata a real CRUD/enable-toggle surface (closes the 404 since Phase 15 deleted V3). A new DIVERGENCE.md entry under "Phase 30 Plan 30-04 — IMetadata manga-shape signature divergence" documents the R-14 manga-shape `IMetadata` contract that wraps this writer.

## Cross-References

- [.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md](../../../../../.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md) — D-14 verbatim (`IMetadataWriter` plugin contract origin).
- [.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-CONTEXT.md](../../../../../.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-CONTEXT.md) — D-02 + R-1 wrap-pattern rationale.
- [../../../Metadata/CLAUDE.md](../../../Metadata/CLAUDE.md) — Phase 30 ThingiProvider substrate that wraps this writer.
- [ComicInfo/ComicInfoMetadataWriter.cs](./ComicInfo/ComicInfoMetadataWriter.cs)
