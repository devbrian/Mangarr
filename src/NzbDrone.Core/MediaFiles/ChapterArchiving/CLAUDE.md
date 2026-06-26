# MediaFiles/ChapterArchiving

## Purpose

ComicInfo-metadata + chapter-archive support surface for the manga import pipeline.

> **Phase 39 (Plan 39-02) — the archiver strategy set was RETIRED.** The CBZ/folder archiver
> machinery (`IChapterArchiver` + `IChapterArchiverFactory` + `ChapterArchiverFactory`, `Cbz/CbzChapterArchiver`
> + `CbzArchiveOutputContext`, `Folder/FolderImagesChapterArchiver` + `FolderArchiveOutputContext`)
> existed solely to build CBZ/folder output for the now-retired in-process image downloader. The
> **external manga gateway (Phase 38) delivers finished CBZs**, so no archiver has a surviving caller —
> the whole set was deleted (Open Question #3, empirically adjudicated by grep + build gate).
> `ChapterArchivedEvent.cs` was deleted in Plan 39-01 with the completion-poller cluster.
> **What SURVIVES is the Phase-38 ComicInfo injector + the Phase-30 `IMetadata` substrate dependency
> tree** (the `Metadata/` subtree + the abstract `ArchiveOutputContext` + `ChapterArchiveRequest` they
> consume), which writes/injects ComicInfo.xml into the gateway-delivered CBZ on import. See `DIVERGENCE.md`
> Phase 39 section.


## Key Files (surviving post-Phase-39)

| File | Purpose |
|------|---------|
| `Metadata/` | `IMetadataWriter` sibling contract (D-14) wrapped under the Phase-30 `IMetadata` ThingiProvider substrate. The surviving v1 ComicInfo writer/injector tree: `Metadata/ComicInfo/ComicInfoMetadataWriter`, `ComicInfoCbzInjector` (the Phase-38 injector that writes ComicInfo.xml into the gateway-delivered CBZ), `ComicInfoXmlBuilder`, `AgeRatingMapper`, `IComicInfoCbzInjector`. See [Metadata/CLAUDE.md](./Metadata/CLAUDE.md). |
| `ArchiveOutputContext.cs` | Abstract base so metadata writers compose via `OpenSidecar(filename)`. SURVIVES — consumed by the Phase-30 `IMetadata` substrate (`IMetadata.WriteAsync` / `MetadataBase.WriteAsync` / `ComicInfoMetadata.WriteAsync`) and `ComicInfoMetadataWriter`. Only the concrete `Cbz`/`FolderArchiveOutputContext` subclasses were orphaned + deleted in Phase 39. |
| `ChapterArchiveRequest.cs` | DTO carrying Manga + Chapter + ReleaseInfo + paths + PageCount. SURVIVES — the Phase-38 `ComicInfoCbzInjector` reconstructs a minimal request to feed `ComicInfoXmlBuilder`. |
| `ChapterGrabbedEvent.cs` | Grab event (≥2 live consumers — history/download/pending/monitoring services). |
| `ChapterDownloadFailedEvent.cs` | Terminal failure event (blocklist/history/failed-download/monitoring consumers subscribe). |

## Patterns / Conventions

- **Metadata writer composition** (Phase 30 Plan 30-04 D-02 flip): the surviving Phase-38 `ComicInfoCbzInjector` resolves the enabled metadata writers via `_metadataFactory.Enabled()` (returns `List<IMetadata>` from the Phase-30 ThingiProvider substrate) and injects ComicInfo.xml into the gateway-delivered CBZ on import. Pre-Plan-30-04 the iteration was over DryIoc-auto-discovered metadata-writer providers; the flip routes through the Settings/Metadata enable-toggle. The writer composes via the abstract `ArchiveOutputContext.OpenSidecar` seam.
- **(RETIRED in Phase 39)** the per-`FormatKey` archiver-strategy lookup against `Config.OutputFormat` + the `.tmp`→final atomic-rename CBZ/folder write path went away with the in-process downloader; the gateway delivers a finished CBZ, and the injector edits it in place.
- **Golden-fixture convention**: binary CBZ + XML golden fixtures are committed in-project under `src/NzbDrone.Core.Test/Files/Phase04/golden-cbz/` and `src/NzbDrone.Core.Test/Files/Phase04/golden-folder/` (csproj `<None Update="Files\Phase04\...">` with `CopyToOutputDirectory=PreserveNewest`). Tests resolve them at runtime via `TestContext.CurrentContext.TestDirectory + "Files" + "Phase04" + ...`. They assert produced output's *entry-list shape* (names + sizes + compression method) matches golden — NOT byte-equal (`ZipArchive` timestamps drift across runs). NOTE: fixtures must NOT live under `_tests/` — that path is the build-OUTPUT dir (`.gitignore` `_tests*`) and CI's `Build Backend` step does `rm -rf _tests` before `dotnet msbuild` (relocated out of `_tests/Fixtures/Phase04/` in the `github-pipeline-backend-builds` debug session, 2026-05-14).

## Manga Adaptation Notes

- **No Sonarr peer for chapter-archive metadata**: this directory is greenfield. Sonarr handles video files; manga injects ComicInfo.xml into archive payloads.
- **Post-Phase-39 role**: the in-process archiver strategy is gone; what remains is the ComicInfo-injection surface applied to the CBZ the external gateway delivers. No TV peer to collapse with.
- **`IMetadataWriter` is a Phase 4 divergence** from Sonarr's per-`MetadataDefinition` provider pattern (`Extras/`), now wrapped under the Phase-30 `IMetadata` substrate. See `DIVERGENCE.md`.

## Cross-References

- [Metadata/CLAUDE.md](./Metadata/CLAUDE.md) — the surviving ComicInfo writer/injector subtree
- [Metadata/ComicInfo/ComicInfoCbzInjector.cs](./Metadata/ComicInfo/ComicInfoCbzInjector.cs) — Phase-38 injector that writes ComicInfo.xml into the gateway-delivered CBZ
- [../../Download/Clients/Gateway/GatewayDownloadClient.cs](../../Download/Clients/Gateway/GatewayDownloadClient.cs) — the external download client that delivers the finished CBZ this subtree injects into (the in-process archiver-orchestrator was retired in Phase 39)
- `DIVERGENCE.md` Phase 39 section — the archiver-set + `ChapterArchivedEvent` retirement record
