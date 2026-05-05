using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no_sibling/UpdateEpisodeFileService).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs (TV).
    // Reuses the shared _configService.FileDate setting + FileDateType enum — no new
    // ConfigService key (corrects audit's "ChapterFileDate" suggestion). Sets ChapterFile
    // mtime to Chapter.ReleaseDate (manga analog of TV's AirDateUtc per PROJECT.md mapping).
    //
    // IHandle<MangaScannedEvent> subscriber wiring is deferred until Plan 03-06 ships
    // MangaScannedEvent. Service is callable directly via IUpdateChapterFileService until then.
    //
    // Phase 8 cleanup: collapse with UpdateEpisodeFileService when Tv/ deletes.
    public interface IUpdateChapterFileService
    {
        void ChangeFileDateForFile(ChapterFile chapterFile, Manga.Manga manga, List<Chapter> chapters);
    }

    public class UpdateChapterFileService : IUpdateChapterFileService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpdateChapterFileService(IDiskProvider diskProvider,
                                        IConfigService configService,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public void ChangeFileDateForFile(ChapterFile chapterFile, Manga.Manga manga, List<Chapter> chapters)
        {
            ChangeFileDate(chapterFile, manga, chapters);
        }

        private bool ChangeFileDate(ChapterFile chapterFile, Manga.Manga manga, List<Chapter> chapters)
        {
            var chapterFilePath = Path.Combine(manga.Path, chapterFile.RelativePath);

            // Pick the chapter with the most recent ReleaseDate (analog of TV picking the
            // latest aired episode). Skips chapters without a ReleaseDate.
            var releaseDateUtc = chapters
                .Where(c => c.ReleaseDate.HasValue)
                .OrderByDescending(c => c.ReleaseDate.Value)
                .Select(c => (DateTime?)c.ReleaseDate.Value)
                .FirstOrDefault();

            if (!releaseDateUtc.HasValue)
            {
                return false;
            }

            return _configService.FileDate switch
            {
                FileDateType.LocalAirDate =>
                    ChangeFileDateToLocalDate(chapterFilePath, releaseDateUtc.Value.ToLocalTime()),

                // Intentionally pass UTC as local per user preference (mirrors TV)
                FileDateType.UtcAirDate =>
                    ChangeFileDateToLocalDate(
                        chapterFilePath,
                        DateTime.SpecifyKind(releaseDateUtc.Value, DateTimeKind.Local)),

                _ => false,
            };
        }

        private bool ChangeFileDateToLocalDate(string filePath, DateTime localDate)
        {
            // FileGetLastWrite returns UTC; convert to local to compare
            var oldLastWrite = _diskProvider.FileGetLastWrite(filePath).ToLocalTime();

            if (OsInfo.IsNotWindows && localDate.ToUniversalTime() < DateTimeExtensions.EpochTime)
            {
                _logger.Debug("Setting date of file to 1970-01-01 as actual release date is before that time and will not be set properly");
                localDate = DateTimeExtensions.EpochTime.ToLocalTime();
            }

            if (!DateTime.Equals(localDate.WithoutTicks(), oldLastWrite.WithoutTicks()))
            {
                try
                {
                    // Preserve prior mtime subseconds (mirrors TV per Sonarr issue #7228)
                    var mtime = localDate.WithTicksFrom(oldLastWrite);

                    _diskProvider.FileSetLastWriteTime(filePath, mtime);
                    _logger.Debug("Date of file [{0}] changed from '{1}' to '{2}'", filePath, oldLastWrite, mtime);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to set date of file [" + filePath + "]");
                }
            }

            return false;
        }
    }
}
