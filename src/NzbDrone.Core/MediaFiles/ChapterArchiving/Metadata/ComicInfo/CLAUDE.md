# MediaFiles/ChapterArchiving/Metadata/ComicInfo

## Purpose

ComicInfo.xml emission for manga chapters. Two write surfaces coexist:

1. **In-archive writer** (Phase 4, legacy) — `ComicInfoMetadataWriter` streams ComicInfo.xml
   into the open `ZipArchive` *during* in-process CBZ construction (`CbzChapterArchiver`).
2. **Post-import injector** (Phase 38 CINFO-01, NEW) — `ComicInfoCbzInjector` opens an
   ALREADY-FINISHED CBZ with `ZipArchiveMode.Update` and upserts a single ComicInfo.xml entry
   at import-step 3.5. This is the SOLE writer for gateway-delivered (page-images-only) CBZs.
   Phase 39 retires the in-process archiver path, leaving the injector as the only ComicInfo
   writer.

Both surfaces reuse the pure `ComicInfoXmlBuilder` + `AgeRatingMapper` verbatim (both MUST
survive Phase 39).


## Key Files

| File | Purpose |
|------|---------|
| `ComicInfoXmlBuilder.cs` | Pure `XDocument` construction (no I/O). Dual-write v2.0 `<ScanInformation>` + v2.1 `<Translator>` (Pitfall 3); InvariantCulture numerics (Pitfall 7); auto-escaped element text (T-04-16). Emits `<PageCount>0</PageCount>` even at 0 — callers MUST pass a real page count. |
| `AgeRatingMapper.cs` | MangaDex `contentRating` → ComicInfo `<AgeRating>` enum. |
| `ComicInfoMetadataWriter.cs` | Phase 4 legacy in-archive writer (FormatKey `"comicinfo"`); wrapped by the Phase 30 `ComicInfoMetadata` ThingiProvider. Retired in Phase 39. |
| `IComicInfoCbzInjector.cs` / `ComicInfoCbzInjector.cs` | **Phase 38 Plan 38-02 (CINFO-01)** — post-import ComicInfo.xml injector. See below. |

## ComicInfoCbzInjector (Phase 38 CINFO-01)

### Wire site — `ImportApprovedChapters` step-3.5

`ImportApprovedChapters.Import` calls `_comicInfoCbzInjector.Inject(chapterFile, lc.Manga, lc.Chapter)`
at **step 3.5b** of the per-decision success path:

- AFTER step 3 `_chapterFileService.Add(chapterFile)` (DB commit — `chapterFile.Path` is populated)
- AFTER the step-3.5 `_updateChapterInfoService.Update(...)` ImageSharp probe (so a probe-populated
  `MediaInfo` is committed first; ordering vs probe is NOT a correctness constraint — D-B2)
- **STRICTLY BEFORE** step 6 `_eventAggregator.PublishEvent(new ChapterImportedEvent {...})`
  — the **Pitfall 4 LAST-LINE invariant**. Komga/Kavita rescan handlers fire on
  `ChapterImportedEvent`; they MUST see a metadata-complete archive.

### D-A — always-upsert, no gate

The injector runs on EVERY CBZ import **unconditionally** — no gateway-only gate, no
download-client-type discriminator, no ComicInfo-presence check. The call is NOT wrapped in its
own swallow-everything try/catch (UNLIKE the non-fatal ImageSharp probe immediately above it).

### Upsert mechanics (D-A2 + D-C1 + D-C2)

- Opens `chapterFile.Path` with `ZipFile.Open(path, ZipArchiveMode.Update)` (NOT `Create` — page
  images are not re-read or re-compressed).
- `<PageCount>` = real image-entry count via `IsImageEntry` (jpg/jpeg/png/webp/gif/avif,
  excluding `ComicInfo.xml`) — D-C1 (the builder would emit `<PageCount>0</PageCount>` otherwise).
- Metadata sourced from a MINIMAL `ReleaseInfo` reconstructed from the persisted
  `ChapterFile.ScanlationGroup` / `ChapterFile.TranslatedLanguage` (D-C2 — these already carry the
  `lc.ScanlationGroup ?? lc.Release?.ScanlationGroup ?? lc.Release?.Indexer` resolution chain
  applied at import-step 3).
- Delete-then-create upsert: `zip.GetEntry("ComicInfo.xml")?.Delete()` then `CreateEntry` +
  `xml.Save(stream)` — guarantees exactly one `ComicInfo.xml` entry (no duplicate).
- D-D: only the `.cbz` path exists (cbt/folder deferred).

### D-B — Warn → retry-once-after-short-delay → fatal ladder

The upsert is wrapped in a try. First failure → log `Warn` + `Thread.Sleep(500ms)` (the
AV / cloud-sync / Windows-Search file-lock window; Pitfall 4 — never block long, the import loop
is synchronous per-decision) + retry once. Second failure → **RE-THROW** (fatal).

### D-B1 — fatal failures use the EXISTING ChapterImportFailedEvent publish (no duplicate)

The injector itself NEVER publishes any event. A persisted (second) failure propagates to
`ImportApprovedChapters`'s existing terminal `catch (Exception ex)` block, which publishes
`ChapterImportFailedEvent` ONCE — so the chapter does NOT land and the Phase-36 auto-retry
orchestrator handles it. No duplicate `ChapterImportFailedEvent` publish was added.

The enclosing catch ladder verified at HEAD: specific `RootFolderNotFoundException`,
`DestinationAlreadyExistsException` (no failed-event publish — queues a rescan), and
`RecycleBinException` catches, followed by a **terminal general `catch (Exception ex)`** that
publishes `ChapterImportFailedEvent`. The injector's fatal failure (an `IOException` or similar)
falls through to this terminal general catch.

### Auto-registration

DryIoc auto-discovers `ComicInfoCbzInjector` by convention (no manual registration), mirroring the
`IUpdateChapterInfo` precedent.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — Metadata writer plugin contract overview
- [../../../MangaImport/CLAUDE.md](../../../MangaImport/CLAUDE.md) — `ImportApprovedChapters` step-3.5 wire site + Pitfall 4 ordering invariant
- [ComicInfoXmlBuilder.cs](./ComicInfoXmlBuilder.cs) — the pure builder reused verbatim
- _(historical)_ `../../Cbz/CbzChapterArchiver.cs` — the Create-mode `ZipArchive` write idiom the injector diverged from (Update mode); **the archiver was deleted in Phase 39** along with the in-process downloader
