using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    public interface IMangaFileTableCleanupService
    {
        void Clean(Manga manga, List<string> filesOnDisk);
    }
}
