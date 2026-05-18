using System.Text.Json.Serialization;
using Mangarr.Api.V5.Manga;
using Mangarr.Api.V5.Manga.Chapter;
using Mangarr.Http.REST;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Parser.Model;

namespace Mangarr.Api.V5.ManualImport;

// Phase 25 Plan 25-02 — V5 ManualImportResource port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 25-01-PORT-SOURCE.md).
//
// Verbatim field mirror of NzbDrone.Core.MediaFiles.MangaImport.Manual.ManualImportItem
// (the backend POCO at src/NzbDrone.Core/MediaFiles/MangaImport/Manual/ManualImportItem.cs).
// Field-by-field shape is locked by 25-01-DTO-MAPPING.md §3 — the reviewer-signed
// 20-row TV→Manga mapping table. Id is force-serialized via JsonIgnoreCondition.Never
// (AutoTaggingResource precedent) so the FE table-key works on default-0 ephemeral rows.
//
// Pitfall 1 — 8 TV symbols intentionally absent: Series, SeasonNumber, Episodes,
// EpisodeFileId, Quality, QualityWeight, ReleaseGroup, Languages.
// Phase 16.1 D-04 collapses TV List<Language> Languages → single TranslatedLanguage string.
// Phase 16.1 D-06 renames TV ReleaseGroup → ScanlationGroup.
// Phase 15 D-04 drops TV QualityModel / QualityWeight (no manga quality axis).
// PROJECT.md DOMAIN-02 drops SeasonNumber (no manga season concept).
//
// Sonarr divergence: ExistingFileBehavior field is INTENTIONALLY ABSENT in 25-02.
// Plan 25-04 D-04 appends ExistingFileBehavior at the same time the per-row "On
// Existing File" dropdown lands. See 25-01-DTO-MAPPING.md §3 row 20.
public class ManualImportResource : RestResource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override int Id { get; set; }

    public string? Path { get; set; }
    public string? RelativePath { get; set; }
    public string? FolderName { get; set; }
    public string? Name { get; set; }
    public long Size { get; set; }
    public MangaResource? Manga { get; set; }
    public List<ChapterResource> Chapters { get; set; } = new();
    public int? ChapterFileId { get; set; }
    public string? TranslatedLanguage { get; set; }
    public string? ScanlationGroup { get; set; }
    public string? DownloadId { get; set; }
    public List<CustomFormat> CustomFormats { get; set; } = new();
    public int CustomFormatScore { get; set; }
    public int IndexerFlags { get; set; }
    public ReleaseType ReleaseType { get; set; }
    public IEnumerable<MangaImportRejection> Rejections { get; set; } = new List<MangaImportRejection>();
}
