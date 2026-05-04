using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-04 RefreshMangaCommand executor. Per D-22 we process IDs sequentially to keep
    /// the per-source rate-limit budget pressure low. The active primary is resolved
    /// dynamically via <see cref="IMetadataSourceFactory.GetPrimary"/> per D-15 — promoting
    /// a different provider via <c>SetPrimary</c> redirects subsequent refreshes without
    /// editing this class.
    /// </summary>
    public class RefreshMangaService : IExecute<RefreshMangaCommand>
    {
        private readonly IMetadataSourceFactory _metaFactory;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IChapterListService _chapterListService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly Logger _logger;

        public RefreshMangaService(IMetadataSourceFactory metaFactory,
                                   IMangaService mangaService,
                                   IChapterService chapterService,
                                   IChapterListService chapterListService,
                                   IEventAggregator eventAggregator,
                                   ICommandResultReporter commandResultReporter,
                                   Logger logger)
        {
            _metaFactory = metaFactory;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _chapterListService = chapterListService;
            _eventAggregator = eventAggregator;
            _commandResultReporter = commandResultReporter;
            _logger = logger;
        }

        public void Execute(RefreshMangaCommand message)
        {
            // gap-01: publish the "refresh starting" pulse BEFORE any iteration.
            // Mirrors TV RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:215)
            // — UI / SignalR subscribers need this to surface a "refreshing" indicator
            // at parity with TV's UX feedback model. Pairs with the trailing
            // MangaRefreshCompleteEvent (gap-02) emitted after the iteration finishes.
            _eventAggregator.PublishEvent(new MangaRefreshStartingEvent(message.Trigger == CommandTrigger.Manual));

            var ids = (message.MangaIds == null || message.MangaIds.Count == 0)
                ? _mangaService.AllMangaIds()
                : message.MangaIds;

            var primaryDef = _metaFactory.GetPrimary();
            var primary = (IProvideMangaInfo)_metaFactory.GetInstance(primaryDef);

            // PER D-22: sequential per manga to keep concurrent budget pressure low.
            foreach (var id in ids)
            {
                var existing = _mangaService.GetManga(id);
                if (existing == null)
                {
                    continue;
                }

                // Use the cross-resolved ID matching this primary.
                var sourceId = primary switch
                {
                    MangaDexMetadataSource _ => existing.MangaDexId?.ToString(),
                    AniListMetadataSource _ => existing.AniListId?.ToString(),
                    MyAnimeListMetadataSource _ => existing.MalId?.ToString(),
                    _ => null,
                };

                if (string.IsNullOrEmpty(sourceId))
                {
                    // WR-08 fix: escalate from Trace to Warn so users notice that a
                    // manga in their library is silently being skipped because the
                    // active primary has no cross-source ID for it. The CONTEXT D-23
                    // manual-relink endpoint is the remediation path; without a
                    // visible signal users would never know it's needed. (Phase 7+
                    // can extend this to a MissingPrimarySourceIdHealthCheck per the
                    // review's secondary recommendation.)
                    _logger.Warn("Skipping manga {0}: no source ID for active primary {1}; manual relink required (POST /api/v5/manga/{2}/links)",
                        existing.Title,
                        primaryDef.Name,
                        existing.Id);
                    continue;
                }

                try
                {
                    var tuple = primary.GetMangaInfo(sourceId);
                    var mangaInfo = tuple.Item1;
                    var chapters = tuple.Item2;
                    existing.ApplyChanges(mangaInfo);

                    // gap-06: mirror RefreshSeriesService.RefreshSeriesInfo
                    // (Tv/RefreshSeriesService.cs:116-124) — normalize Manga.Path to
                    // its full absolute form with actual disk casing on every refresh.
                    // Defends against OS-level renames (Windows casing drift) so the
                    // file-import pipeline can still match canonical paths.
                    try
                    {
                        existing.Path = new DirectoryInfo(existing.Path).FullName;
                        existing.Path = existing.Path.GetActualCasing();
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Couldn't update manga path for " + existing.Path);
                    }

                    // gap-11: suppress UpdateManga's event publish so the trailing
                    // PublishEvent below is the SOLE MangaUpdatedEvent per refresh,
                    // emitted AFTER SyncChapters runs. Mirrors TV
                    // RefreshSeriesService.RefreshSeriesInfo's UpdateSeries(publishUpdatedEvent:false)
                    // → RefreshEpisodeInfo → PublishEvent(SeriesUpdatedEvent) ordering
                    // (Pitfall 4 invariant: DB write FIRST, event LAST).
                    _mangaService.UpdateManga(existing, publishUpdatedEvent: false);

                    // Phase 8 backfill (audit gap: no-sibling/EpisodeRefreshedService.md +
                    // RefreshSeriesService-vs-RefreshMangaService.md gap-10 reclassified):
                    // snapshot the chapter set BEFORE SyncChapters so we can compute the
                    // (added/updated/removed) delta for ChapterInfoRefreshedEvent. Mirrors
                    // RefreshEpisodeService.RefreshEpisodeInfo (Tv/RefreshEpisodeService.cs:131)
                    // which publishes EpisodeInfoRefreshedEvent with the equivalent delta.
                    //
                    // SyncChapters does not return a delta (its public surface predates this
                    // requirement), so we snapshot+diff here. The diff key is ChapterId — rows
                    // whose ID exists in both snapshots count as "updated" (SyncChapters may
                    // have flipped IsSynthetic and other fields in place); IDs only present
                    // post-sync are "added"; IDs only present pre-sync are "removed".
                    var beforeIds = _chapterService.GetChaptersByManga(existing.Id)
                        .ToDictionary(c => c.Id);

                    // D-17: chapter-list synthesis fallback when MangaDex not linked.
                    _chapterListService.SyncChapters(existing, chapters);

                    var afterChapters = _chapterService.GetChaptersByManga(existing.Id);
                    var added = afterChapters.Where(c => !beforeIds.ContainsKey(c.Id)).ToList();
                    var updated = afterChapters.Where(c => beforeIds.ContainsKey(c.Id)).ToList();
                    var removed = beforeIds.Values.Where(c => afterChapters.All(a => a.Id != c.Id)).ToList();

                    _eventAggregator.PublishEvent(new ChapterInfoRefreshedEvent(existing, added, updated, removed));

                    _eventAggregator.PublishEvent(new MangaUpdatedEvent(existing));
                }
                catch (MangaNotFoundException) when (!message.IsNewManga)
                {
                    _logger.Warn("Manga {0} not found at primary source — preserving existing data",
                        existing.Title);

                    // gap-07: mirror RefreshSeriesService.RefreshSeriesInfo
                    // (Tv/RefreshSeriesService.cs:70-81) — when the primary source no longer
                    // returns this manga, flip Status to the "deleted" string-sentinel so the
                    // UI can surface "removed at source". String sentinel (not a schema
                    // column) is the audit's stated alternative to a new RemovedAtSource
                    // bool — joins the documented set on Manga.cs:54
                    // (ongoing | completed | hiatus | cancelled | deleted). Persists via
                    // UpdateManga(publishUpdatedEvent:false) so the trailing
                    // PublishEvent(MangaUpdatedEvent) is the SOLE update event for this
                    // iteration (Pitfall 4 invariant: DB write FIRST, event LAST).
                    //
                    // Divergence from TV: do NOT rethrow. Manga refresh is batch-tolerant
                    // per WR-07 — one removed-at-source manga must not abort the whole loop.
                    // TV rethrows because its outer Execute treats single-series refreshes
                    // as fatal; manga's outer Execute is sequential-tolerant by design.
                    if (existing.Status != "deleted")
                    {
                        existing.Status = "deleted";
                        _mangaService.UpdateManga(existing, publishUpdatedEvent: false);
                        _logger.Debug("Manga marked as deleted at source for {0}", existing.Title);
                        _eventAggregator.PublishEvent(new MangaUpdatedEvent(existing));
                    }

                    // gap-09: mirror RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:235)
                    // — flag the command result Indeterminate so a partial-success batch is
                    // not falsely marked Completed by the command queue / health check.
                    _commandResultReporter.Report(CommandResult.Indeterminate);
                }
                catch (Exception ex)
                {
                    // WR-07 fix: catch every non-MangaNotFound failure so one bad
                    // manga (HTTP 503, JSON deser error, MangaDex maintenance, …)
                    // does NOT abort the entire refresh batch. D-22 mandates
                    // sequential refreshes specifically to keep one bad manga from
                    // blowing up the whole loop; without this catch the previous
                    // implementation did the opposite.
                    _logger.Warn(ex, "Refresh failed for manga {0}; skipping and continuing", existing.Title);

                    // gap-09: mirror RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:245)
                    // — flag the command result Indeterminate so a partial-success batch is
                    // not falsely marked Completed by the command queue / health check.
                    _commandResultReporter.Report(CommandResult.Indeterminate);
                }
            }

            // gap-02: publish the "refresh complete" pulse AFTER the iteration finishes.
            // Mirrors TV RefreshSeriesService.Execute's trailing
            // PublishEvent(new SeriesRefreshCompleteEvent()) — UI / SignalR subscribers
            // need this to clear the "refreshing" indicator. Pairs with the (gap-01)
            // MangaRefreshStartingEvent emitted at the top of Execute.
            _eventAggregator.PublishEvent(new MangaRefreshCompleteEvent());
        }
    }
}
