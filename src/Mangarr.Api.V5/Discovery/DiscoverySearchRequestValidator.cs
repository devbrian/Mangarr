using FluentValidation;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 Plan 42-04 — FluentValidation for the Discovery search request (T-42-04-TAMPER).
//
// Clamps + whitelists the untrusted filter BEFORE DiscoverySearchRequestResource.ToFilter()
// forwards it to DiscoveryService.Search → MangaBakaApi.Browse. 42-02 explicitly handed the
// X-clamp + enum-whitelist + range-bound responsibility to the controller (the service does
// NOT sanitize). A violation returns 400 BadRequest from the controller.
//
// Whitelists (canonical source: .planning/notes/discovery-tab-bulk-add.md §Enums):
//   type           — manga, novel, manhwa, manhua, oel, other
//   status         — cancelled, completed, hiatus, releasing, unknown, upcoming
//   content_rating — safe, suggestive, erotica, pornographic
//   sort_by        — direction-baked sort tokens (default relevance_desc)
//
// Year is bounded to 1679-2262 and score to 0-100 (RESEARCH §Security Domain V5 Input
// Validation row) so out-of-range values cannot reach the upstream browse query.
public class DiscoverySearchRequestValidator : AbstractValidator<DiscoverySearchRequestResource>
{
    private static readonly HashSet<string> TypeWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "manga", "novel", "manhwa", "manhua", "oel", "other"
    };

    private static readonly HashSet<string> StatusWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "cancelled", "completed", "hiatus", "releasing", "unknown", "upcoming"
    };

    private static readonly HashSet<string> ContentRatingWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "safe", "suggestive", "erotica", "pornographic"
    };

    private static readonly HashSet<string> SortByWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "name_asc", "name_desc",
        "popularity_asc", "popularity_desc",
        "random",
        "relevance_asc", "relevance_desc",
        "score_asc", "score_desc",
        "chapters_asc", "chapters_desc",
        "volumes_asc", "volumes_desc",
        "published_year_asc", "published_year_desc",
        "published_start_date_asc", "published_start_date_desc",
        "published_end_date_asc", "published_end_date_desc",
        "latest"
    };

    private static readonly HashSet<string> TagModeWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or"
    };

    public DiscoverySearchRequestValidator()
    {
        // T-42-04-DOS: clamp X to the grid's 1-100 ceiling.
        RuleFor(r => r.X).InclusiveBetween(1, 100);

        // Enum whitelists — reject any unknown token on both the include and exclude lists.
        RuleForEach(r => r.Type).Must(v => TypeWhitelist.Contains(v))
            .WithMessage("Unknown type filter value.");
        RuleForEach(r => r.TypeNot).Must(v => TypeWhitelist.Contains(v))
            .WithMessage("Unknown type filter value.");

        RuleForEach(r => r.Status).Must(v => StatusWhitelist.Contains(v))
            .WithMessage("Unknown status filter value.");
        RuleForEach(r => r.StatusNot).Must(v => StatusWhitelist.Contains(v))
            .WithMessage("Unknown status filter value.");

        RuleForEach(r => r.ContentRating).Must(v => ContentRatingWhitelist.Contains(v))
            .WithMessage("Unknown content rating filter value.");

        RuleFor(r => r.SortBy).Must(v => SortByWhitelist.Contains(v!))
            .When(r => !string.IsNullOrWhiteSpace(r.SortBy))
            .WithMessage("Unknown sort_by value.");

        RuleFor(r => r.TagMode).Must(v => TagModeWhitelist.Contains(v))
            .When(r => !string.IsNullOrWhiteSpace(r.TagMode))
            .WithMessage("tag_mode must be 'and' or 'or'.");

        // Range bounds — year 1679-2262, score 0-100; only when the field is set.
        RuleFor(r => r.YearLower!.Value).InclusiveBetween(1679, 2262).When(r => r.YearLower.HasValue);
        RuleFor(r => r.YearUpper!.Value).InclusiveBetween(1679, 2262).When(r => r.YearUpper.HasValue);
        RuleFor(r => r.RatingLower!.Value).InclusiveBetween(0, 100).When(r => r.RatingLower.HasValue);
        RuleFor(r => r.RatingUpper!.Value).InclusiveBetween(0, 100).When(r => r.RatingUpper.HasValue);
    }
}
