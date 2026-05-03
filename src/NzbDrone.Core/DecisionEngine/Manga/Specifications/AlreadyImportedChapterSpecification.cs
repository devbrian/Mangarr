using System.Linq;
using NLog;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV AlreadyImportedSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/AlreadyImportedSpecification.cs (99 lines).
    //
    // Phase 6 D-21 STUB body replacement — wires the Phase 5 STUB to the new Phase 6
    // ChapterHistoryService. BL-01 fix: queries ChapterHistory.ChapterId (NOT
    // EpisodeHistory.EpisodeId — independent autoincrement sequences across the two tables).
    //
    // Phase 5 history (preserved for context): originally this spec wrong-table-queried the
    // TV-side IHistoryService by-episode lookup with Chapter.Id values, returning unrelated TV
    // history rows when Episode.Id and Chapter.Id ints collided across independent SQLite
    // autoincrement sequences. Phase 6 ships the ChapterHistory parallel sibling table and
    // FindByChapterId queries it directly — the cross-domain lookup path is gone.
    //
    // Implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard preserved); priority =
    // Database (short-circuits before Default-priority specs); type = Permanent. Mirrors
    // BlocklistSpecification + QueueDuplicateSpecification stub pattern. The 11-spec
    // auto-discovery count (F-01 fixture Be(11)) remains stable — class shape unchanged,
    // only the ctor adds an IChapterHistoryService dependency.
    //
    // ADAPTATION HOTSPOT 6 (preserved): DROPS the Quality.Equals comparison (TV-analog
    // lines 68-71) — manga has no quality model. Conservative semantics per Assumption A6:
    // reject re-grab when monitored=true AND chapter has an Imported history row.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class AlreadyImportedChapterSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IChapterHistoryService _chapterHistoryService;
        private readonly Logger _logger;

        public AlreadyImportedChapterSpecification(IChapterHistoryService chapterHistoryService, Logger logger)
        {
            _chapterHistoryService = chapterHistoryService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // Phase 6 D-21 STUB body replacement — wires Phase 5 STUB.
            // BL-01 fix: queries ChapterHistory.ChapterId (NOT EpisodeHistory.EpisodeId).
            foreach (var chapter in subject.Chapters)
            {
                if (!chapter.Monitored)
                {
                    continue;
                }

                var imported = _chapterHistoryService.FindByChapterId(chapter.Id)
                    .FirstOrDefault(h => h.EventType == ChapterHistoryEventType.Imported);

                if (imported != null)
                {
                    _logger.Debug("Chapter {0} already imported at {1}, rejecting", chapter.Id, imported.Date);
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterAlreadyImported,
                        "Chapter already imported");
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
