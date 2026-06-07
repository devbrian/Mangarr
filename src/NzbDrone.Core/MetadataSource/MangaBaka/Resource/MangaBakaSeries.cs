using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.MangaBaka.Resource
{
    /// <summary>
    /// MangaBaka series record (the rich record shared by the search-array and by-id
    /// envelopes). <see cref="Id"/> is a raw integer (vs MangaDex's UUID-string).
    /// Snake_case API keys carry explicit <c>[JsonProperty(...)]</c> attributes because
    /// the project-wide Newtonsoft contract resolver is camelCase.
    ///
    /// PITFALL 2 (mirror of MangaDex Pitfall 7b on <c>LastVolume</c>/<c>LastChapter</c>):
    /// <see cref="TotalChapters"/> and <see cref="FinalVolume"/> are typed <c>string</c>,
    /// NEVER <c>int?</c>. MangaBaka ships these as JSON STRING values that may carry
    /// non-integer content (e.g. half/extra chapters like <c>"1.2"</c>, or empty strings).
    /// Newtonsoft cannot coerce <c>"1.2"</c> to <c>int?</c> and would crash the entire
    /// response. Consumers (Plan 41-03) parse defensively via
    /// <c>decimal.TryParse(InvariantCulture)</c> + <c>Math.Truncate</c> at the mapping
    /// boundary — never coerced by Newtonsoft.
    ///
    /// D-08-R cross-source ids: the <see cref="Source"/> block exposes <c>anilist.id</c>
    /// and <c>my_anime_list.id</c> as RAW INTEGERS (modelled as <c>int?</c> on
    /// <see cref="MangaBakaSourceRef"/>). The <c>anime_planet.id</c> / <c>manga_updates.id</c>
    /// ids are STRINGS and the <c>links_v2[]</c> entries are reading-platform links (NOT
    /// cross-source ids) — both intentionally NOT modelled here.
    /// </summary>
    public class MangaBakaSeries
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("native_title")]
        public string NativeTitle { get; set; }

        [JsonProperty("romanized_title")]
        public string RomanizedTitle { get; set; }

        // PITFALL 3 (verified live against GET /v1/series/3397, Plan 41 close-out): the API
        // ships `secondary_titles` as a language-keyed dictionary whose VALUES are ARRAYS of
        // localized-title objects — `{ "unknown": [ { type, title, note }, ... ] }` — NOT a
        // flat string map. A `Dictionary<string,string>` typing throws a Newtonsoft
        // JsonReaderException ("Unexpected character '['") that fails the ENTIRE response
        // (search 500s, by-id silently empties).
        [JsonProperty("secondary_titles")]
        public Dictionary<string, List<MangaBakaSecondaryTitle>> SecondaryTitles { get; set; }

        [JsonProperty("titles")]
        public List<MangaBakaTitleEntry> Titles { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("year")]
        public int? Year { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("content_rating")]
        public string ContentRating { get; set; }

        [JsonProperty("authors")]
        public List<string> Authors { get; set; }

        [JsonProperty("artists")]
        public List<string> Artists { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        // PITFALL 2: STRING per MangaBaka API (never int?) — parse via decimal.TryParse +
        // Math.Truncate at consumption (Plan 41-03), never coerced by Newtonsoft.
        [JsonProperty("total_chapters")]
        public string TotalChapters { get; set; }

        // PITFALL 2: STRING per MangaBaka API (never int?).
        [JsonProperty("final_volume")]
        public string FinalVolume { get; set; }

        [JsonProperty("cover")]
        public MangaBakaCover Cover { get; set; }

        [JsonProperty("source")]
        public MangaBakaSource Source { get; set; }
    }

    /// <summary>
    /// One entry of the <c>titles[]</c> array — a localized title plus a primary flag.
    /// </summary>
    public class MangaBakaTitleEntry
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("primary")]
        public bool Primary { get; set; }
    }

    /// <summary>
    /// One entry of a <c>secondary_titles[lang]</c> array — a localized alternate title.
    /// The API nests these under language keys (e.g. <c>"unknown"</c>, <c>"en"</c>); only
    /// <see cref="Title"/> is harvested for alt-title matching.
    /// </summary>
    public class MangaBakaSecondaryTitle
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("note")]
        public string Note { get; set; }
    }

    /// <summary>
    /// Pre-resolved cover URLs at MangaBaka's published sizes. Each variant is a
    /// <see cref="MangaBakaCoverImage"/> wrapping a single <c>url</c>.
    /// </summary>
    public class MangaBakaCover
    {
        [JsonProperty("raw")]
        public MangaBakaCoverImage Raw { get; set; }

        [JsonProperty("x150")]
        public MangaBakaCoverImage X150 { get; set; }

        [JsonProperty("x250")]
        public MangaBakaCoverImage X250 { get; set; }

        [JsonProperty("x350")]
        public MangaBakaCoverImage X350 { get; set; }
    }

    /// <summary>
    /// A single cover-image variant — only the resolved <c>url</c> is consumed.
    /// </summary>
    public class MangaBakaCoverImage
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// The direct cross-source block. Per D-08-R only <see cref="AniList"/> and
    /// <see cref="MyAnimeList"/> carry RAW INTEGER ids (modelled as <c>int?</c>);
    /// anime_planet/manga_updates ship STRING ids and are intentionally not modelled.
    /// </summary>
    public class MangaBakaSource
    {
        [JsonProperty("anilist")]
        public MangaBakaSourceRef AniList { get; set; }

        [JsonProperty("my_anime_list")]
        public MangaBakaSourceRef MyAnimeList { get; set; }
    }

    /// <summary>
    /// A cross-source reference whose <c>id</c> is a RAW INTEGER (D-08-R) — typed
    /// <c>int?</c> so a missing/null id deserializes cleanly.
    /// </summary>
    public class MangaBakaSourceRef
    {
        [JsonProperty("id")]
        public int? Id { get; set; }
    }
}
