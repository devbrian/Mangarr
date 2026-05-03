using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV AcceptableSizeSpecification (size sanity floor).
    // Manga sanity: reject releases under 50 KB — typical signal of HTML error page or empty
    // CBZ. Per-manga / per-quality bounds (TV pattern) not applicable; manga has no quality model.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class AcceptableSizeSpecification : IMangaDecisionEngineSpecification
    {
        private const long MinAcceptableBytes = 50 * 1024;   // 50 KB floor — empty CBZ / HTML error page guard
        private readonly Logger _logger;

        public AcceptableSizeSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            if (subject.Release == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var size = subject.Release.Size;
            if (size <= 0)
            {
                // Indexer didn't provide a size — accept and let Phase 4 downloader catch problems.
                return DownloadSpecDecision.Accept();
            }

            if (size < MinAcceptableBytes)
            {
                _logger.Debug("Release size {0} bytes below acceptable manga floor {1} bytes", size, MinAcceptableBytes);
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.SizeTooSmall,
                    "Release size {0} bytes below acceptable manga floor (50 KB)",
                    size);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
