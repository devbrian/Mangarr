using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Parser.Model
{
    public class ReleaseInfo
    {
        public ReleaseInfo()
        {
            Languages = new List<Language>();
        }

        public string Guid { get; set; }
        public string Title { get; set; }
        public long Size { get; set; }
        public string DownloadUrl { get; set; }
        public string InfoUrl { get; set; }
        public string CommentUrl { get; set; }
        public int IndexerId { get; set; }
        public string Indexer { get; set; }
        public int IndexerPriority { get; set; }
        public int SeasonSearchMaximumSingleEpisodeAge { get; set; }
        public DownloadProtocol DownloadProtocol { get; set; }
        public int TvdbId { get; set; }
        public int TvRageId { get; set; }
        public string ImdbId { get; set; }
        public DateTime PublishDate { get; set; }

        public string Origin { get; set; }
        public string Source { get; set; }
        public string Container { get; set; }
        public string Codec { get; set; }
        public string Resolution { get; set; }

        // ── Phase 3 manga-shaped fields (Q-4 / SOURCE-04) ─────────────────────────────────
        // Indexer-supplied wins (Phase 2 D-10 contract); parser fallback in MangaParsingService.Map.
        // Populated by Phase 3 manga indexer parsers (MangaDex, comix.to). Phase 5 Custom Formats
        // consume both for group-match + language-match rules. Phase 8 may rename / collapse.

        /// <summary>
        /// Scanlation group attribution (e.g. "Mangastream"). MangaDex returns from
        /// <c>relationships[scanlation_group].attributes.name</c>; comix.to returns null
        /// (single-source). Required for SOURCE-07 (MangaDex ToS scanlation-group attribution).
        /// </summary>
        public string ScanlationGroup { get; set; }

        /// <summary>
        /// BCP-47 translated-language code (<c>"en"</c>, <c>"es"</c>, <c>"ja"</c>, <c>"es-la"</c>).
        /// Distinct from <see cref="Languages"/> because Sonarr's <c>Language</c> enum is
        /// TV-region-flavored. Phase 5 <c>TranslationProfile</c> ordinal gate consumes this field.
        /// </summary>
        public string TranslatedLanguage { get; set; }

        /// <summary>
        /// Phase 6 PIPELINE-04 — manga sibling of <c>GrabbedReleaseInfo.EpisodeIds</c>. Stamped
        /// at search-time after MangaParsingService.Map resolves a release to one or more
        /// Chapter rows; consumed by Plan 06-07 <c>MatchesGrabSpecification</c> to reject
        /// downloads whose actual <c>LocalChapter.Chapters</c> do not match the chapters this
        /// release was grabbed for (downloaded the wrong thing). Initialized to empty list.
        /// Phase 8 cleanup: collapse with <c>EpisodeIds</c> when <c>Tv/</c> deletes.
        /// Sonarr divergence: NEW manga-side field — see DIVERGENCE.md.
        /// </summary>
        public List<int> ChapterIds { get; set; } = new List<int>();

        public List<Language> Languages { get; set; }

        [JsonIgnore]
        public IndexerFlags IndexerFlags { get; set; }

        // Used to track pending releases that are being reprocessed
        [JsonIgnore]
        public PendingReleaseReason? PendingReleaseReason { get; set; }

        public int Age
        {
            get
            {
                return DateTime.UtcNow.Subtract(PublishDate).Days;
            }

            private set
            {
            }
        }

        public double AgeHours
        {
            get
            {
                return DateTime.UtcNow.Subtract(PublishDate).TotalHours;
            }

            private set
            {
            }
        }

        public double AgeMinutes
        {
            get
            {
                return DateTime.UtcNow.Subtract(PublishDate).TotalMinutes;
            }

            private set
            {
            }
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1} [{2}]", PublishDate, Title, Size);
        }

        public virtual string ToString(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "L": // Long format
                    var stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine("Guid: " + Guid ?? "Empty");
                    stringBuilder.AppendLine("Title: " + Title ?? "Empty");
                    stringBuilder.AppendLine("Size: " + Size ?? "Empty");
                    stringBuilder.AppendLine("InfoUrl: " + InfoUrl ?? "Empty");
                    stringBuilder.AppendLine("DownloadUrl: " + DownloadUrl ?? "Empty");
                    stringBuilder.AppendLine("Indexer: " + Indexer ?? "Empty");
                    stringBuilder.AppendLine("CommentUrl: " + CommentUrl ?? "Empty");
                    stringBuilder.AppendLine("DownloadProtocol: " + DownloadProtocol ?? "Empty");
                    stringBuilder.AppendLine("TvdbId: " + TvdbId ?? "Empty");
                    stringBuilder.AppendLine("TvRageId: " + TvRageId ?? "Empty");
                    stringBuilder.AppendLine("ImdbId: " + ImdbId ?? "Empty");
                    stringBuilder.AppendLine("PublishDate: " + PublishDate ?? "Empty");
                    return stringBuilder.ToString();
                default:
                    return ToString();
            }
        }
    }
}
