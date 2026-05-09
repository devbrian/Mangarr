using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 resource per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: src/Mangarr.Api.V5/Episodes/EpisodeResource.cs (lines 11-42).
//
// Manga sibling preserves: RestResource base + ToResource extension mapper convention.
//
// Manga sibling diverges from EpisodeResource:
//   * Drop SeasonNumber, SceneNumbering*, AirDate*, EpisodeFile subresource
//     (PROJECT.md Volumes/Seasons Out-of-Scope; EpisodeFile-subresource hydration is
//     deferred until a real consumer needs it).
//   * Rename Series→Manga, Episode→Chapter.
//   * Phase 16 STRUCT-08: TranslatedLanguage / ScanlationGroup / IsSynthetic / ReleaseDate
//     are LIFTED to ChapterRelease (per-language data lives there now). The
//     `releases: [...]` collection that surfaces them on the wire lands in Plan 16-05.
//     For Plan 16-02 boundary GREEN they are simply absent from this resource.
//   * Adds FirstReleaseDate (D-02 — Sonarr-mirror of Episode.AirDateUtc; chapter-publish date).
//   * Keeps ChapterType (string enum projection), VolumeNumber (display-only — no Volumes table).
//   * ChapterNumber is decimal (DECIMAL(10,3) per Phase 2 D-12 widen — supports 1.5,
//     1.123, etc.).
//
// Phase-12 follow-up (canonical-resource-reuse, 2026-05-06): added optional
// `Manga` subresource field so `MangaMissingController` / `MangaCutoffController`
// can reuse this canonical DTO instead of shipping their own
// `MissingChapterResource` / `MangaCutoffResource` POCOs (mirrors TV's
// `EpisodeResource.Series?` subresource hydration consumed by `MissingController` /
// `CutoffController`). Hydrated only when the `[FromQuery] *Subresource[]?
// includeSubresources` query contains `Manga` (TV-mirroring enum-array shape;
// see `MangaMissingSubresource` / `MangaCutoffSubresource`).
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
    public string? ChapterType { get; set; }            // Regular / Special / Oneshot / Extra

    // Phase 16 D-02 — Sonarr-mirror of Episode.AirDateUtc; chapter-publish date.
    // TODO(plan-16-05): add `releases: [...]` collection per STRUCT-08 — per-language
    // ChapterRelease projection lifted off the canonical Chapter row.
    public DateTime? FirstReleaseDate { get; set; }
    public bool Monitored { get; set; }
    public bool HasFile => ChapterFileId.HasValue;
    public string? ExternalId { get; set; }

    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06): optional Manga
    // subresource hydrated by MangaMissingController / MangaCutoffController when
    // `includeSubresources=Manga` is passed (mirrors TV's EpisodeResource.Series? +
    // MissingController/CutoffController `MissingSubresource.Series` /
    // `CutoffSubresource.Series` enum-array pattern). Null by default to preserve
    // existing ChapterController callers' wire shape.
    public MangaSubresource? Manga { get; set; }
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
        ChapterType = model.ChapterType.ToString(),
        FirstReleaseDate = model.FirstReleaseDate,
        Monitored = model.Monitored,
        ExternalId = model.ExternalId,
    };

    public static List<ChapterResource> ToResource(this IEnumerable<NzbDrone.Core.Manga.Chapter> models)
        => models.Select(ToResource).ToList();
}
