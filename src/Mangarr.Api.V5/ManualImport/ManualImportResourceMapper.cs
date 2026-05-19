using Mangarr.Api.V5.Manga;
using Mangarr.Api.V5.Manga.Chapter;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;

namespace Mangarr.Api.V5.ManualImport;

// Phase 25 Plan 25-02 — V5 ManualImportResourceMapper port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 25-01-PORT-SOURCE.md).
//
// Static-extension shape mirrors Phase 24's AutoTaggingResourceMapper precedent
// (separate file from the Resource definition). Pure 1:1 field copy — no business
// logic, no inline computations. Null-safe on every collection-shaped property.
//
// ToResource(ManualImportItem) → ManualImportResource: hydrates nested MangaResource
// via existing MangaResourceMapper.ToResource extension; hydrates each ChapterResource
// via ChapterResourceMapper.ToResource extension. Rejections / CustomFormats / Chapters
// fall back to empty enumerables when null so the FE table never NREs on a row.
//
// ToModel(ManualImportReprocessResource) → ManualImportFile: returns the POST-body
// shape mapped into the backend bulk-input POCO. Plan 25-02 Task 3 appends the
// matching `ReprocessItems(List<ManualImportFile>)` method on ManualImportService.
//
// Phase 25 Plan 25-04 Task 7 — ExistingFileBehavior mapping appended per
// 25-01-DTO-MAPPING.md §3 row 20 + §5 last row. Round-trips both directions
// (ToResource emits, ToModel consumes).
public static class ManualImportResourceMapper
{
    public static ManualImportResource ToResource(this ManualImportItem model)
    {
        // Codex review P1.1 — derive a deterministic per-row Id by hashing
        // the Path. Mirrors Sonarr V3 `ManualImportResource.ToResource`
        // (HashConverter.GetHashInt31 — pinned SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160).
        // Without this, every row serialized as `id: 0`, and the FE table
        // (which keys selection by `item.id`) treated all rows as a single
        // selection group — selecting one row marked every row selected and
        // the import loop processed all of them.
        //
        // Codex review P1.2 — derive the `kind` discriminator from the call
        // shape so the FE's typed discriminated union narrows correctly
        // (`InteractiveImportContent.tsx:557-562` strips downloadId /
        // chapterFileId when kind is undefined; the queue + existing-library
        // import paths silently lost context without this field).
        //   * ChapterFileId.HasValue       → "manga-imported" (existing library row)
        //   * DownloadId is non-empty       → "queue-source"   (active-download row)
        //   * otherwise                     → "folder-source"  (folder-scan row)
        var kind = model.ChapterFileId.HasValue
            ? "manga-imported"
            : model.DownloadId.IsNotNullOrWhiteSpace()
                ? "queue-source"
                : "folder-source";

        return new ManualImportResource
        {
            Id = HashConverter.GetHashInt31(model.Path),
            Kind = kind,
            Path = model.Path,
            RelativePath = model.RelativePath,
            FolderName = model.FolderName,
            Name = model.Name,
            Size = model.Size,
            Manga = model.Manga?.ToResource(),
            Chapters = model.Chapters?.Select(c => c.ToResource()).ToList() ?? new List<ChapterResource>(),
            ChapterFileId = model.ChapterFileId,
            TranslatedLanguage = model.TranslatedLanguage,
            ScanlationGroup = model.ScanlationGroup,
            DownloadId = model.DownloadId,
            CustomFormats = model.CustomFormats ?? new(),
            CustomFormatScore = model.CustomFormatScore,
            IndexerFlags = model.IndexerFlags,
            ReleaseType = model.ReleaseType,
            Rejections = model.Rejections ?? Enumerable.Empty<MangaImportRejection>(),
            ExistingFileBehavior = model.ExistingFileBehavior
        };
    }

    public static List<ManualImportResource> ToResource(this IEnumerable<ManualImportItem> models)
    {
        return models?.Select(m => m.ToResource()).ToList() ?? new List<ManualImportResource>();
    }

    public static ManualImportFile ToModel(this ManualImportReprocessResource resource)
    {
        return new ManualImportFile
        {
            Path = resource.Path,
            FolderName = resource.FolderName,
            MangaId = resource.MangaId,
            ChapterIds = resource.ChapterIds ?? new List<int>(),
            ChapterFileId = resource.ChapterFileId,
            ScanlationGroup = resource.ScanlationGroup,
            IndexerFlags = resource.IndexerFlags,
            ReleaseType = resource.ReleaseType,
            DownloadId = resource.DownloadId,
            ExistingFileBehavior = resource.ExistingFileBehavior
        };
    }

    public static List<ManualImportFile> ToModel(this IEnumerable<ManualImportReprocessResource> resources)
    {
        return resources?.Select(r => r.ToModel()).ToList() ?? new List<ManualImportFile>();
    }
}
