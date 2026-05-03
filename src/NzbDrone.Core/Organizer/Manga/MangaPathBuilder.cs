using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using ChapterModel = NzbDrone.Core.Manga.Chapter;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Organizer.Manga
{
    public interface IBuildMangaPaths
    {
        string BuildPath(MangaModel manga, bool useExistingRelativeFolder);

        // Phase 6 PIPELINE-04 — compose the destination path for an imported chapter.
        // Returns: <manga.RootFolderPath>/<mangaFolder>/<chapterFileName>.<ext>
        // where <mangaFolder> comes from MangaFileNameBuilder.GetMangaFolder and
        // <chapterFileName> from MangaFileNameBuilder.BuildFileName (Phase 5 deliverable).
        // Extension is preserved from sourcePath. Plan 06-07 ImportApprovedChapters consumes.
        string BuildChapterPath(MangaModel manga, ChapterModel chapter, string sourcePath);
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
                throw new System.ArgumentException("Root folder was not provided", nameof(manga));
            }

            if (useExistingRelativeFolder && manga.Path.IsNotNullOrWhiteSpace())
            {
                var relativePath = GetExistingRelativePath(manga);
                return Path.Combine(manga.RootFolderPath, relativePath);
            }

            return Path.Combine(manga.RootFolderPath, _fileNameBuilder.GetMangaFolder(manga));
        }

        public string BuildChapterPath(MangaModel manga, ChapterModel chapter, string sourcePath)
        {
            // Phase 6 PIPELINE-04. Reuses Phase 5 _fileNameBuilder.BuildFileName for the
            // chapter filename (which honors NamingConfig.StandardChapterFormat + reader-compat
            // presets) and GetMangaFolder for the manga directory. Extension preserved from
            // sourcePath so CBZ/folder/zip imports route correctly.
            //
            // Sonarr divergence: NEW manga path-builder method per Phase 6 — see DIVERGENCE.md.
            var ext = string.IsNullOrEmpty(sourcePath) ? string.Empty : Path.GetExtension(sourcePath);
            var chapterFileName = _fileNameBuilder.BuildFileName(
                new List<ChapterModel> { chapter },
                manga,
                release: null,
                extension: ext);

            // Honor existing manga.Path when set, otherwise compose from RootFolderPath + computed mangaFolder.
            // Mirrors MangaFileNameBuilder.BuildFilePath semantics — keeps a single source of truth for
            // path composition between import and rename paths.
            if (manga.Path.IsNotNullOrWhiteSpace())
            {
                return Path.Combine(manga.Path, chapterFileName);
            }

            var mangaFolder = _fileNameBuilder.GetMangaFolder(manga);
            return Path.Combine(manga.RootFolderPath ?? string.Empty, mangaFolder, chapterFileName);
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
