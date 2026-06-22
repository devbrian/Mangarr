using NzbDrone.Core.Discovery;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 Plan 42-04 (DISC-02 / DISC-03) — the Discovery response DTOs + mappers.
//
// DiscoverySearchResponseResource is the POST /search envelope: the eligible cards plus
// the pool-exhaustion signal the UI renders as "found N of X" (D-06). DiscoveryResultResource
// is the slim grid card. DiscoveryGenreResource / DiscoveryTagResource are the cached
// option-list rows for the filter UI (GET /genres, GET /tags).

public class DiscoverySearchResponseResource
{
    public List<DiscoveryResultResource> Results { get; set; } = [];

    // True when the MangaBaka pool ran dry before X eligible rows were collected (D-06).
    public bool PoolExhausted { get; set; }

    // The X the caller asked for (echoed for "found N of X").
    public int Requested { get; set; }

    // Count of eligible rows the loop accumulated.
    public int Found { get; set; }
}

public class DiscoveryResultResource
{
    public int MangaBakaId { get; set; }
    public string? Title { get; set; }
    public string? CoverUrl { get; set; }
    public int? Year { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? ContentRating { get; set; }
    public List<string> Genres { get; set; } = [];

    // 0-100 score (decimal? — the MangaBaka wire value is fractional; the card UI rounds
    // at display per Plan 42-01/42-02's decimal? typing).
    public decimal? Score { get; set; }
}

public class DiscoveryGenreResource
{
    // The human-facing label (e.g. "Boys' Love").
    public string? Label { get; set; }

    // The API filter token (e.g. "boys_love") bound to genre / genre_not.
    public string? Value { get; set; }
}

public class DiscoveryTagResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? NamePath { get; set; }
    public int SeriesCount { get; set; }
    public string? ContentRating { get; set; }
}

public static class DiscoveryResourceMapper
{
    public static DiscoveryResultResource ToResource(this DiscoveryResultItem model) => new()
    {
        MangaBakaId = model.MangaBakaId,
        Title = model.Title,
        CoverUrl = model.CoverUrl,
        Year = model.Year,
        Status = model.Status,
        Type = model.Type,
        ContentRating = model.ContentRating,
        Genres = model.Genres ?? [],
        Score = model.Score
    };

    public static DiscoverySearchResponseResource ToResource(this DiscoveryResult model) => new()
    {
        Results = model.Results?.Select(r => r.ToResource()).ToList() ?? [],
        PoolExhausted = model.PoolExhausted,
        Requested = model.Requested,
        Found = model.Found
    };

    public static DiscoveryGenreResource ToResource(this MangaBakaGenre model) => new()
    {
        Label = model.Label,
        Value = model.Value
    };

    public static DiscoveryTagResource ToResource(this MangaBakaTag model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        NamePath = model.NamePath,
        SeriesCount = model.SeriesCount,
        ContentRating = model.ContentRating
    };
}
