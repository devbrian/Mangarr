using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.MangaDex.Resource
{
    /// <summary>
    /// Envelope for GET /manga/{id} — single-item response wrapping
    /// a <see cref="MangaDataItem"/> in <c>data</c>. Per MangaDex API docs.
    /// </summary>
    public class MangaResource
    {
        public MangaDataItem Data { get; set; }
    }

    /// <summary>
    /// MangaDex manga record. <see cref="Id"/> is a UUID (string form). <see cref="Type"/>
    /// is always <c>"manga"</c>. <see cref="Attributes"/> carries the editorial fields;
    /// <see cref="Relationships"/> links author / cover_art / scanlation_group records
    /// via <c>includes[]=...</c> on the request.
    /// </summary>
    public class MangaDataItem
    {
        public string Id { get; set; }                          // GUID-string
        public string Type { get; set; }                        // "manga"
        public MangaAttributes Attributes { get; set; }
        public List<RelationshipItem> Relationships { get; set; }
    }

    /// <summary>
    /// Per MangaDex API <c>attributes</c> block. Multi-language string fields are
    /// modeled as <c>Dictionary&lt;string,string&gt;</c> (BCP-47 language code → text).
    ///
    /// PITFALL 7: <see cref="Links"/> values arrive as JSON STRINGS (not integers).
    /// MangaDex stores cross-source IDs (al = AniList, mal = MyAnimeList) as strings;
    /// callers MUST run <c>int.TryParse</c> before persisting to <c>Manga.AniListId</c>
    /// or <c>Manga.MalId</c> (D-19).
    ///
    /// PITFALL 7b (debug session add-manga-lookup-null-path, 2026-05-12): MangaDex also
    /// ships <see cref="LastVolume"/> and <see cref="LastChapter"/> as JSON STRING
    /// values — including non-integer chapter numbers like <c>"1.2"</c> for half/extra
    /// chapters. Newtonsoft cannot coerce <c>"1.2"</c> to <c>int?</c>, which crashes
    /// the entire <c>/api/v5/manga/lookup</c> response on any page that contains a
    /// half-chapter entry. Both fields are typed as <c>string</c> here; consumers run
    /// <c>decimal.TryParse(InvariantCulture)</c> + <c>(int)Math.Truncate</c> at the
    /// mapping boundary (mirrors <c>ChapterFeedResource</c>'s string-typed Chapter
    /// field convention).
    /// </summary>
    public class MangaAttributes
    {
        public Dictionary<string, string> Title { get; set; }
        public List<Dictionary<string, string>> AltTitles { get; set; }
        public Dictionary<string, string> Description { get; set; }

        // PER PITFALL 7: links values are STRINGS (not ints). Resolver does
        // int.TryParse(links["al"], out _) before persisting to Manga.AniListId.
        public Dictionary<string, string> Links { get; set; }

        public string OriginalLanguage { get; set; }

        // PITFALL 7b: STRING per MangaDex API (was int? — crashed on "1.2"
        // half-chapters in lookup responses). Parse via decimal.TryParse +
        // Math.Truncate at consumption.
        public string LastVolume { get; set; }
        public string LastChapter { get; set; }                  // total-chapter-count axis (D-21); STRING per Pitfall 7b

        public string PublicationDemographic { get; set; }
        public string Status { get; set; }                       // ongoing | completed | hiatus | cancelled
        public int? Year { get; set; }                           // publication-year axis (D-21)
        public string ContentRating { get; set; }                // safe | suggestive | erotica | pornographic
        public List<TagItem> Tags { get; set; }
    }

    public class TagItem
    {
        public string Id { get; set; }
        public TagAttributes Attributes { get; set; }
    }

    public class TagAttributes
    {
        public Dictionary<string, string> Name { get; set; }
    }

    /// <summary>
    /// Relationship row from <c>includes[]=author</c> / <c>cover_art</c> /
    /// <c>scanlation_group</c>. Without the <c>includes[]</c> query param, the
    /// inner <see cref="Attributes"/> is null.
    /// </summary>
    public class RelationshipItem
    {
        public string Id { get; set; }
        public string Type { get; set; }                        // "author", "cover_art", "scanlation_group"
        public RelationshipAttributes Attributes { get; set; }
    }

    public class RelationshipAttributes
    {
        public string Name { get; set; }                        // for author / scanlation_group
        public string FileName { get; set; }                    // for cover_art

        // For relationships of type="manga" — MangaDex returns title as a multilingual
        // dictionary on the relationship's attributes when the request includes
        // includes[]=manga (e.g. /chapter, /manga/{id}/feed). Without this field the
        // chapter-feed parser falls back to "Unknown" and downstream Decision Engine
        // rejects every release with UnknownSeries: Unknown Manga.
        public Dictionary<string, string> Title { get; set; }
    }
}
