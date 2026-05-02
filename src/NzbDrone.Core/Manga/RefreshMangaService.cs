using System;
using NLog;
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
        private readonly IChapterListService _chapterListService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RefreshMangaService(IMetadataSourceFactory metaFactory,
                                   IMangaService mangaService,
                                   IChapterListService chapterListService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _metaFactory = metaFactory;
            _mangaService = mangaService;
            _chapterListService = chapterListService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(RefreshMangaCommand message)
        {
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
                        existing.Title, primaryDef.Name, existing.Id);
                    continue;
                }

                try
                {
                    var tuple = primary.GetMangaInfo(sourceId);
                    var mangaInfo = tuple.Item1;
                    var chapters = tuple.Item2;
                    existing.ApplyChanges(mangaInfo);
                    _mangaService.UpdateManga(existing);

                    // D-17: chapter-list synthesis fallback when MangaDex not linked.
                    _chapterListService.SyncChapters(existing, chapters);

                    _eventAggregator.PublishEvent(new MangaUpdatedEvent(existing));
                }
                catch (MangaNotFoundException) when (!message.IsNewManga)
                {
                    _logger.Warn("Manga {0} not found at primary source — preserving existing data",
                        existing.Title);
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
                }
            }
        }
    }
}
