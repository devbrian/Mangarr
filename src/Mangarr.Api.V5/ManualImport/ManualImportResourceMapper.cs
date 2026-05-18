using Mangarr.Api.V5.Manga;
using Mangarr.Api.V5.Manga.Chapter;
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
        return new ManualImportResource
        {
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
