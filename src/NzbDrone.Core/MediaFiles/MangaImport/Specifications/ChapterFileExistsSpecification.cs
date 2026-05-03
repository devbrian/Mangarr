using NLog;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // (closest match: ChapterFile-existence is roughly TV's "EpisodeFileId already set" guard
    // checked inside UpgradeSpecification; manga splits it out for cleaner idempotency).
    //
    // Idempotency guard. If Chapter.ChapterFileId is already set (Plan 06-01 PIPELINE-04 column),
    // the chapter has been imported in a prior run and re-importing without an upgrade-allowed
    // path through UpgradeSpecification would silently overwrite the existing ChapterFile.
    // Reject early so the orchestrator sees a clean "ChapterFileExists" rejection.
    //
    // Phase 8 cleanup: collapse with TV idempotency check when Tv/ deletes.
    public class ChapterFileExistsSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public ChapterFileExistsSpecification(Logger logger)
        {
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (localChapter.ExistingFile)
            {
                return MangaImportSpecDecision.Accept();
            }

            if (localChapter.Chapter == null)
            {
                return MangaImportSpecDecision.Accept();
            }

            if (localChapter.Chapter.ChapterFileId.GetValueOrDefault() > 0)
            {
                _logger.Debug(
                    "Chapter {0} already has a ChapterFile ({1}); rejecting re-import",
                    localChapter.Chapter.Id,
                    localChapter.Chapter.ChapterFileId);

                return MangaImportSpecDecision.Reject(
                    ImportRejectionReason.ChapterFileExists,
                    "Chapter {0} already has a file",
                    localChapter.Chapter.Id);
            }

            return MangaImportSpecDecision.Accept();
        }
    }
}
