using System.Collections.Generic;

namespace NzbDrone.Core.Discovery
{
    /// <summary>
    /// Strongly-typed MangaBaka browse-filter model — the typed contract consumed by
    /// <see cref="NzbDrone.Core.MetadataSource.MangaBaka.MangaBakaApi"/>'s <c>Browse</c> method
    /// (Plan 42-01) and the eligibility loop in <c>DiscoveryService</c> (Plan 42-02).
    ///
    /// <para>
    /// NEW-in-Mangarr — no Sonarr analog (Sonarr has no remote-attribute browse surface). Each
    /// list maps to a REPEATED query param on the MangaBaka <c>/v1/series/search</c> endpoint
    /// (e.g. <c>Genre=["action","shounen"]</c> → <c>genre=action&amp;genre=shounen</c>); the params
    /// are NEVER comma-joined (a comma trips MangaBaka's HTTP 400 per RESEARCH Pitfall 2).
    /// </para>
    ///
    /// <para>
    /// This is a pure POCO — it carries no validation. Enum-whitelisting + range-clamping of the
    /// values is the service/controller's job (42-02 / 42-04); the Browse method only URL-encodes
    /// whatever it is handed (T-42-01-SSRF mitigation: typed values, fixed base host).
    /// </para>
    ///
    /// <para>
    /// Enum reference (see <c>.planning/notes/discovery-tab-bulk-add.md</c>):
    /// type — <c>manga,novel,manhwa,manhua,oel,other</c>;
    /// status — <c>cancelled,completed,hiatus,releasing,unknown,upcoming</c>;
    /// content_rating — <c>safe,suggestive,erotica,pornographic</c>.
    /// <see cref="Tag"/> / <see cref="TagNot"/> carry integer tag ids (D-10), NOT names.
    /// </para>
    /// </summary>
    public class DiscoveryFilter
    {
        public List<string> Type { get; set; } = new List<string>();
        public List<string> TypeNot { get; set; } = new List<string>();
        public List<string> Genre { get; set; } = new List<string>();
        public List<string> GenreNot { get; set; } = new List<string>();

        // Integer tag ids (D-10) — tag binds to the MangaBaka tag `id`, never its name.
        public List<int> Tag { get; set; } = new List<int>();
        public List<int> TagNot { get; set; } = new List<int>();

        // "and" (AND across selected tags) or "or" (ANY). Default "and".
        public string TagMode { get; set; } = "and";

        public List<string> Status { get; set; } = new List<string>();
        public List<string> StatusNot { get; set; } = new List<string>();
        public List<string> ContentRating { get; set; } = new List<string>();

        // When false, the SERVICE (42-02) injects content_rating=safe&suggestive — NOT this model
        // and NOT MangaBakaApi.Browse.
        public bool IncludeAdult { get; set; }

        public int? YearLower { get; set; }
        public int? YearUpper { get; set; }

        // 0–100 score range (card-display/sort score on the MangaBaka record).
        public int? RatingLower { get; set; }
        public int? RatingUpper { get; set; }

        public string SortBy { get; set; }
    }
}
