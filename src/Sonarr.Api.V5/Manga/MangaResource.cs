using NzbDrone.Core.MediaCover;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga;

// Phase 2 developer-surface DTO per Plan 02-10. Mirrors Sonarr's SeriesResource shape
// (Sonarr.Api.V5/Series/SeriesResource.cs) but diverges on cross-source ID typing per
// 02-CONTEXT.md specifics: SINGULAR `Guid? MangaDexId`, `int? MalId`, `int? AniListId`
// (manga is 1:1 across sources, unlike anime which uses HashSets on Series).
public class MangaResource : RestResource
{
    public string? Title { get; set; }
    public string? CleanTitle { get; set; }
    public string? SortTitle { get; set; }
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
