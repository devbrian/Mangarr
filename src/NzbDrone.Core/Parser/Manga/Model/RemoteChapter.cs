using System.Collections.Generic;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Parser.Manga.Model
{
    // Manga peer of RemoteEpisode. Wave 0 stub — fields land in Plan 02-04.
    public class RemoteChapter
    {
        public ParsedChapterInfo ParsedChapterInfo { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public List<Chapter> Chapters { get; set; }
    }
}
