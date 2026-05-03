using NLog;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog (loose): src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // NotSampleSpecification.cs — TV uses media-info to detect sample files; manga uses the
    // simpler size-zero check on the staging CBZ produced by Phase 4 InProcessImageDownloadClient.
    //
    // Empty archive = Phase 4 wrote a zero-byte CBZ (signal of failure mid-archive). The
    // import pipeline must reject so the auto-retry orchestrator (Plan 06-08) re-fires.
    //
    // Phase 8 cleanup: collapse with TV NotSampleSpecification when Tv/ deletes.
    public class NotEmptyArchiveSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public NotEmptyArchiveSpecification(Logger logger)
        {
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (localChapter.Size > 0)
            {
                return MangaImportSpecDecision.Accept();
            }

            _logger.Warn("Empty archive at {0}; rejecting", localChapter.Path);
            return MangaImportSpecDecision.Reject(ImportRejectionReason.EmptyArchive, "Archive is empty");
        }
    }
}
