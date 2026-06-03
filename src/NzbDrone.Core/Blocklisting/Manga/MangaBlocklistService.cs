using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-11 + D-19 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Blocklisting/BlocklistService.cs.
    //
    // Event-driven service (RESEARCH Anti-Pattern: NEVER write blocklist rows from inside the
    // Repository). Subscribes to ChapterDownloadFailedEvent for auto-blocklist on terminal
    // failure + IHandleAsync<MangaDeletedEvent> for cascade cleanup + IExecute<ClearMangaBlocklistCommand>
    // for the "Clear blocklist" UI button.
    //
    // ORDERING INVARIANT for Plan 06-08 AutoRetryOrchestrator (D-19):
    //   1. _repository.Insert(blocklist)           — row committed first
    //   2. _eventAggregator.PublishEvent(...Added) — synchronous fan-out; subscribers see the row
    // Plan 06-08 subscribes to MangaBlocklistAddedEvent (NOT ChapterDownloadFailedEvent) so the
    // re-search cannot fire before the blocklist row exists; BlocklistSpecification then
    // correctly rejects release A on the next decision pass.
    //
    // PITFALL 5 mitigation in Blocklisted(int, ReleaseInfo):
    //   - Title: trim + ToLowerInvariant both sides + OrdinalIgnoreCase compare
    //   - ReleaseGuid: OrdinalIgnoreCase compare on the raw value
    //   - SourceKey: null-tolerant — when either side is null/empty, fall back to (Title, Guid)
    //     pair only. Otherwise OrdinalIgnoreCase compare.
    // The auto-retry loop (Plan 06-08) cannot pick up a release that was just blocklisted because
    // of case/whitespace/null variation in the (SourceKey, ReleaseGuid, Title) triple.
    //
    // Phase 8 cleanup: collapse with BlocklistService when Tv/ deletes.
    public class MangaBlocklistService : IMangaBlocklistService,
                                         IExecute<ClearMangaBlocklistCommand>,
                                         IHandle<ChapterDownloadFailedEvent>,
                                         IHandleAsync<MangaDeletedEvent>
    {
        private readonly IMangaBlocklistRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaBlocklistService(IMangaBlocklistRepository repository,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public bool Blocklisted(int mangaId, ReleaseInfo release)
        {
            if (release == null)
            {
                return false;
            }

            // Pitfall 5: trim + lowercase both sides; OrdinalIgnoreCase on string compares;
            // null-tolerant SourceKey fallback. The auto-retry loop (Plan 06-08) cannot
            // re-grab a just-blocklisted release because of case/whitespace/null variation.
            var normalizedTitle = (release.Title ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedGuid = release.Guid ?? string.Empty;
            var normalizedSourceKey = release.Indexer; // ReleaseInfo.Indexer == manga "SourceKey" per Phase 3 D-17.

            var candidates = _repository.BlocklistedByTitle(mangaId, normalizedTitle);
            return candidates.Any(b => MatchesIdentity(b, normalizedTitle, normalizedGuid, normalizedSourceKey));
        }

        private static bool MatchesIdentity(MangaBlocklist b, string title, string guid, string sourceKey)
        {
            // Title is normalized (trim + lower) on both sides.
            var bTitle = (b.SourceTitle ?? string.Empty).Trim();
            var titleMatch = bTitle.Equals(title, StringComparison.OrdinalIgnoreCase);

            // Guid: OrdinalIgnoreCase on raw values.
            var bGuid = b.ReleaseGuid ?? string.Empty;
            var guidMatch = bGuid.Equals(guid, StringComparison.OrdinalIgnoreCase);

            // Null-tolerant SourceKey fallback: when either side lacks a SourceKey, treat as match
            // on (Title, Guid) only. When both sides have a value, OrdinalIgnoreCase compare.
            bool sourceKeyMatch;
            if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(b.SourceKey))
            {
                sourceKeyMatch = true;
            }
            else
            {
                sourceKeyMatch = b.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase);
            }

            return titleMatch && guidMatch && sourceKeyMatch;
        }

        public PagingSpec<MangaBlocklist> Paged(PagingSpec<MangaBlocklist> pagingSpec)
        {
            return _repository.GetPaged(pagingSpec);
        }

        public void Block(MangaBlocklist blocklist, bool manual = false)
        {
            // Manual UI insert path. Mirrors the Handle(...) ordering invariant: Insert FIRST,
            // then PublishEvent. Keeps the AutoRetryOrchestrator subscriber contract uniform
            // regardless of whether the row originated from auto-blocklist or manual insert.
            //
            // GH #309: `manual` flows onto the event so AutoRetryOrchestrator can skip the
            // failure-budget auto-retry for user-initiated blocklists (the caller — e.g. the
            // queue-Remove controller — owns the explicit skipRedownload-gated re-search).
            _repository.Insert(blocklist);
            _eventAggregator.PublishEvent(new MangaBlocklistAddedEvent(blocklist, manual: manual));
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void Delete(List<int> ids)
        {
            _repository.DeleteMany(ids);
        }

        public void Execute(ClearMangaBlocklistCommand message)
        {
            _repository.Purge();
        }

        public void Handle(ChapterDownloadFailedEvent message)
        {
            // Phase 6 D-19 — auto-blocklist on terminal download failure. Plan 06-01 extended
            // ChapterDownloadFailedEvent with optional Source / DownloadClient / Release /
            // SourceTitle init-only properties. Phase 4 emit sites populate via object-initializer
            // where the data is in scope; null otherwise. Tolerate null Release for legacy emit
            // sites by falling back to event.Message as the SourceTitle.
            var release = message.Release;

            var blocklist = new MangaBlocklist
            {
                MangaId = message.MangaId,
                ChapterIds = new List<int> { message.ChapterId },
                SourceTitle = message.SourceTitle ?? release?.Title ?? message.Message,
                SourceKey = release?.Indexer,
                ReleaseGuid = release?.Guid,
                ReleaseInfoJson = release == null ? null : Json.ToJson(release),
                Date = DateTime.UtcNow,
                Reason = message.Message,
                Source = message.Source
            };

            // ORDERING INVARIANT (D-19):
            // 1. Insert FIRST — row must be committed before any event handler runs.
            _repository.Insert(blocklist);

            // 2. THEN publish the event. Sonarr's IEventAggregator.PublishEvent is synchronous
            //    fan-out, so any subscriber (e.g. Plan 06-08 AutoRetryOrchestrator) sees the
            //    inserted row when it queries the repository in response to this event.
            _eventAggregator.PublishEvent(new MangaBlocklistAddedEvent(blocklist, message));
        }

        public void HandleAsync(MangaDeletedEvent message)
        {
            // Cascade-delete blocklist rows when the parent manga is removed. Mirrors TV's
            // BlocklistService.HandleAsync(SeriesDeletedEvent) at BlocklistService.cs:160-163.
            if (message?.Manga == null)
            {
                return;
            }

            _repository.DeleteForManga(message.Manga.Id);
        }
    }
}
