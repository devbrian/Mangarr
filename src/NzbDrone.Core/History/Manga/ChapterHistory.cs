using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/EpisodeHistory.cs.
    //
    // BL-01 fix: ChapterId column is INDEPENDENT of EpisodeHistory.EpisodeId (separate table).
    // The Phase 5 spec STUB called _historyService.FindByEpisodeId(chapter.Id) and got TV rows
    // back when Episode.Id and Chapter.Id collided across the independent autoincrement
    // sequences. The new substrate replaces that with a manga-aware FindByChapterId that
    // queries this table — see ChapterHistoryService.FindByChapterId for the wiring site.
    //
    // Manga sibling diverges from EpisodeHistory:
    //   * No Quality (manga has no quality model per Phase 5 D-04).
    //   * No Languages (single TranslatedLanguage : string per BCP-47 Phase 3 D-Q4).
    //   * Adds ScanlationGroup, SourceKey, ReleaseGuid for D-11 release identity triple.
    //
    // Phase 8 cleanup: collapse with EpisodeHistory when Tv/ deletes.
    public class ChapterHistory : ModelBase
    {
        public const string DOWNLOAD_CLIENT = "downloadClient";
        public const string INDEXER = "indexer";
        public const string SIZE = "size";
        public const string AGE = "age";
        public const string PUBLISHED_DATE = "publishedDate";
        public const string CUSTOM_FORMAT_SCORE = "customFormatScore";
        public const string PROTOCOL = "protocol";
        public const string MESSAGE = "message";
        public const string SOURCE = "source";
        public const string CHAPTER_FILE_ID = "chapterFileId";
        public const string DROPPED_PATH = "droppedPath";
        public const string IMPORTED_PATH = "importedPath";
        public const string FAILURE_REASON = "failureReason";
        public const string REJECTION_TYPE = "rejectionType";
        public const string RELEASE_GROUP = "releaseGroup";

        public ChapterHistory()
        {
            Data = new Dictionary<string, string>();
        }

        public int MangaId { get; set; }
        public int ChapterId { get; set; }
        public string SourceTitle { get; set; }
        public DateTime Date { get; set; }
        public ChapterHistoryEventType EventType { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public string DownloadId { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }
        public string SourceKey { get; set; }
        public string ReleaseGuid { get; set; }
        public bool Successful { get; set; }
    }
}
