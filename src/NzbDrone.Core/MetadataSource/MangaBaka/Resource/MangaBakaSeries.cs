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
    /// D-08-R cross-source ids: the <see cref="Source"/> block exposes SEVEN cross-source
    /// ids onto <see cref="NzbDrone.Core.Manga.Manga"/>. Five carry RAW INTEGER ids
    /// (<c>anilist.id</c> / <c>my_anime_list.id</c> / <c>kitsu.id</c> /
    /// <c>anime_news_network.id</c> / <c>shikimori.id</c>, modelled as <c>int?</c> on
    /// <see cref="MangaBakaSourceRef"/>); two carry STRING ids (<c>anime_planet.id</c> a
    /// slug like <c>"solo-leveling"</c>, <c>manga_updates.id</c> a base36 token like
    /// <c>"6z1uqw7"</c>, modelled as <c>string</c> on <see cref="MangaBakaSourceStringRef"/>
    /// — NEVER coerced to int). Only the <c>links_v2[]</c> reading-platform links and the
    /// per-source <c>rating</c> / <c>rating_normalized</c> values remain intentionally NOT
    /// modelled here (quick-260608-l2e extended the original AniList/MAL-only mapping).
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

        // Record lifecycle state — "active" / "merged" / "deleted". MangaBaka periodically merges
        // duplicate records into a canonical one (or soft-deletes), leaving the old id resolving to
        // a non-"active" stub. D-12: Discovery (42-02) SKIPS non-"active" records during the
        // eligibility loop. Modelled here so that skip is not a silent no-op — without this field
        // the state never deserializes and every record looks "active". The round-trip is pinned by
        // MangaBakaDeserializationFixture so a future field drop fails the build (T-42-01-DTO).
        [JsonProperty("state")]
        public string State { get; set; }

        // 0–100 popularity/quality score (card display + sort). PITFALL 2 analog: the live wire
        // value is a FRACTIONAL number (e.g. 86.2083333333333 in series_by_id_3397.json), NOT an
        // integer — typing this `int?` makes Newtonsoft throw a JsonReaderException that fails the
        // ENTIRE response (search 500s, by-id silently empties). Modelled `decimal?` to round-trip
        // the fraction faithfully; consumers truncate/round at the display boundary (card score).
        // null when MangaBaka ships no score.
        [JsonProperty("rating")]
        public decimal? Rating { get; set; }

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

        // Rich tag objects (the v2 tag taxonomy — NOT the flat "tags" string array). The
        // Discovery card surfaces tag NAMES (ordered by weight) in a click-to-open popover.
        [JsonProperty("tags_v2")]
        public List<MangaBakaSeriesTag> TagsV2 { get; set; }

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
    /// One entry of the <c>tags_v2[]</c> taxonomy. Only <see cref="Name"/> + <see cref="Weight"/>
    /// are consumed — the card popover lists the names, weight-ordered (defining &gt; core &gt;
    /// incidental) so the most relevant tags surface first.
    /// </summary>
    public class MangaBakaSeriesTag
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        // "defining" | "core" | "incidental" (others possible) — relevance hint.
        [JsonProperty("weight")]
        public string Weight { get; set; }
    }

    /// <summary>
    /// One entry of the <c>titles[]</c> array — a localized title string with its BCP-47
    /// <see cref="Language"/> tag (e.g. <c>"en"</c>, <c>"ko"</c>, <c>"ko-Latn"</c> romanization,
    /// <c>"ja"</c>) and an <see cref="IsPrimary"/> flag marking the preferred title for that
    /// language. This is the richest title surface MangaBaka ships: unlike the top-level
    /// <c>title</c> (often a romanization for non-Latin works — e.g. <c>"Ichyeojin Deulpan"</c>),
    /// the <c>en</c>+primary entry carries the recognizable English title users search for
    /// (<c>"The Forgotten Field"</c>). <c>MangaBakaMetadataSource.SelectPreferredTitle</c> prefers it.
    ///
    /// PITFALL (this bit us): the API key is <c>is_primary</c>, NOT <c>primary</c>, and each
    /// entry DOES carry a <c>title</c> string — the earlier model omitted the title and used the
    /// wrong key, leaving the entire <c>titles[]</c> array dead and forcing the romanized
    /// canonical title into the UI.
    /// </summary>
    public class MangaBakaTitleEntry
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("traits")]
        public List<string> Traits { get; set; }

        [JsonProperty("is_primary")]
        public bool IsPrimary { get; set; }
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
    /// The direct cross-source block (D-08-R). <see cref="AniList"/>, <see cref="MyAnimeList"/>,
    /// <see cref="Kitsu"/>, <see cref="AnimeNewsNetwork"/> and <see cref="Shikimori"/> carry RAW
    /// INTEGER ids (modelled as <c>int?</c> on <see cref="MangaBakaSourceRef"/>);
    /// <see cref="AnimePlanet"/> and <see cref="MangaUpdates"/> carry STRING ids (modelled as
    /// <c>string</c> on <see cref="MangaBakaSourceStringRef"/> — a slug / base36 token, NEVER an
    /// int). Per-source <c>rating</c> values are intentionally not modelled.
    /// </summary>
    public class MangaBakaSource
    {
        [JsonProperty("anilist")]
        public MangaBakaSourceRef AniList { get; set; }

        [JsonProperty("my_anime_list")]
        public MangaBakaSourceRef MyAnimeList { get; set; }

        [JsonProperty("kitsu")]
        public MangaBakaSourceRef Kitsu { get; set; }

        [JsonProperty("anime_news_network")]
        public MangaBakaSourceRef AnimeNewsNetwork { get; set; }

        [JsonProperty("shikimori")]
        public MangaBakaSourceRef Shikimori { get; set; }

        [JsonProperty("anime_planet")]
        public MangaBakaSourceStringRef AnimePlanet { get; set; }

        [JsonProperty("manga_updates")]
        public MangaBakaSourceStringRef MangaUpdates { get; set; }
    }

    /// <summary>
    /// A cross-source reference whose <c>id</c> is a RAW INTEGER (D-08-R) — typed
    /// <c>int?</c> so a missing/null id deserializes cleanly. Used for
    /// anilist / my_anime_list / kitsu / anime_news_network / shikimori.
    /// </summary>
    public class MangaBakaSourceRef
    {
        [JsonProperty("id")]
        public int? Id { get; set; }
    }

    /// <summary>
    /// A cross-source reference whose <c>id</c> is a STRING (D-08-R) — anime_planet ships a
    /// slug (e.g. <c>"solo-leveling"</c>) and manga_updates a base36 token (e.g. <c>"6z1uqw7"</c>).
    /// Typed <c>string</c> (NOT <c>int?</c>) because Newtonsoft would throw a JsonReaderException
    /// trying to coerce these to an integer, failing the entire response.
    /// </summary>
    public class MangaBakaSourceStringRef
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
