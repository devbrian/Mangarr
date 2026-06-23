using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;

namespace NzbDrone.Core.Discovery
{
    /// <summary>
    /// Eligibility auto-paging loop (D-02 / D-06 / D-12) + adult-by-default injection (D-04 / D-05)
    /// + genres/tags cache (D-03). The highest unit-test surface of the Discovery vertical
    /// (boundary cases pinned by <c>DiscoveryServiceFixture</c>).
    ///
    /// <para>
    /// MangaBaka resolution: <see cref="MangaBakaApi"/> is NOT DI-registered — it is constructed
    /// lazily inside <see cref="MangaBakaMetadataSource"/> from the provider's <c>Settings</c>
    /// (<c>Definition.Settings</c>). So we reach the browse surface through the provider's public
    /// <c>Browse</c>/<c>GetGenres</c>/<c>GetTags</c> pass-throughs — but we MUST resolve the
    /// CONFIGURED instance via <see cref="IMetadataSourceFactory"/>'s <c>GetInstance</c> (which
    /// populates <c>Definition</c>), NOT the raw DI <c>IEnumerable&lt;IMetadataSource&gt;</c> template (whose
    /// <c>Definition</c> is null, so <c>Settings</c>/<c>Api</c> NRE). We resolve MangaBaka
    /// SPECIFICALLY (not the active-primary resolver) because only MangaBaka's search supports
    /// attribute filters — Discovery browses MangaBaka regardless of the active primary.
    /// </para>
    ///
    /// <para>
    /// Throttling lives ENTIRELY in the HTTP layer (<c>MangaBakaApi.Browse</c>'s <c>RateLimit</c>
    /// on the shared "mangabaka" bucket — D-02). This loop performs no in-loop sleep of any kind.
    /// </para>
    /// </summary>
    public class DiscoveryService : IDiscoveryService
    {
        // LIMIT: MangaBaka page size for the eligibility pull. MAX_PAGE: the hard ceiling that
        // guarantees termination of an otherwise-unbounded auto-page loop (X huge / a filter that
        // matches nothing) — T-42-02-DOS. The ceiling-hit fixture proves no infinite loop.
        private const int Limit = 100;
        private const int MaxPage = 100;

        // Cap on tag names surfaced per card (weight-ordered) — bounds the search payload.
        private const int MaxCardTags = 100;

        private readonly IMetadataSourceFactory _metadataSourceFactory;
        private readonly IMangaService _mangaService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        private readonly ICached<List<MangaBakaGenre>> _genreCache;
        private readonly ICached<List<MangaBakaTag>> _tagCache;

        public DiscoveryService(
            IMetadataSourceFactory metadataSourceFactory,
            IMangaService mangaService,
            IImportListExclusionService importListExclusionService,
            ICacheManager cacheManager,
            Logger logger)
        {
            _metadataSourceFactory = metadataSourceFactory;
            _mangaService = mangaService;
            _importListExclusionService = importListExclusionService;
            _logger = logger;

            _genreCache = cacheManager.GetCache<List<MangaBakaGenre>>(GetType(), "genres");
            _tagCache = cacheManager.GetCache<List<MangaBakaTag>>(GetType(), "tags");
        }

        // Resolve the CONFIGURED MangaBaka provider (Definition + Settings populated) via the
        // factory. The raw DI IEnumerable<IMetadataSource> yields the ThingiProvider TEMPLATE whose
        // Definition is null — accessing its Settings/Api NREs (the 500 the live E2E caught). We
        // resolve MangaBaka SPECIFICALLY (not the active primary) because only its search supports
        // attribute filters; a missing row is a wiring bug we surface loudly.
        private MangaBakaMetadataSource ResolveMangaBaka()
        {
            // Exactly one MangaBaka provider must be registered. FirstOrDefault would
            // silently pick one of two duplicates (masking a DI wiring bug); a 0/2 count
            // is a wiring error we surface loudly (the comment intent the loop relied on).
            var definitions = _metadataSourceFactory.All()
                .Where(d => d.Implementation == nameof(MangaBakaMetadataSource))
                .ToList();

            if (definitions.Count == 0)
            {
                throw new InvalidOperationException(
                    "MangaBaka metadata source is not configured — Discovery requires it.");
            }

            if (definitions.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one MangaBaka metadata source but found {definitions.Count} — Discovery requires a single registration.");
            }

            return (MangaBakaMetadataSource)_metadataSourceFactory.GetInstance(definitions[0]);
        }

        public DiscoveryResult Search(DiscoveryFilter filter, int x)
        {
            // One up-front HashSet<int> each for the in-library + excluded MangaBakaIds — the
            // per-row eligibility test is O(1) and the DB reads happen once, not per page.
            var inLibrary = new HashSet<int>(
                _mangaService.GetAllManga()
                    .Where(m => m.MangaBakaId.HasValue)
                    .Select(m => m.MangaBakaId.Value));

            var excluded = new HashSet<int>(
                _importListExclusionService.All()
                    .Where(e => e.MangaBakaId.HasValue)
                    .Select(e => e.MangaBakaId.Value));

            // Safe-by-default (D-04 / D-05 / T-42-02-ADULT): when adult is not requested, inject
            // content_rating=safe&suggestive. The original caller filter is never mutated.
            var browseFilter = ApplyAdultDefault(filter);

            var mangaBaka = ResolveMangaBaka();

            var eligible = new List<DiscoveryResultItem>();
            var poolExhausted = false;
            var page = 1;

            // Toolbar summary figures (sketch 001): total matching the filter (captured once from
            // the first page's pagination) + the in-library/excluded rows skipped while paging.
            var totalMatch = 0;
            var hiddenInLibrary = 0;
            var hiddenExcluded = 0;

            for (; page <= MaxPage; page++)
            {
                var resource = mangaBaka.Browse(browseFilter, page, Limit);

                if (page == 1)
                {
                    totalMatch = resource?.Pagination?.Total ?? 0;
                }

                // Empty/absent page → the pool is dry (there is no `next` field on the MangaBaka
                // envelope; an empty Data array is the definitive end-of-pool signal).
                if (resource?.Data == null || resource.Data.Count == 0)
                {
                    poolExhausted = true;
                    break;
                }

                foreach (var row in resource.Data)
                {
                    // D-12: skip merged/deleted record stubs (the old id of a record MangaBaka has
                    // since merged into a canonical one or soft-deleted).
                    if (IsMergedOrDeleted(row.State))
                    {
                        continue;
                    }

                    // Count the two hidden classes separately for the toolbar summary.
                    if (inLibrary.Contains(row.Id))
                    {
                        hiddenInLibrary++;
                        continue;
                    }

                    if (excluded.Contains(row.Id))
                    {
                        hiddenExcluded++;
                        continue;
                    }

                    eligible.Add(Map(row));
                }

                // Collected enough — stop WITHOUT marking the pool exhausted (more may remain).
                if (eligible.Count >= x)
                {
                    break;
                }

                // Computed exhaustion: page*limit has covered the reported total.
                var total = resource.Pagination?.Total ?? 0;
                if (page * Limit >= total)
                {
                    poolExhausted = true;
                    break;
                }
            }

            // Hit the MAX_PAGE ceiling without collecting X and without an exhaustion break — the
            // pool is (effectively) exhausted as far as Discovery will page. T-42-02-DOS.
            if (eligible.Count < x && page > MaxPage)
            {
                poolExhausted = true;
            }

            return new DiscoveryResult
            {
                Results = eligible.Take(x).ToList(),
                PoolExhausted = poolExhausted,
                Requested = x,
                Found = eligible.Count,
                TotalMatch = totalMatch,
                HiddenInLibrary = hiddenInLibrary,
                HiddenExcluded = hiddenExcluded
            };
        }

        public List<MangaBakaGenre> GetGenres()
        {
            // D-03: 12h TTL get-or-add. The 2nd call within the window hits cache, not the provider.
            return _genreCache.Get("genres", () => ResolveMangaBaka().GetGenres(), TimeSpan.FromHours(12));
        }

        public List<MangaBakaTag> GetTags()
        {
            return _tagCache.Get("tags", () => ResolveMangaBaka().GetTags(), TimeSpan.FromHours(12));
        }

        // Clone the caller's filter, injecting the safe-by-default content_rating when adult is not
        // requested. Never mutates the source filter (the controller may reuse it).
        private static DiscoveryFilter ApplyAdultDefault(DiscoveryFilter source)
        {
            var filter = new DiscoveryFilter
            {
                Type = source.Type,
                TypeNot = source.TypeNot,
                Genre = source.Genre,
                GenreNot = source.GenreNot,
                Tag = source.Tag,
                TagNot = source.TagNot,
                TagMode = source.TagMode,
                Status = source.Status,
                StatusNot = source.StatusNot,
                ContentRating = source.ContentRating,
                IncludeAdult = source.IncludeAdult,
                YearLower = source.YearLower,
                YearUpper = source.YearUpper,
                RatingLower = source.RatingLower,
                RatingUpper = source.RatingUpper,
                SortBy = source.SortBy
            };

            if (!source.IncludeAdult)
            {
                filter.ContentRating = new List<string> { "safe", "suggestive" };
            }

            return filter;
        }

        private static bool IsMergedOrDeleted(string state)
        {
            return string.Equals(state, "merged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "deleted", StringComparison.OrdinalIgnoreCase);
        }

        // tags_v2 weight ranking — most-relevant tags first; unknown weights sort last.
        private static int WeightRank(string weight) => weight?.ToLowerInvariant() switch
        {
            "defining" => 0,
            "core" => 1,
            "incidental" => 2,
            _ => 3
        };

        private static DiscoveryResultItem Map(MangaBakaSeries row)
        {
            return new DiscoveryResultItem
            {
                MangaBakaId = row.Id,
                Title = row.Title ?? row.RomanizedTitle ?? row.NativeTitle,
                CoverUrl = row.Cover?.Raw?.Url,
                Year = row.Year,
                Status = row.Status,
                Type = row.Type,
                ContentRating = row.ContentRating,
                Genres = row.Genres ?? new List<string>(),
                Description = row.Description,
                Tags = (row.TagsV2 ?? new List<MangaBakaSeriesTag>())
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .OrderBy(t => WeightRank(t.Weight))
                    .Select(t => t.Name)
                    .Take(MaxCardTags)
                    .ToList(),
                Score = row.Rating
            };
        }
    }
}
