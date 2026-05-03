using NLog;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/DecisionEngine/Specifications/BlocklistSpecification.cs.
    //
    // Phase 6 D-19 STUB body replacement — wires the Phase 5 Accept-always STUB to the new
    // IMangaBlocklistService introduced in Plan 06-04. The matching logic lives in the service
    // (Pitfall 5: trim + lowercase + OrdinalIgnoreCase + null-tolerant SourceKey fallback) so
    // the spec stays a thin gate that returns Reject(Blocklisted) when the service says yes.
    //
    // The class still implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard
    // preserved); priority = Database (short-circuits before Default-priority specs); type =
    // Permanent. Class shape and interface implementation are unchanged from the Phase 5 STUB
    // — only the ctor adds an IMangaBlocklistService dependency. The 11-spec auto-discovery
    // count (F-01 fixture Be(11)) remains stable.
    //
    // PITFALL 6 GUARD: AutoMoqer-style fixtures consuming this spec MUST register
    // IMangaBlocklistService or the spec drops from IEnumerable<IMangaDecisionEngineSpecification>
    // resolution and the count fails 11→10. The Wave 5 F-01 fixture (Plan 06-12) covers this;
    // local fixtures in this plan inject the mock directly.
    //
    // Phase 8 cleanup: collapse with TV BlocklistSpecification when Tv/ deletes.
    public class BlocklistSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IMangaBlocklistService _mangaBlocklistService;
        private readonly Logger _logger;

        public BlocklistSpecification(IMangaBlocklistService mangaBlocklistService, Logger logger)
        {
            _mangaBlocklistService = mangaBlocklistService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // Phase 6 D-19 STUB body replacement — wires Phase 5 STUB.
            if (_mangaBlocklistService.Blocklisted(subject.Manga.Id, subject.Release))
            {
                _logger.Debug("{0} is blocklisted, rejecting", subject.Release.Title);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.Blocklisted,
                    "Release is blocklisted");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
