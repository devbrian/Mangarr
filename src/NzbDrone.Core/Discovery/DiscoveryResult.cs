using System.Collections.Generic;

namespace NzbDrone.Core.Discovery
{
    /// <summary>
    /// Envelope returned by <see cref="IDiscoveryService.Search"/> (Plan 42-02). Carries the
    /// post-filtered eligible cards plus the pool-exhaustion signal the UI uses to tell the user
    /// "we ran out of matching titles before reaching the requested count" (D-06).
    ///
    /// <para>
    /// <see cref="PoolExhausted"/> is true when the eligibility auto-paging loop stopped because
    /// the MangaBaka result pool ran dry — an empty page, <c>page*limit &gt;= total</c>, or the
    /// <c>MAX_PAGE=100</c> ceiling (T-42-02-DOS termination guard) — BEFORE collecting
    /// <see cref="Requested"/> eligible rows. It is false when the loop stopped because it
    /// collected enough (there may be more in the pool).
    /// </para>
    /// </summary>
    public class DiscoveryResult
    {
        public List<DiscoveryResultItem> Results { get; set; } = new List<DiscoveryResultItem>();

        // True when the pool was exhausted (empty page / page*limit>=total / MAX_PAGE ceiling)
        // before Requested eligible rows were collected.
        public bool PoolExhausted { get; set; }

        // The X the caller asked for (echoed so the UI can render "found N of X").
        public int Requested { get; set; }

        // Count of eligible rows collected (may exceed Requested before the Take(x) trim, but is
        // reported here as the true eligible count the loop accumulated).
        public int Found { get; set; }

        // Total rows on MangaBaka matching the filter (the first page's pagination total) — the
        // "764 total match" figure in the toolbar summary (sketch 001).
        public int TotalMatch { get; set; }

        // Rows skipped while paging because they are already in the library — the
        // "hiding N in library" figure.
        public int HiddenInLibrary { get; set; }

        // Rows skipped while paging because they are on the global ImportListExclusion list —
        // the "M excluded" figure.
        public int HiddenExcluded { get; set; }
    }

    /// <summary>
    /// A single Discovery grid card — the slim projection of a MangaBaka series record the browse
    /// UI needs. NOT a full <see cref="NzbDrone.Core.Manga.Manga"/>; the bulk-add path (42-03)
    /// re-resolves the full record from <see cref="MangaBakaId"/> at add time.
    /// </summary>
    public class DiscoveryResultItem
    {
        public int MangaBakaId { get; set; }
        public string Title { get; set; }
        public string CoverUrl { get; set; }
        public int? Year { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public string ContentRating { get; set; }
        public List<string> Genres { get; set; } = new List<string>();

        // Synopsis shown in the title click-to-open popover.
        public string Description { get; set; }

        // Tag names (weight-ordered, capped) shown in the cover tags click-to-open popover.
        public List<string> Tags { get; set; } = new List<string>();

        // 0–100 popularity/quality score (decimal? — the MangaBaka wire value is fractional;
        // the card UI truncates/rounds at the display boundary per Plan 42-01's decimal? typing).
        public decimal? Score { get; set; }
    }
}
