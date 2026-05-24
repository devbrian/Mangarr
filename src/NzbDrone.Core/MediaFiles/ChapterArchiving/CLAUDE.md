# MediaFiles/ChapterArchiving

## Purpose

Phase 4 archiver plugin contract surface (ARCHIVE-05). Strategy seam selecting between CBZ and folder-of-images output formats based on global `Config.OutputFormat`. Pluggable `IMetadataWriter` sibling contract (`Metadata/`) emits reader-metadata sidecars (v1: ComicInfo.xml dual-write).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MediaFiles\ChapterArchiving`

## Key Files

| File | Purpose |
|------|---------|
| `IChapterArchiver.cs` | Plugin contract (D-13). v1 impls: `Cbz/CbzChapterArchiver`, `Folder/FolderImagesChapterArchiver`. v2 may add CBR / EPUB / raw-image without touching downloader / queue / import. |
| `IChapterArchiverFactory.cs` | Resolves the active archiver by `Config.OutputFormat` with case-insensitive match + `cbz` fallback. |
| `ChapterArchiverFactory.cs` | Impl using `IEnumerable<IChapterArchiver>` DryIoc auto-discovery. |
| `ChapterArchiveRequest.cs` | DTO carrying Manga + Chapter + ReleaseInfo + ScratchDir + StagingDir + OutputFilename + PageCount. |
| `ArchiveOutputContext.cs` | Abstraction so metadata writers compose with both CBZ and folder archivers via `OpenSidecar(filename)`. |
| `Cbz/` | CBZ impl. Streams to a FileStream-backed `ZipArchive` (Pitfall 4 — never MemoryStream); entries use `CompressionLevel.NoCompression` (D-17 Stored on .NET 9+ per dotnet/docs#40299); atomic `.cbz.tmp` → `.cbz` rename (D-16). |
| `Folder/` | Folder-of-images impl. Atomic `.tmp/` → final dir rename. |
| `Metadata/` | `IMetadataWriter` sibling contract (D-14). v1 impl: `Metadata/ComicInfo/ComicInfoMetadataWriter`. |
| `ChapterArchivedEvent.cs` | Phase 6 import handoff event. |
| `ChapterDownloadFailedEvent.cs` | Terminal failure event (Phase 6 History writes subscribe). |

## Patterns / Conventions

- **Plugin contract via FormatKey**: each `IChapterArchiver` has a `string FormatKey`; factory does case-insensitive lookup against `Config.OutputFormat`; falls back to `"cbz"` (ARCHIVE-01 default) on unknown FormatKey.
- **Atomic write**: every archiver writes to a `.tmp` artifact then atomic-renames to the final path. Reader apps never see a half-written CBZ or folder. Same-volume by construction (`StagingDir` is one dir).
- **Metadata writer composition** (Phase 30 Plan 30-04 D-02 flip): archivers iterate `_metadataFactory.Enabled()` (returns `List<IMetadata>` from the new ThingiProvider substrate) INSIDE their archive scope. Pre-Plan-30-04 the iteration was over DryIoc-auto-discovered metadata-writer providers; the flip routes through the Settings/Metadata enable-toggle. CBZ's `CbzArchiveOutputContext` opens `ZipArchive` entries; folder's `FolderArchiveOutputContext` opens `FileStream`s under the staging dir.
- **Golden-fixture convention**: binary CBZ + XML golden fixtures are committed in-project under `src/NzbDrone.Core.Test/Files/Phase04/golden-cbz/` and `src/NzbDrone.Core.Test/Files/Phase04/golden-folder/` (csproj `<None Update="Files\Phase04\...">` with `CopyToOutputDirectory=PreserveNewest`). Tests resolve them at runtime via `TestContext.CurrentContext.TestDirectory + "Files" + "Phase04" + ...`. They assert produced output's *entry-list shape* (names + sizes + compression method) matches golden — NOT byte-equal (`ZipArchive` timestamps drift across runs). NOTE: fixtures must NOT live under `_tests/` — that path is the build-OUTPUT dir (`.gitignore` `_tests*`) and CI's `Build Backend` step does `rm -rf _tests` before `dotnet msbuild` (relocated out of `_tests/Fixtures/Phase04/` in the `github-pipeline-backend-builds` debug session, 2026-05-14).

## Manga Adaptation Notes

- **No Mangarr peer for archivers**: this directory is greenfield. Mangarr handles video files (`MediaFiles/EpisodeImport/`); manga handles archive packaging.
- **Phase 8 collapse**: stays as-is. No TV peer to collapse with.
- **`IMetadataWriter` is a Phase 4 divergence** from Mangarr's per-`MetadataDefinition` provider pattern (`Extras/`). See `DIVERGENCE.md`.

## Cross-References

- [Cbz/CbzChapterArchiver.cs](./Cbz/CbzChapterArchiver.cs)
- [Folder/FolderImagesChapterArchiver.cs](./Folder/FolderImagesChapterArchiver.cs)
- [Metadata/CLAUDE.md](./Metadata/CLAUDE.md)
- [src/NzbDrone.Core/Download/Clients/InProcess/CLAUDE.md](../../Download/Clients/InProcess/CLAUDE.md) — orchestrator that invokes the archiver factory
