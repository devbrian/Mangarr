using System.Collections.Generic;
using NLog;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no-sibling/SeriesScannedHandler.md)
    // — see DIVERGENCE.md.
    //
    // Mirrors Tv/SeriesScannedHandler.cs verbatim shape and behavior:
    //   * IHandle<MangaScannedEvent> — runs the post-add lifecycle once disk-scan is done.
    //   * Reads Manga.AddOptions (Phase 8 audit gap-03 — added in Plan 03-10).
    //   * No AddOptions => trigger ChapterRefreshedService.Search (cluster 01) backfill auto-search; return.
    //   * AddOptions present =>
    //       1. Apply per-Chapter Monitored flags via IChapterMonitoredService.SetChapterMonitoredStatus
    //          (Phase 8 audit no-sibling/EpisodeMonitoredService.md sibling).
    //       2. Trigger ChapterRefreshedService.Search backfill auto-search.
    //       3. Push search commands per the Search* flags. Mirrors TV's combined-flags shortcut: if BOTH
    //          SearchForMissingChapters && SearchForCutoffUnmetChapters are set, push ONE bulk
    //          MangaSearchCommand (whole-manga search, monitored-chapter-only filter) — TV equivalent
    //          of SeriesSearchCommand. Otherwise push MissingChapterSearchCommand for the missing-only
    //          flag, and CutoffUnmetChapterSearchCommand for the cutoff-unmet-only flag.
    //       4. Clear AddOptions via IMangaService.RemoveAddOptions (mirrors
    //          Tv/SeriesService.RemoveAddOptions — uses repository SetFields so only the AddOptions
    //          column is persisted and no MangaUpdatedEvent is fired).
    //       5. Publish MangaAddCompletedEvent.
    //
    // Sonarr divergence: TV also handles SeriesScanSkippedEvent. The manga-side scan-skipped event
    // does not yet exist (Plan 12-01 / disk-scan territory) — when it lands, add a second
    // IHandle<MangaScanSkippedEvent> with the same HandleScanEvents body.
    //
    // DryIoc auto-discovers IHandle<> subscribers; no manual DI registration required.
    public class MangaScannedHandler : IHandle<MangaScannedEvent>
    {
        private readonly IChapterMonitoredService _chapterMonitoredService;
        private readonly IMangaService _mangaService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IChapterRefreshedService _chapterRefreshedService;
        private readonly IEventAggregator _eventAggregator;

        private readonly Logger _logger;

        public MangaScannedHandler(IChapterMonitoredService chapterMonitoredService,
                                   IMangaService mangaService,
                                   IManageCommandQueue commandQueueManager,
                                   IChapterRefreshedService chapterRefreshedService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _chapterMonitoredService = chapterMonitoredService;
            _mangaService = mangaService;
            _commandQueueManager = commandQueueManager;
            _chapterRefreshedService = chapterRefreshedService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private void HandleScanEvents(Manga manga)
        {
            var addOptions = manga.AddOptions;

            if (addOptions == null)
            {
                _chapterRefreshedService.Search(manga.Id);
                return;
            }

            _logger.Info("[{0}] was recently added, performing post-add actions", manga.Title);
            _chapterMonitoredService.SetChapterMonitoredStatus(manga, addOptions);

            _chapterRefreshedService.Search(manga.Id);

            // Mirrors TV SeriesScannedHandler combined-flags shortcut: when BOTH search flags are
            // set, push ONE whole-manga MangaSearchCommand (which Phase 5 specs filter to monitored
            // chapters only). Avoids duplicate-search overlap on the same chapter set.
            if (addOptions.SearchForMissingChapters && addOptions.SearchForCutoffUnmetChapters)
            {
                _commandQueueManager.Push(new MangaSearchCommand(new List<int> { manga.Id }));
            }
            else
            {
                if (addOptions.SearchForMissingChapters)
                {
                    _commandQueueManager.Push(new MissingChapterSearchCommand(manga.Id));
                }

                if (addOptions.SearchForCutoffUnmetChapters)
                {
                    // Narrow cutoff-only sweep. Consumer = CutoffUnmetChapterSearchService (Phase 8 audit gap-04).
                    _commandQueueManager.Push(new CutoffUnmetChapterSearchCommand(manga.Id));
                }
            }

            // Clear AddOptions via the RemoveAddOptions sibling (mirrors Tv/SeriesService.RemoveAddOptions
            // — uses SetFields so only the AddOptions column is persisted and no MangaUpdatedEvent fires).
            _mangaService.RemoveAddOptions(manga);

            _eventAggregator.PublishEvent(new MangaAddCompletedEvent(manga));
        }

        public void Handle(MangaScannedEvent message)
        {
            HandleScanEvents(message.Manga);
        }
    }
}
