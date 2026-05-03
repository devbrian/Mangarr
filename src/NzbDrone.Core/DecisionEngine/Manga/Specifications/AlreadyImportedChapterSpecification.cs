using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Mirrors AlreadyImportedSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/AlreadyImportedSpecification.cs (99 lines).
    // ADAPTATION HOTSPOT 6: DROPS the Quality.Equals comparison (TV-analog lines 68-71) — manga has
    // no quality model. Conservative semantics per Assumption A6: reject re-grab when monitored=true
    // AND chapter has a Grabbed+Imported history pair. Phase 6 owns "is this release better than
    // the already-imported one" upgrade decision.
    //
    // History coupling note: Phase 5 reuses IHistoryService.FindByEpisodeId(int) with chapter.Id
    // because Episode.Id and Chapter.Id share the int ModelBase.Id space. Phase 6 will introduce
    // FindByChapterId once it builds the manga history pipeline; this spec swaps to that method
    // with a one-line change at the call site.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class AlreadyImportedChapterSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IHistoryService _historyService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public AlreadyImportedChapterSpecification(IHistoryService historyService,
                                                   IConfigService configService,
                                                   Logger logger)
        {
            _historyService = historyService;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            var cdhEnabled = _configService.EnableCompletedDownloadHandling;
            if (!cdhEnabled)
            {
                _logger.Debug("Skipping already-imported check because CDH is disabled");
                return DownloadSpecDecision.Accept();
            }

            if (subject.Chapters == null || !subject.Chapters.Any())
            {
                return DownloadSpecDecision.Accept();
            }

            foreach (var chapter in subject.Chapters)
            {
                if (!chapter.Monitored)
                {
                    continue;
                }

                // Phase 5 reuses TV's FindByEpisodeId — chapter.Id and episode.Id share int Id space.
                // Phase 6 swaps to FindByChapterId when manga history pipeline lands.
                var history = _historyService.FindByEpisodeId(chapter.Id);
                if (history == null || !history.Any())
                {
                    continue;
                }

                var grabbed = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed);
                var imported = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.DownloadFolderImported);

                if (grabbed != null && imported != null)
                {
                    _logger.Debug("Chapter {0} already imported; rejecting re-grab (Phase 6 owns upgrade decision)", chapter.Id);
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterAlreadyImported, "Chapter already imported");
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
