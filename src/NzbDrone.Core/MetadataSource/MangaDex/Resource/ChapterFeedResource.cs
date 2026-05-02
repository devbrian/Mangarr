using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.MangaDex.Resource
{
    /// <summary>
    /// Envelope for GET /manga/{id}/feed (paginated chapter feed). Per MangaDex docs,
    /// the endpoint paginates at <c>limit=500</c> max; <see cref="MangaDexApi.GetFeed"/>
    /// loops <c>offset</c> until a page returns &lt; pageSize entries.
    /// </summary>
    public class ChapterFeedResource
    {
        public List<ChapterFeedEntry> Data { get; set; }
    }

    /// <summary>
    /// Single chapter row from the feed. Note <see cref="ChapterFeedAttributes.Chapter"/>
    /// is a STRING (decimal as text) per MangaDex API contract — caller must run
    /// <c>decimal.TryParse</c> with InvariantCulture before persisting.
    /// </summary>
    public class ChapterFeedEntry
    {
        public string Id { get; set; }
        public string Type { get; set; }                        // "chapter"
        public ChapterFeedAttributes Attributes { get; set; }
        public List<RelationshipItem> Relationships { get; set; }
    }

    public class ChapterFeedAttributes
    {
        public string Volume { get; set; }
        public string Chapter { get; set; }                     // string per MangaDex API; parse to decimal
        public string Title { get; set; }
        public string TranslatedLanguage { get; set; }          // BCP-47
        public DateTime? PublishAt { get; set; }
    }
}
