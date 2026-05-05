using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    public class RenameChapterFilePreview
    {
        public int MangaId { get; set; }
        public List<int> ChapterIds { get; set; }
        public List<decimal> ChapterNumbers { get; set; }
        public int ChapterFileId { get; set; }
        public string ExistingPath { get; set; }
        public string NewPath { get; set; }
    }
}
