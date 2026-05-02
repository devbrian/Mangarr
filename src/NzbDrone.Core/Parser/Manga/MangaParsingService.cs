using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Parser.Manga
{
    // Wave 0 contract for the manga parsing service. Resolves a parsed release into
    // a RemoteChapter against the local DB (Manga + Chapters). Mirrors
    // IParsingService.Map per CONTEXT D-03.
    public interface IMangaParsingService
    {
        // Per D-10: indexer-supplied TranslatedLanguage wins; the parser-extracted
        // value is fallback only.
        RemoteChapter Map(ParsedChapterInfo parsedChapterInfo, NzbDrone.Core.Manga.Manga manga, IList<Chapter> chapters);
    }

    // Wave 0 stub — full implementation lands in Plan 02-04.
    public class MangaParsingService : IMangaParsingService
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        public MangaParsingService(IMangaService mangaService, IChapterService chapterService, Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _logger = logger;
        }

        public RemoteChapter Map(ParsedChapterInfo parsedChapterInfo, NzbDrone.Core.Manga.Manga manga, IList<Chapter> chapters)
        {
            throw new NotImplementedException(
                "MangaParsingService is a Wave 0 stub. Real implementation lands in Plan 02-04.");
        }
    }
}
