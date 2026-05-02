using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// D-17 chapter-list synthesis fallback. Three strategies:
    /// 1. MangaDex linked + incoming non-empty → real-feed source-of-truth; replace
    ///    synthetic in place by ChapterNumber, insert new chapters not previously present.
    /// 2. MangaDex NOT linked + manga.TotalChapterCount > 0 → synthesize N rows
    ///    (ChapterNumber 1..N, IsSynthetic=true, TranslatedLanguage="und" sentinel
    ///    per RESEARCH §Open Question 3).
    /// 3. chapter-count null → empty list + warning log
    ///    (MissingChapterListHealthCheck fires elsewhere).
    /// </summary>
    public interface IChapterListService
    {
        void SyncChapters(Manga manga, List<Chapter> incoming);
    }
}
