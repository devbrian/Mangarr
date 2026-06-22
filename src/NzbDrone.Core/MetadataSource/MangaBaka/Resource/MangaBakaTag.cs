using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.MangaBaka.Resource
{
    /// <summary>
    /// A SLIM tag option from GET <c>/v1/tags</c> (DISC-03 / D-10). The browse filter binds to the
    /// integer <see cref="Id"/> (NOT the name); <see cref="NamePath"/> disambiguates same-named
    /// tags (spike 004), <see cref="SeriesCount"/> drives option ordering, and
    /// <see cref="ContentRating"/> lets the UI gate adult tags.
    ///
    /// <para>
    /// DELIBERATELY slim (DISC-03): the live <c>/v1/tags</c> item also carries several extra keys
    /// (the long blurb, <c>is_spoiler</c>, <c>parent_id</c>, <c>merged_with</c>, <c>is_genre</c>,
    /// <c>level</c>) — NONE are modelled. Newtonsoft silently ignores the extra keys (A1), so
    /// dropping them is safe. The long blurb in particular is intentionally NOT modelled (it bloats
    /// the option payload and is unused by the filter UI).
    /// </para>
    /// </summary>
    public class MangaBakaTag
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("name_path")]
        public string NamePath { get; set; }

        [JsonProperty("series_count")]
        public int SeriesCount { get; set; }

        [JsonProperty("content_rating")]
        public string ContentRating { get; set; }
    }

    /// <summary>
    /// Envelope for GET <c>/v1/tags</c> — MangaBaka wraps the option list in <c>{ data[] }</c>.
    /// </summary>
    public class MangaBakaTagListResource
    {
        [JsonProperty("data")]
        public List<MangaBakaTag> Data { get; set; }
    }
}
