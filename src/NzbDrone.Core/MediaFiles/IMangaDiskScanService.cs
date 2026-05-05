using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    public interface IMangaDiskScanService
    {
        void Scan(Manga manga);
        string[] GetMangaFiles(string path, bool allDirectories = true);
        List<string> FilterPaths(string basePath, IEnumerable<string> files, bool filterExtras = true);
    }
}
