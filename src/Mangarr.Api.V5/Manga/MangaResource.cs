using NzbDrone.Core.MediaCover;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga;

// Phase 2 developer-surface DTO per Plan 02-10. Mirrors Sonarr's SeriesResource shape
// (Mangarr.Api.V5/Series/SeriesResource.cs) but diverges on cross-source ID typing per
// 02-CONTEXT.md specifics: SINGULAR `Guid? MangaDexId`, `int? MalId`, `int? AniListId`
// (manga is 1:1 across sources, unlike anime which uses HashSets on Series).
public class MangaResource : RestResource
{
    public string? Title { get; set; }
    public string? CleanTitle { get; set; }
    public string? SortTitle { get; set; }

    // URL-safe identifier — the frontend's /manga/:titleSlug route at
    // MangaDetailsPage.tsx:28-31 + the MangaIndexOverview link at line 114
    // do `findIndex(m => m.titleSlug === titleSlug)` on the resource list,
    // so omitting this field makes every detail-page navigation render MIA.
    public string? TitleSlug { get; set; }
    public string? Status { get; set; }
    public string? ContentRating { get; set; }
    public string? Overview { get; set; }
    public List<MediaCover>? Images { get; set; }
    public List<string>? Genres { get; set; }
    public string? Path { get; set; }
    public string? RootFolderPath { get; set; }
    public bool Monitored { get; set; }
    public DateTime Added { get; set; }
    public DateTime? LastInfoSync { get; set; }
    public HashSet<int>? Tags { get; set; }

    // SINGULAR cross-source IDs per 02-CONTEXT.md specifics (vs Series's pluralized lists).
    public Guid? MangaDexId { get; set; }
    public int? MalId { get; set; }
    public int? AniListId { get; set; }

    // Cross-source confirm axes (read-only display; populated by primary metadata source).
    public int? TotalChapterCount { get; set; }
    public int? PublicationYear { get; set; }
    public string? PrimaryAuthor { get; set; }

    // Computed at GET time by MangaController.MapResource — drives the UI library
    // tile's chapter progress bar (`X / Y (Total: …)`). The field set carries
    // both the canonical chapter-shape names and the verbatim TV-shape aliases
    // (episodeCount / episodeFileCount) that the inherited Phase 7 MangaIndexPoster
    // / MangaIndexOverview / MangaIndexRow components still read pre-Phase-8.
    // F-05 from quick-260507-tff-rerun: the field was missing entirely so the
    // frontend defaulted episodeCount/episodeFileCount to 0 and showed `0 / 0`
    // even when chapters had landed on disk.
    public MangaStatisticsResource? Statistics { get; set; }
}

public class MangaStatisticsResource
{
    public int ChapterCount { get; set; }
    public int ChapterFileCount { get; set; }
    public int TotalChapterCount { get; set; }
    public int MonitoredChapterCount { get; set; }
    public long SizeOnDisk { get; set; }

    // TV-shape aliases — the Phase 7 MangaIndexPoster + MangaIndexOverview
    // components inherit verbatim from Series/Index/* and read these names.
    // Phase 8 collapse will rename consumers to the chapter-shape names and
    // drop these aliases.
    public int EpisodeCount { get; set; }
    public int EpisodeFileCount { get; set; }
    public int TotalEpisodeCount { get; set; }
    public int MonitoredEpisodeCount { get; set; }
    public int SeasonCount { get; set; }
}

public static class MangaResourceMapper
{
    public static MangaResource? ToResource(this NzbDrone.Core.Manga.Manga? model)
    {
        if (model == null)
        {
            return null;
        }

        return new MangaResource
        {
            Id = model.Id,
            Title = model.Title,
            CleanTitle = model.CleanTitle,
            SortTitle = model.SortTitle,
            TitleSlug = model.TitleSlug,
            Status = model.Status,
            ContentRating = model.ContentRating,
            Overview = model.Overview,
            Images = model.Images,
            Genres = model.Genres,
            Path = model.Path,
            RootFolderPath = model.RootFolderPath,
            Monitored = model.Monitored,
            Added = model.Added,
            LastInfoSync = model.LastInfoSync,
            Tags = model.Tags,
            MangaDexId = model.MangaDexId,
            MalId = model.MalId,
            AniListId = model.AniListId,
            TotalChapterCount = model.TotalChapterCount,
            PublicationYear = model.PublicationYear,
            PrimaryAuthor = model.PrimaryAuthor,
        };
    }

    public static NzbDrone.Core.Manga.Manga? ToModel(this MangaResource? resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new NzbDrone.Core.Manga.Manga
        {
            Id = resource.Id,
            Title = resource.Title,
            CleanTitle = resource.CleanTitle,
            SortTitle = resource.SortTitle,
            TitleSlug = resource.TitleSlug,
            Status = resource.Status,
            ContentRating = resource.ContentRating,
            Overview = resource.Overview,
            Images = resource.Images ?? new List<MediaCover>(),
            Genres = resource.Genres ?? new List<string>(),
            Path = resource.Path,
            RootFolderPath = resource.RootFolderPath,
            Monitored = resource.Monitored,
            Added = resource.Added,
            LastInfoSync = resource.LastInfoSync,
            Tags = resource.Tags ?? new HashSet<int>(),
            MangaDexId = resource.MangaDexId,
            MalId = resource.MalId,
            AniListId = resource.AniListId,
            TotalChapterCount = resource.TotalChapterCount,
            PublicationYear = resource.PublicationYear,
            PrimaryAuthor = resource.PrimaryAuthor,
        };
    }
}
