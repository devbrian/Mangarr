using NzbDrone.Core.MediaFiles.MangaImport.Manual;
using NzbDrone.Core.Parser.Model;

namespace Mangarr.Api.V5.ManualImport;

// Phase 25 Plan 25-02 — V5 ManualImportReprocessResource port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 25-01-PORT-SOURCE.md).
//
// POST-body DTO for `POST /api/v5/manualimport` (the reprocess endpoint backing the
// InteractiveImport modal's row-edit / batch-reprocess flow). Plain class — NOT
// RestResource — this is a transport-only shape, not an addressable resource.
//
// Per 25-01-DTO-MAPPING.md §5 — TV→Manga rename rules applied:
//   SeriesId → MangaId; EpisodeIds → ChapterIds; EpisodeFileId → ChapterFileId;
//   ReleaseGroup → ScanlationGroup; Languages → TranslatedLanguage.
//
// 5 TV symbols intentionally absent: SeriesId, SeasonNumber, EpisodeIds,
// EpisodeFileId, ReleaseGroup. Plan 25-04 Task 7 appends ExistingFileBehavior
// per D-04 (camelCase string roundtrip via STJson global JsonStringEnumConverter).
public class ManualImportReprocessResource
{
    public string? Path { get; set; }
    public string? FolderName { get; set; }
    public int MangaId { get; set; }
    public List<int> ChapterIds { get; set; } = new();
    public int? ChapterFileId { get; set; }
    public string? ScanlationGroup { get; set; }
    public string? TranslatedLanguage { get; set; }
    public int IndexerFlags { get; set; }
    public ReleaseType ReleaseType { get; set; }
    public string? DownloadId { get; set; }

    // Phase 25 Plan 25-04 Task 7 — per-row D-04 dropdown carry-over (camelCase
    // string roundtrip via STJson global JsonStringEnumConverter).
    public ExistingFileBehavior ExistingFileBehavior { get; set; } = ExistingFileBehavior.Skip;
}
