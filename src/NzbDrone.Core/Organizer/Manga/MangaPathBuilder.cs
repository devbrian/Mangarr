using System;
using System.IO;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Organizer.Manga
{
    public interface IBuildMangaPaths
    {
        string BuildPath(MangaModel manga, bool useExistingRelativeFolder);
    }

    // Sonarr divergence: NEW manga-side path builder per Phase 5 D-15 — see DIVERGENCE.md.
    // Mirrors src/NzbDrone.Core/Tv/SeriesPathBuilder.cs verbatim with type swap (Series→Manga,
    // GetSeriesFolder→GetMangaFolder). Flat folder layout per D-15: <root>/<Manga.Title>.
    // Phase 8 cleanup: collapse to canonical PathBuilder when Tv/ deletes.
    public class MangaPathBuilder : IBuildMangaPaths
    {
        private readonly IBuildMangaFileNames _fileNameBuilder;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public MangaPathBuilder(IBuildMangaFileNames fileNameBuilder, IRootFolderService rootFolderService, Logger logger)
        {
            _fileNameBuilder = fileNameBuilder;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public string BuildPath(MangaModel manga, bool useExistingRelativeFolder)
        {
            if (manga.RootFolderPath.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("Root folder was not provided", nameof(manga));
            }

            if (useExistingRelativeFolder && manga.Path.IsNotNullOrWhiteSpace())
            {
                var relativePath = GetExistingRelativePath(manga);
                return Path.Combine(manga.RootFolderPath, relativePath);
            }

            return Path.Combine(manga.RootFolderPath, _fileNameBuilder.GetMangaFolder(manga));
        }

        private string GetExistingRelativePath(MangaModel manga)
        {
            // Mirror SeriesPathBuilder.cs lines 44-58 verbatim with type swap.
            var rootFolderPath = _rootFolderService.GetBestRootFolderPath(manga.Path);

            if (rootFolderPath.IsParentPath(manga.Path))
            {
                return rootFolderPath.GetRelativePath(manga.Path);
            }

            var directoryName = manga.Path.GetDirectoryName();

            _logger.Warn("Unable to get relative path for manga path {0}, using manga folder name {1}", manga.Path, directoryName);

            return directoryName;
        }
    }
}
