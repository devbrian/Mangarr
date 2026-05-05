using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.MediaFiles.MangaImport.Manual
{
    public class ManuallyImportedFile
    {
        public TrackedDownload TrackedDownload { get; set; }
        public MangaImportResult ImportResult { get; set; }
    }
}
