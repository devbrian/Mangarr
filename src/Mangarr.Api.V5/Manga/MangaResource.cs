using Mangarr.Http.REST;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaCover;

namespace Mangarr.Api.V5.Manga;

// Phase 2 developer-surface DTO per Plan 02-10. Mirrors Mangarr's SeriesResource shape
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

    // Issue #28: three fields deferred from PR #27's single-Manga Edit modal because
    // they were not round-tripping end-to-end. All three now flow through ToResource /
    // ToModel + Manga.ApplyChanges so the Edit modal can persist them via PUT.
    //   * MonitorNewItems — mirrors Sonarr's SeriesResource.MonitorNewItems (commit ade40b72b).
    //   * TranslationProfileId / CustomFormatProfileId — already on the core Manga model
    //     since Phase 5 D-01 + D-07 but were not surfaced on the wire pre-#28.
    public MangaMonitorNewItems MonitorNewItems { get; set; }
    public int? TranslationProfileId { get; set; }
    public int? CustomFormatProfileId { get; set; }

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

    // Phase 24 v1.1 INSERTED 2026-05-17 — new manga axes per D-03 (AuthorArtistSpec)
    // + D-04 (DemographicSpec). Round-tripped end-to-end via MangaResourceMapper.
    public string? Artist { get; set; }
    public MangaDemographic? Demographic { get; set; }

    // Bug fix new-manga-default-monitored (2026-05-08): carry the user's post-add
    // Monitor + Search* choices through to NzbDrone.Core.Manga.Manga.AddOptions
    // so MangaScannedHandler can apply the per-Chapter monitor cascade. Without
    // this field on the wire, the frontend AddNewMangaModal's `monitor` /
    // `searchForMissingChapters` keys were silently dropped at JSON deserialization
    // → AddOptions == null → MangaScannedHandler bypassed SetChapterMonitoredStatus
    // entirely, so newly-added manga had ALL chapters unmonitored regardless of
    // the user's Monitor=All choice. Mirrors Sonarr's SeriesResource.AddOptions
    // (TV pattern: Series.AddOptions populated at AddSeries POST → SeriesScannedHandler
    // applies per-Episode monitor flags). Round-tripped on GET so PUT-as-resource-mirror
    // semantics survive.
    public AddMangaOptionsResource? AddOptions { get; set; }

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

    // TV-shape aliases — Plan 07-04 historical lock; verbatim-inheritance
    // unwound by Phase 17.3 D-13/D-14. Consumers migrate to chapter-shape
    // names; these aliases remain for wire-shape backward compatibility.
    public int EpisodeCount { get; set; }
    public int EpisodeFileCount { get; set; }
    public int TotalEpisodeCount { get; set; }
    public int MonitoredEpisodeCount { get; set; }
    public int SeasonCount { get; set; }
}

// Bug fix new-manga-default-monitored (2026-05-08): wire-shape mirror of
// `NzbDrone.Core.Manga.AddMangaOptions` (the IEmbeddedDocument persisted on Manga).
// Carries the post-add Monitor cascade choices through the AddManga POST.
// Mirrors Sonarr's TV-side AddSeriesOptionsResource shape; manga sibling
// preserves the IgnoreChaptersWith*Files flags for v2 ImportLists pre-pop
// support but does not surface them in the v1 Add Manga UI.
public class AddMangaOptionsResource
{
    public MangaMonitor Monitor { get; set; }
    public bool SearchForMissingChapters { get; set; }
    public bool SearchForCutoffUnmetChapters { get; set; }
    public bool IgnoreChaptersWithFiles { get; set; }
    public bool IgnoreChaptersWithoutFiles { get; set; }
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
            MonitorNewItems = model.MonitorNewItems,
            TranslationProfileId = model.TranslationProfileId,
            CustomFormatProfileId = model.CustomFormatProfileId,
            Added = model.Added,
            LastInfoSync = model.LastInfoSync,
            Tags = model.Tags,
            MangaDexId = model.MangaDexId,
            MalId = model.MalId,
            AniListId = model.AniListId,
            TotalChapterCount = model.TotalChapterCount,
            PublicationYear = model.PublicationYear,
            PrimaryAuthor = model.PrimaryAuthor,
            Artist = model.Artist,
            Demographic = model.Demographic,
            AddOptions = model.AddOptions == null ? null : new AddMangaOptionsResource
            {
                Monitor = model.AddOptions.Monitor,
                SearchForMissingChapters = model.AddOptions.SearchForMissingChapters,
                SearchForCutoffUnmetChapters = model.AddOptions.SearchForCutoffUnmetChapters,
                IgnoreChaptersWithFiles = model.AddOptions.IgnoreChaptersWithFiles,
                IgnoreChaptersWithoutFiles = model.AddOptions.IgnoreChaptersWithoutFiles,
            },
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
            MonitorNewItems = resource.MonitorNewItems,
            TranslationProfileId = resource.TranslationProfileId,
            CustomFormatProfileId = resource.CustomFormatProfileId,
            Added = resource.Added,
            LastInfoSync = resource.LastInfoSync,
            Tags = resource.Tags ?? new HashSet<int>(),
            MangaDexId = resource.MangaDexId,
            MalId = resource.MalId,
            AniListId = resource.AniListId,
            TotalChapterCount = resource.TotalChapterCount,
            PublicationYear = resource.PublicationYear,
            PrimaryAuthor = resource.PrimaryAuthor,
            Artist = resource.Artist,
            Demographic = resource.Demographic,
            AddOptions = resource.AddOptions == null ? null : new AddMangaOptions
            {
                Monitor = resource.AddOptions.Monitor,
                SearchForMissingChapters = resource.AddOptions.SearchForMissingChapters,
                SearchForCutoffUnmetChapters = resource.AddOptions.SearchForCutoffUnmetChapters,
                IgnoreChaptersWithFiles = resource.AddOptions.IgnoreChaptersWithFiles,
                IgnoreChaptersWithoutFiles = resource.AddOptions.IgnoreChaptersWithoutFiles,
            },
        };
    }
}
