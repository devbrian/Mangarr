using NzbDrone.Core.Discovery;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 Plan 42-04 (DISC-02) — the POST /api/v5/discovery/search request payload.
//
// Allow-list DTO (T-42-04-MASS mitigation, MangaEditorResource precedent): only the
// fields enumerated below survive System.Text.Json deserialization; any extra field an
// attacker posts is silently dropped. The values are NOT trusted here — the paired
// DiscoverySearchRequestValidator clamps X (1-100), whitelists the type/status/
// content_rating/sort_by enums, and bounds the year/score ranges BEFORE ToFilter()
// hands the typed filter to DiscoveryService.Search → MangaBakaApi.Browse
// (T-42-04-TAMPER — the controller owns the clamp per 42-02's handoff note).
//
// Wire shape is camelCase after Newtonsoft serialization. Each list maps to a REPEATED
// MangaBaka query param (genre=action&genre=shounen) — NEVER comma-joined (RESEARCH
// Pitfall 2). Tag/TagNot carry integer tag ids (D-10), not names.
public class DiscoverySearchRequestResource
{
    public List<string> Type { get; set; } = [];
    public List<string> TypeNot { get; set; } = [];
    public List<string> Genre { get; set; } = [];
    public List<string> GenreNot { get; set; } = [];

    // Integer tag ids (D-10) — the browse filter binds to the MangaBaka tag id, not its name.
    public List<int> Tag { get; set; } = [];
    public List<int> TagNot { get; set; } = [];

    // "and" (ALL selected tags) or "or" (ANY). Default "and".
    public string TagMode { get; set; } = "and";

    public List<string> Status { get; set; } = [];
    public List<string> StatusNot { get; set; } = [];
    public List<string> ContentRating { get; set; } = [];

    // When false, DiscoveryService injects content_rating=safe&suggestive (D-04/D-05) —
    // the safe-by-default branch is in the SERVICE, never this DTO.
    public bool IncludeAdult { get; set; }

    public int? YearLower { get; set; }
    public int? YearUpper { get; set; }

    // 0-100 score range.
    public int? RatingLower { get; set; }
    public int? RatingUpper { get; set; }

    public string? SortBy { get; set; }

    // The number of eligible cards the grid wants (X). Clamped to 1-100 by the validator
    // (T-42-04-DOS — the X-clamp half of the DoS guard is the controller's job).
    public int X { get; set; }

    // Maps the validated request to the Core DiscoveryFilter the eligibility loop consumes.
    public DiscoveryFilter ToFilter() => new()
    {
        Type = Type ?? [],
        TypeNot = TypeNot ?? [],
        Genre = Genre ?? [],
        GenreNot = GenreNot ?? [],
        Tag = Tag ?? [],
        TagNot = TagNot ?? [],
        TagMode = TagMode,
        Status = Status ?? [],
        StatusNot = StatusNot ?? [],
        ContentRating = ContentRating ?? [],
        IncludeAdult = IncludeAdult,
        YearLower = YearLower,
        YearUpper = YearUpper,
        RatingLower = RatingLower,
        RatingUpper = RatingUpper,
        SortBy = SortBy
    };
}
