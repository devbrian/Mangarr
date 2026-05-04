using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 resource per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/Episodes/EpisodeResource.cs (lines 11-42).
//
// Manga sibling preserves: RestResource base + ToResource extension mapper convention.
//
// Manga sibling diverges from EpisodeResource:
//   * Drop SeasonNumber, SceneNumbering*, AirDate*, EpisodeFile subresource, Series
//     subresource (PROJECT.md Volumes/Seasons Out-of-Scope; subresource hydration is
//     deferred until a real consumer needs it).
//   * Rename Series→Manga, Episode→Chapter.
//   * Add TranslatedLanguage (BCP-47), ScanlationGroup, IsSynthetic, ChapterType (string
//     enum projection), VolumeNumber (display-only — no Volumes table).
//   * ChapterNumber is decimal (DECIMAL(10,3) per Phase 2 D-12 widen — supports 1.5,
//     1.123, etc.).
//
// Phase 8 cleanup: collapse with EpisodeResource when Tv/ deletes.
public class ChapterResource : RestResource
{
    public int MangaId { get; set; }
    public int? ChapterFileId { get; set; }
    public decimal ChapterNumber { get; set; }
    public decimal? AbsoluteChapterNumber { get; set; }
    public int? VolumeNumber { get; set; }              // display-only — no Volumes table.
    public string? Title { get; set; }
    public string? TranslatedLanguage { get; set; }     // BCP-47; "und" for synthetic
    public string? ScanlationGroup { get; set; }
    public string? ChapterType { get; set; }            // Regular / Special / Oneshot / Extra
    public bool IsSynthetic { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public bool Monitored { get; set; }
    public bool HasFile => ChapterFileId.HasValue;
    public string? ExternalId { get; set; }
}

public static class ChapterResourceMapper
{
    public static ChapterResource ToResource(this NzbDrone.Core.Manga.Chapter model) => new()
    {
        Id = model.Id,
        MangaId = model.MangaId,
        ChapterFileId = model.ChapterFileId,
        ChapterNumber = model.ChapterNumber,
        AbsoluteChapterNumber = model.AbsoluteChapterNumber,
        VolumeNumber = model.VolumeNumber,
        Title = model.Title,
        TranslatedLanguage = model.TranslatedLanguage,
        ScanlationGroup = model.ScanlationGroup,
        ChapterType = model.ChapterType.ToString(),
        IsSynthetic = model.IsSynthetic,
        ReleaseDate = model.ReleaseDate,
        Monitored = model.Monitored,
        ExternalId = model.ExternalId,
    };

    public static List<ChapterResource> ToResource(this IEnumerable<NzbDrone.Core.Manga.Chapter> models)
        => models.Select(ToResource).ToList();
}
