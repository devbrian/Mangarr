using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    public class ChapterFileMoveResult
    {
        public ChapterFileMoveResult()
        {
            OldFiles = new List<DeletedChapterFile>();
        }

        public ChapterFile ChapterFile { get; set; }
        public List<DeletedChapterFile> OldFiles { get; set; }
    }
}
