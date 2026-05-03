using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Phase 5 STUB per planner-revision Issue #8: ships as Accept-always so the auto-discovery
    // 11-spec count + DI-resolution test in plan 05-07 pass without blocking on Phase 6 Blocklist UX.
    // The class still implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard preserved);
    // priority = Database (short-circuits before Default-priority specs once wired); type = Permanent.
    //
    // TODO Phase 6 — wire IBlocklistService manga overload (Blocklisted(int mangaId, ReleaseInfo))
    // when 06-CONTEXT.md is authored. The wired body should mirror
    // src/NzbDrone.Core/DecisionEngine/Specifications/BlocklistSpecification.cs:1-33 verbatim
    // with Manga.Id substituted for Series.Id.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class BlocklistSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public BlocklistSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // TODO Phase 6 — wire IBlocklistService manga overload.
            return DownloadSpecDecision.Accept();
        }
    }
}
