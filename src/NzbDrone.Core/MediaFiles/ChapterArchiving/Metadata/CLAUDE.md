# MediaFiles/ChapterArchiving/Metadata

## Purpose

Phase 4 metadata-writer plugin contract (D-14). Pluggable sibling to `IChapterArchiver` so v2 plug-ins can stack additional reader-metadata sidecars (Mihon-format, Calibre OPF, etc.) without touching the downloader, archiver, or import layers.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MediaFiles\ChapterArchiving\Metadata`

## Key Files

| File | Purpose |
|------|---------|
| `IMetadataWriter.cs` | Plugin contract — `FormatKey`, `AppliesTo(request)`, `WriteAsync(request, ctx, ct)`. |
| `ComicInfo/ComicInfoMetadataWriter.cs` | v1 default writer (FormatKey=`"comicinfo"`). Dual-write v2.0 + v2.1 ComicInfo.xml. |
| `ComicInfo/ComicInfoXmlBuilder.cs` | Pure XDocument construction; testable in isolation against golden XML. |
| `ComicInfo/AgeRatingMapper.cs` | MangaDex `contentRating` → ComicInfo `<AgeRating>` enum mapping (RESEARCH.md Q-5). |

## Patterns / Conventions

- **User toggles via `Config.MetadataFormats`** (default `["comicinfo"]`; can be `[]` to disable; can stack `["comicinfo","mihon"]` once v2 ships). Per Phase 1 D-04 — global, NO Library entity.
- **Dual-write for ComicInfo** (Pitfall 3): emits BOTH v2.0 `<ScanInformation>` AND v2.1 `<Translator>` so v2.0-only readers (Kavita ambiguous) see fallback AND v2.1-aware readers (Komga 1.10+) see preferred field.
- **InvariantCulture for numerics** (Pitfall 7): `<Number>` always uses `chapter.Number.ToString("0.###", CultureInfo.InvariantCulture)` so `de-DE` doesn't format `132.5` as `"132,5"`.
- **No xmlns**: anansi-project XSDs declare no `targetNamespace`; emitted XML is plain `<ComicInfo>...</ComicInfo>`.

## Manga Adaptation Notes

- **Mangarr divergence**: Mangarr writes per-`MetadataDefinition` provider files via `MetadataService` + `IMetadataDefinition`; Mangarr's chapter-archive metadata is pluggable via `IMetadataWriter`. See `DIVERGENCE.md`.

## Cross-References

- [.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md](../../../../../.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md) — D-14 verbatim
- [ComicInfo/ComicInfoMetadataWriter.cs](./ComicInfo/ComicInfoMetadataWriter.cs)
