using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 8 backfill (audit gap
    // no-sibling/DeletedEpisodeFileSpecification). Mirrors TV
    // DecisionEngine/Specifications/RssSync/DeletedEpisodeFileSpecification.cs verbatim shape:
    // gates RSS-sync grabs when the chapter file row exists in the DB but the artifact is
    // missing from disk, so the next disk-scan can unmonitor instead of looping the same
    // release on every RSS tick.
    //
    // Scope decisions (vs audit's broader outline):
    //   * Reuses existing IConfigService.AutoUnmonitorPreviouslyDownloadedEpisodes — shared
    //     content-management toggle, not TV-specific. Phase 9 rename will sweep the name.
    //     NO new ConfigService key in this backfill.
    //   * Reuses DownloadRejectionReason.ChapterNotMonitored (analog of TV's
    //     EpisodeNotMonitored at line 56 of the TV original). NO new reject enum value.
    //   * Lives flat under DecisionEngine/Manga/Specifications/ — no RssSync/ subdir on the
    //     manga side (sibling specs like AlreadyImportedChapterSpecification are top level).
    //
    // Type=Temporary mirrors the TV original (line 26): the file may reappear (mounted
    // share, restored backup) so a future RSS tick can revisit instead of permanently
    // declining the release.
    //
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class DeletedChapterFileSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly IChapterFileService _chapterFileService;
        private readonly Logger _logger;

        public DeletedChapterFileSpecification(IDiskProvider diskProvider,
                                               IConfigService configService,
                                               IChapterFileService chapterFileService,
                                               Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _chapterFileService = chapterFileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Disk;
        public RejectionType Type => RejectionType.Temporary;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            if (!_configService.AutoUnmonitorPreviouslyDownloadedEpisodes)
            {
                return DownloadSpecDecision.Accept();
            }

            if (information?.MangaSearchCriteria != null)
            {
                _logger.Debug("Skipping deleted chapterfile check during search");
                return DownloadSpecDecision.Accept();
            }

            var missingChapterFiles = subject.Chapters
                                             .Where(c => c.ChapterFileId.HasValue && c.ChapterFileId.Value > 0)
                                             .Select(c => _chapterFileService.Get(c.ChapterFileId.Value))
                                             .Where(f => f != null)
                                             .DistinctBy(f => f.Id)
                                             .Where(f => IsChapterFileMissing(subject.Manga, f))
                                             .ToArray();

            if (missingChapterFiles.Any())
            {
                foreach (var missingChapterFile in missingChapterFiles)
                {
                    _logger.Trace("Chapter file {0} is missing from disk.", missingChapterFile.RelativePath);
                }

                _logger.Debug("Files for this chapter exist in the database but not on disk, will be unmonitored on next diskscan. skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterNotMonitored, "Chapter is not monitored");
            }

            return DownloadSpecDecision.Accept();
        }

        private bool IsChapterFileMissing(NzbDrone.Core.Manga.Manga manga, ChapterFile chapterFile)
        {
            var fullPath = Path.Combine(manga.Path, chapterFile.RelativePath);

            return !_diskProvider.FileExists(fullPath);
        }
    }
}
