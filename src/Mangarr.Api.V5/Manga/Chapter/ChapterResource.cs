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
//   * Phase 16 STRUCT-08 (REWRITTEN per Plan 16-05): pre-Phase-16 ChapterResource
//     carried 4 per-language fields (TranslatedLanguage, ScanlationGroup, IsSynthetic,
//     ReleaseDate) directly on the canonical resource because the underlying Chapter row
//     was per-(MangaId, ChapterNumber, TranslatedLanguage, ScanlationGroup). Post-Phase-16,
//     Chapter is canonical at (MangaId, ChapterNumber); per-language data lives on the new
//     Releases nested collection (one ChapterReleaseResource per ChapterRelease row).
//     Mirrors RemoteEpisode's per-release pattern at the wire boundary.
//   * Adds FirstReleaseDate (D-02 — Sonarr-mirror of Episode.AirDateUtc; chapter-publish date).
//   * Adds Releases (STRUCT-08 — per-canonical-chapter ChapterRelease projection;
//     hydrated by ChapterController via N+1-safe bulk load + GroupBy in memory).
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
    // Distinct from ChapterReleaseResource.ReleaseDate (per-translation upload time).
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

    // Phase 16 STRUCT-08 — per-canonical-chapter releases collection.
    // Hydrated by ChapterController.GetChapters via N+1-safe bulk load + GroupBy in memory
    // (single IChapterReleaseService.GetReleasesByMangaId / GetReleasesByChapterIds call
    // per request, then GroupBy(ChapterId) → ToDictionary; per-Chapter resource lookup is O(1)).
    // Empty list (NOT null) when the canonical Chapter has 0 ChapterRelease rows — the
    // D-04 alias-flip "Wanted/Missing = no ChapterRelease rows OR releases-without-file".
    // camelCase keys (`releases: [...]`) per Newtonsoft.Json default contract resolver convention.
    public List<ChapterReleaseResource> Releases { get; set; } = new();
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

        // Note: Releases is left as the empty default list `new()`; ChapterController
        // hydrates per-resource after a SINGLE bulk fetch (RESEARCH §Pitfall N+1).
    };

    public static List<ChapterResource> ToResource(this IEnumerable<NzbDrone.Core.Manga.Chapter> models)
        => models.Select(ToResource).ToList();
}
