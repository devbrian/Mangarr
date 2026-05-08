using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 resource per Phase 13 Plan 13-07 (D-13-04
// forward-prophylactic + D-13-07 Series-rename family rule). See DIVERGENCE.md.
// Role-match analog: src/Mangarr.Api.V5/EpisodeFiles/EpisodeFileResource.cs.
//
// Manga sibling preserves: RestResource base + ToResource extension mapper convention.
//
// Manga sibling diverges from EpisodeFileResource:
//   * Drop SeasonNumber (PROJECT.md "Volumes/Seasons" Out-of-Scope).
//   * Drop QualityModel? Quality + List<Language> Languages + List<CustomFormatResource>
//     CustomFormats + CustomFormatScore + QualityCutoffNotMet (Phase 5 D-05 — manga uses
//     Custom Format + Translation Profile, NOT QualityModel; the cutoff/CF columns belong
//     to MangaCutoffController + the Translation/CustomFormatProfile editors).
//   * Drop SceneName + IndexerFlags + ReleaseType + MediaInfoResource? MediaInfo
//     (TV-only — no scene-release naming, no torrent indexer flags, no video MediaInfo
//     for image archives).
//   * Replace with manga-domain provenance fields per ChapterFile model
//     (src/NzbDrone.Core/MediaFiles/ChapterFile.cs:11-43): MangaId / ChapterId /
//     RelativePath / Path / Size / DateAdded / TranslatedLanguage (BCP-47) /
//     ScanlationGroup / ReleaseGroup.
//
// SignalR resource auto-derivation note: bare [V5ApiController] on the controller falls
// back to `new TResource().ResourceName.Trim('/')` per RestControllerWithSignalR.cs:23-33,
// and RestResource.ResourceName returns `GetType().Name.ToLowerInvariant().Replace("resource", "")`
// per RestResource.cs:11. The runtime-derived value is therefore `chapterfile` (all
// lowercase, no camelCase). The frontend SignalRListener.tsx handler entry MUST match
// this lowercase literal — see Plan 13-07 Task 3.
//
// Phase 8 cleanup: collapse with EpisodeFileResource when Tv/ deletes.
public class ChapterFileResource : RestResource
{
    public int MangaId { get; set; }
    public int ChapterId { get; set; }
    public string? RelativePath { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
    public DateTime DateAdded { get; set; }
    public string? TranslatedLanguage { get; set; }
    public string? ScanlationGroup { get; set; }
    public string? ReleaseGroup { get; set; }
}

public static class ChapterFileResourceMapper
{
    public static ChapterFileResource ToResource(this NzbDrone.Core.MediaFiles.ChapterFile model)
    {
        if (model == null)
        {
            return null!;
        }

        return new ChapterFileResource
        {
            Id = model.Id,
            MangaId = model.MangaId,
            ChapterId = model.ChapterId,
            RelativePath = model.RelativePath,
            Path = model.Path,
            Size = model.Size,
            DateAdded = model.DateAdded,
            TranslatedLanguage = model.TranslatedLanguage,
            ScanlationGroup = model.ScanlationGroup,
            ReleaseGroup = model.ReleaseGroup
        };
    }

    public static List<ChapterFileResource> ToResource(this IEnumerable<NzbDrone.Core.MediaFiles.ChapterFile> models)
    {
        return models.Select(ToResource).ToList();
    }
}
