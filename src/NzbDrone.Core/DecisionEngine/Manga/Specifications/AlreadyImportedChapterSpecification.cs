using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV AlreadyImportedSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/AlreadyImportedSpecification.cs (99 lines).
    //
    // Phase 5 STUB per code-review BL-01 — TODO Phase 6 wire IHistoryService.FindByChapterId(int).
    // Originally this spec called _historyService.FindByEpisodeId(chapter.Id) to short-circuit
    // re-grabs once a Grabbed+Imported history pair existed. That method filters on the TV
    // History.EpisodeId column; Episode.Id and Chapter.Id come from independent SQLite
    // autoincrement sequences, so any value where both happen to exist (e.g. Chapter.Id=100
    // AND Episode.Id=100) silently returned TV history for an unrelated TV episode and
    // rejected the manga release as "already imported" (cross-pollination).
    //
    // The fix lives in Phase 6: add a manga-aware overload (IHistoryService.FindByChapterId,
    // or a manga-side IMangaHistoryService) gated on the manga history pipeline. Until that
    // substrate lands, this spec ships as Accept-always so the auto-discovery 11-spec count
    // + DI-resolution test in plan 05-07 pass without blocking on Phase 6 deliverables and
    // without driving the false-rejection bug.
    //
    // Implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard preserved); priority =
    // Database (short-circuits before Default-priority specs once wired); type = Permanent.
    // Mirrors the BlocklistSpecification + QueueDuplicateSpecification stub pattern.
    //
    // ADAPTATION HOTSPOT 6 (preserved for Phase 6): DROPS the Quality.Equals comparison
    // (TV-analog lines 68-71) — manga has no quality model. Conservative semantics per
    // Assumption A6: reject re-grab when monitored=true AND chapter has a Grabbed+Imported
    // history pair. Phase 6 owns "is this release better than the already-imported one"
    // upgrade decision.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class AlreadyImportedChapterSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public AlreadyImportedChapterSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // Phase 5 STUB per code-review BL-01 — TODO Phase 6 wire IHistoryService.FindByChapterId.
            return DownloadSpecDecision.Accept();
        }
    }
}
