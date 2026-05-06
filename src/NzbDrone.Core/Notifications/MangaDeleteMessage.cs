namespace NzbDrone.Core.Notifications
{
    public class MangaDeleteMessage
    {
        public string Message { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public bool DeletedFiles { get; set; }
        public string DeletedFilesMessage { get; set; }

        public override string ToString()
        {
            return Message;
        }

        public MangaDeleteMessage(NzbDrone.Core.Manga.Manga manga, bool deleteFiles)
        {
            Manga = manga;
            DeletedFiles = deleteFiles;
            DeletedFilesMessage = DeletedFiles
                ? "Manga removed and all files were deleted"
                : "Manga removed, files were not deleted";
            Message = manga.Title + " - " + DeletedFilesMessage;
        }
    }
}
