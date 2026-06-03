using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>Release</c> schema.
    /// Required: guid, title, sourceKey, downloadHandle, publishDate. The rest are advisory
    /// hints (<c>nullable: true</c>).
    ///
    /// <para>
    /// Pitfall 3: <see cref="ChapterNumber"/> is <c>decimal?</c> (never <c>double</c>) and
    /// <see cref="SizeBytes"/> is <c>long?</c> so a malformed/huge number has no exception path.
    /// </para>
    /// </summary>
    public class GatewayRelease
    {
        // Required fields (OpenAPI required: [guid, title, sourceKey, downloadHandle, publishDate]).
        public string Guid { get; set; }
        public string Title { get; set; }
        public string SourceKey { get; set; }

        // Opaque R6 token submitted back to POST /downloads — NOT a URL Mangarr fetches (Pitfall 5).
        public string DownloadHandle { get; set; }

        // WR-03: nullable on the wire. `publishDate` is "required" in the OpenAPI schema but it is
        // remote-controlled — a missing/null value must NOT silently become DateTime.MinValue
        // (0001-01-01), which would corrupt age-based decision specs / RSS watermark dedup. The
        // parser substitutes DateTime.UtcNow when this is null.
        public DateTime? PublishDate { get; set; }

        // Advisory / nullable hints.
        public string InfoUrl { get; set; }
        public string MangaTitle { get; set; }

        // Pitfall 3: decimal? — advisory decimal chapter hint.
        public decimal? ChapterNumber { get; set; }
        public int? Volume { get; set; }

        // BCP-47 → ReleaseInfo.TranslatedLanguage.
        public string Language { get; set; }
        public string ScanlationGroup { get; set; }
        public int? PageCount { get; set; }

        // Pitfall 3: long? — defaults to 0 on the wire.
        public long? SizeBytes { get; set; }

        public Dictionary<string, object> Ids { get; set; }
    }
}
