using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MangaStats
{
    public class MangaStatistics : ResultSet
    {
        public int MangaId { get; set; }
        public DateTime? NextChapterDate { get; set; }
        public DateTime? PreviousChapterDate { get; set; }
        public DateTime? LastChapterDate { get; set; }
        public int ChapterFileCount { get; set; }
        public int ChapterCount { get; set; }
        public int TotalChapterCount { get; set; }
        public int MonitoredChapterCount { get; set; }
        public long SizeOnDisk { get; set; }
        public List<string> ScanlationGroups { get; set; }
        public List<Language> TranslatedLanguages { get; set; }
        public List<ReleaseType> ReleaseTypes { get; set; }
    }
}
