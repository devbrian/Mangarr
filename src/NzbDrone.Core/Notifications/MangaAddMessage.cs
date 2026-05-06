namespace NzbDrone.Core.Notifications
{
    public class MangaAddMessage
    {
        public string Message { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }

        public override string ToString()
        {
            return Message;
        }
    }
}
