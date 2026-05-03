using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// MangaDex <c>/at-home/server/{chapterId}</c> response shape.
    /// Verified shape: <c>https://api.mangadex.org/docs/04-chapter/retrieving-chapter</c>.
    ///
    /// <para>
    /// Token TTL ~15min documented; D-03 reactive 403/410 → re-fetch handles expiry without
    /// proactive scheduling. Phase 4 plan 04-03's <c>ChapterPageFetcher</c> raises
    /// <c>ManifestExpiredException</c> on 403/410 against image URLs derived from this resource;
    /// <c>ChapterDownloadService</c> catches the exception and re-issues GetChapterPages once.
    /// </para>
    /// </summary>
    public sealed class MangaDexAtHomeResource
    {
        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonProperty("chapter")]
        public MangaDexAtHomeChapter Chapter { get; set; }
    }

    public sealed class MangaDexAtHomeChapter
    {
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("data")]
        public List<string> Data { get; set; } = new();

        [JsonProperty("dataSaver")]
        public List<string> DataSaver { get; set; } = new();
    }
}
