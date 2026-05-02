using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Wave 0 stub — full surface lands in Plan 02-03.
    public interface IChapterService
    {
        Chapter FindByMangaAndNumber(int mangaId, decimal chapterNumber, string translatedLanguage);
        List<Chapter> GetByMangaId(int mangaId);
        Chapter InsertChapter(Chapter chapter);
        Chapter UpdateChapter(Chapter chapter);
        void DeleteChapter(int chapterId);
    }
}
