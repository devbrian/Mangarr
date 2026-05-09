using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Phase 8 backfill (audit gap: no-sibling/EpisodeRefreshedService.md). Manga sibling
    // of Sonarr's Tv/EpisodeRefreshedService.cs — see DIVERGENCE.md.
    //
    // Two-step pattern (verbatim mirror of the TV original):
    //   1) IHandle<ChapterInfoRefreshedEvent> caches IDs of newly-added monitored chapters
    //      whose ReleaseDate is recent (last 14 days, manga analog of TV's AirDateUtc per
    //      PROJECT.md "Air Date → Release Date" mapping).
    //   2) public Search(int mangaId) is called from the post-scan trigger site (the
    //      future MangaScannedHandler — Plan 03-11 territory). It pulls cached chapter
    //      IDs minus those that already have a ChapterFile imported, then pushes a
    //      ChapterSearchCommand for the remainder.
    //
    // Sonarr divergence: the TV original additionally walks message.Updated for the
    // `AbsoluteEpisodeNumberAdded` flag (anime renumbering retro-search). Manga has no
    // anime renumbering concept, so that branch is intentionally dropped.
    //
    // DryIoc auto-discovers via the IHandle<> + interface conventions; no manual DI
    // registration required.
    public interface IChapterRefreshedService
    {
        void Search(int mangaId);
    }

    public class ChapterRefreshedService : IChapterRefreshedService, IHandle<ChapterInfoRefreshedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;
        private readonly ICached<List<int>> _searchCache;

        public ChapterRefreshedService(ICacheManager cacheManager,
                                       IManageCommandQueue commandQueueManager,
                                       IChapterService chapterService,
                                       Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _chapterService = chapterService;
            _logger = logger;
            _searchCache = cacheManager.GetCache<List<int>>(GetType());
        }

        public void Search(int mangaId)
        {
            var previouslyReleased = _searchCache.Find(mangaId.ToString());

            if (previouslyReleased != null && previouslyReleased.Any())
            {
                // Re-fetch each chapter and filter to those still without a ChapterFile.
                // Mirrors EpisodeRefreshedService.Search's `e.HasFile` filter (manga's
                // ChapterFileId == null is the equivalent of Episode.EpisodeFileId == 0).
                var missing = previouslyReleased
                    .Select(id => _chapterService.GetChapter(id))
                    .Where(c => c != null && c.ChapterFileId == null)
                    .ToList();

                if (missing.Any())
                {
                    _commandQueueManager.Push(new ChapterSearchCommand(missing.Select(c => c.Id).ToList()));
                }
            }

            _searchCache.Remove(mangaId.ToString());
        }

        public void Handle(ChapterInfoRefreshedEvent message)
        {
            if (!message.Manga.Monitored)
            {
                _logger.Debug("Manga is not monitored");
                return;
            }

            // Sonarr divergence: Phase 16 D-02 — chapter.ReleaseDate -> chapter.FirstReleaseDate.
            var previouslyReleased = message.Added.Where(a =>
                    a.FirstReleaseDate.HasValue &&
                    a.FirstReleaseDate.Value.Between(DateTime.UtcNow.AddDays(-14), DateTime.UtcNow.AddDays(1)) &&
                    a.Monitored)
                .Select(c => c.Id)
                .ToList();

            if (previouslyReleased.Empty())
            {
                _logger.Debug("Newly added chapters all release in the future (or none added)");
                return;
            }

            _searchCache.Set(message.Manga.Id.ToString(), previouslyReleased.Distinct().ToList());
        }
    }
}
