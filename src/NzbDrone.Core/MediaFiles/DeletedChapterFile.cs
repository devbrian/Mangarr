namespace NzbDrone.Core.MediaFiles
{
    public class DeletedChapterFile
    {
        public string RecycleBinPath { get; set; }
        public ChapterFile ChapterFile { get; set; }

        public DeletedChapterFile(ChapterFile chapterFile, string recycleBinPath)
        {
            ChapterFile = chapterFile;
            RecycleBinPath = recycleBinPath;
        }
    }
}
