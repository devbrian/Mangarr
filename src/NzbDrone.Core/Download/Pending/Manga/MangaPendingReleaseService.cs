using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.DecisionEngine.Manga.Aggregators;
using NzbDrone.Core.IndexerSearch.Manga;          // MangaRssSyncCompleteEvent (Plan 09-12) + MangaRssSyncCommand
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;   // ChapterGrabbedEvent
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Translations;        // TranslationProfileUpdatedEvent (Open Q §1)
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.Download.Pending.Manga
{
    // CS0118 mitigation: this file lives under the NzbDrone.Core.Download.Pending.Manga
    // sub-namespace, where unqualified `Manga` would resolve to the sub-namespace itself
    // rather than the POCO type. The namespace-scoped alias below pins `Manga` to the
    // type for body code (decision.RemoteChapter.Manga.Id, message.Manga, etc.).
    // Pattern matches IChapterFileService.cs:5 (Phase 6 precedent — commit 6597c94c7).
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-06..08 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs (701 lines).
    //
    // 9 public methods (skip TV's 3 *Obsolete — manga has no V3 API per Phase 14 D-12).
    // 9 IHandle subscribers (8 from D-09-07 + TranslationProfileUpdatedEvent per Open Q §1).
    //
    // CRITICAL — Pitfall 4 ordering: every state-mutating method commits the DB write
    // BEFORE publishing MangaPendingReleasesUpdatedEvent. The event is the LAST line.
    // See PATTERNS section A. ChapterGrabbedEvent is the success-side mutation trigger;
    // MangaRssSyncCompleteEvent (per Plan 09-12 audit gap-01 close-out — NOT TV's
    // RssSyncCompleteEvent) is the rejected-side mutation trigger.
    //
    // ANTI-RACE (per RESEARCH Open Q §2 + Pitfall 6): AutoRetryOrchestrator (Plan 06-08)
    // does NOT subscribe to MangaPendingReleasesUpdatedEvent — would create infinite
    // re-search loop. Auto-retry uses MangaBlocklistAddedEvent only.
    //
    // KNOWN LIMITATION (per RESEARCH Open Q §3 + Pitfall 5): GetDelay calls
    // DelayProfile.GetProtocolDelay(DownloadProtocol.Http) which falls through to UsenetDelay
    // (DelayProfile.cs:25-28 — Http is neither Usenet nor Torrent). Manga delay-profile
    // cooldown silently uses the Usenet number. v1.1 follow-up: extend DelayProfile with
    // explicit HttpDelay column. Logged in Plan 09-11 close-out to 08/deferred-items.md.
    //
    // RemoveGrabbed simplification vs TV (Phase 5 D-04 — manga has NO QualityProfile):
    // TV's RemoveGrabbed compares quality via QualityModelComparer to keep higher-quality
    // pending releases. Manga has no quality model, so we delete every pending release that
    // intersects the grabbed chapter ids without comparison. The CustomFormat score axis is
    // re-evaluated on the next RSS sync if a still-pending release ages out of cooldown.
    //
    // GetPendingQueue simplification vs TV: TV dedupes by (episode-id-set + QualityModelComparer)
    // ordering and protocol-priority. Manga drops the QualityModel axis (D-04). Dedup by
    // chapter-id-set is preserved; protocol-priority is preserved (only Http/Torrent matter).
    //
    // Phase 14 cleanup: collapse with PendingReleaseService when Tv/ deletes.
    public class MangaPendingReleaseService : IMangaPendingReleaseService,
                                              IHandle<MangaEditedEvent>,                       // was SeriesEditedEvent
                                              IHandle<MangaUpdatedEvent>,                      // was SeriesUpdatedEvent
                                              IHandle<MangaDeletedEvent>,                      // was SeriesDeletedEvent
                                              IHandle<ChapterGrabbedEvent>,                    // was EpisodeGrabbedEvent (Plan 06-01)
                                              IHandle<MangaRssSyncCompleteEvent>,              // PER PLAN 09-12 — manga-shape sibling (audit gap-01 close-out; NOT TV RssSyncCompleteEvent)
                                              IHandle<CustomFormatProfileUpdatedEvent>,        // was QualityProfileUpdatedEvent (D-04)
                                              IHandle<TranslationProfileUpdatedEvent>,         // 9th — Open Q §1 acceptance
                                              IHandle<ConfigSavedEvent>,                       // REUSE
                                              IHandle<ApplicationStartedEvent>                 // REUSE
    {
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IMangaPendingReleaseRepository _repository;
        private readonly IMangaParsingService _parsingService;
        private readonly IDelayProfileService _delayProfileService;
        private readonly ITaskManager _taskManager;
        private readonly IConfigService _configService;
        private readonly IRemoteChapterAggregationService _aggregationService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        // Static-list projection per PATTERNS section B (mirror TV PendingReleaseService.cs:66
        // verbatim — no lock; single-writer via IHandle, atomic-by-reference assignment in
        // UpdatePendingReleases()).
        private static List<MangaPendingRelease> _pendingReleases = new();

        public MangaPendingReleaseService(IIndexerStatusService indexerStatusService,
                                          IMangaPendingReleaseRepository repository,
                                          IMangaParsingService parsingService,
                                          IDelayProfileService delayProfileService,
                                          ITaskManager taskManager,
                                          IConfigService configService,
                                          IRemoteChapterAggregationService aggregationService,
                                          IDownloadClientFactory downloadClientFactory,
                                          IIndexerFactory indexerFactory,
                                          IEventAggregator eventAggregator,
                                          Logger logger)
        {
            _indexerStatusService = indexerStatusService;
            _repository = repository;
            _parsingService = parsingService;
            _delayProfileService = delayProfileService;
            _taskManager = taskManager;
            _configService = configService;
            _aggregationService = aggregationService;
            _downloadClientFactory = downloadClientFactory;
            _indexerFactory = indexerFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // ============================================================================
        // 9 public methods — bodies filled in Task 2b.
        // ============================================================================

        public void Add(MangaDownloadDecision decision, PendingReleaseReason reason)
        {
            throw new NotImplementedException();
        }

        public void AddMany(List<Tuple<MangaDownloadDecision, PendingReleaseReason>> decisions)
        {
            throw new NotImplementedException();
        }

        public List<ReleaseInfo> GetPending()
        {
            throw new NotImplementedException();
        }

        public List<RemoteChapter> GetPendingRemoteChapters(int mangaId)
        {
            throw new NotImplementedException();
        }

        public List<MangaQueueItem> GetPendingQueue()
        {
            throw new NotImplementedException();
        }

        public MangaQueueItem FindPendingQueueItem(int queueId)
        {
            throw new NotImplementedException();
        }

        public void RemovePendingQueueItems(int queueId)
        {
            throw new NotImplementedException();
        }

        public RemoteChapter OldestPendingRelease(int mangaId, int[] chapterIds)
        {
            throw new NotImplementedException();
        }

        // ============================================================================
        // Pitfall 4 wrappers — Insert (PATTERNS section A; mirrors TV
        // PendingReleaseService.cs:530-548 VERBATIM ordering: DB write FIRST, event LAST).
        // Implemented in Task 2a because every IHandle body in Task 2b calls them.
        // ============================================================================
        private void Insert(MangaDownloadDecision decision, PendingReleaseReason reason)
        {
            _repository.Insert(new MangaPendingRelease           // ← DB write FIRST
            {
                MangaId = decision.RemoteChapter.Manga.Id,
                ParsedChapterInfo = decision.RemoteChapter.ParsedChapterInfo,
                Release = decision.RemoteChapter.Release,
                Title = decision.RemoteChapter.Release.Title,
                Added = DateTime.UtcNow,
                Reason = reason,
                // No AdditionalInfo — manga has no SeriesMatchType/ReleaseSource analog (D-09-06).
            });

            _eventAggregator.PublishEvent(new MangaPendingReleasesUpdatedEvent());   // ← event LAST
        }

        private void Delete(MangaPendingRelease pendingRelease)
        {
            _repository.Delete(pendingRelease);                                       // ← DB write FIRST
            _eventAggregator.PublishEvent(new MangaPendingReleasesUpdatedEvent());   // ← event LAST
        }

        // ============================================================================
        // 9 IHandle implementations.
        // For state-mutating handlers, the wrappers above already enforce Pitfall 4
        // ordering. Pure-rebuild handlers just call UpdatePendingReleases().
        // Body refinement (e.g., RemoveRejected payload extraction) lives here in 2a
        // because the IHandle wiring is the public contract that DI auto-discovery
        // and the smoke test exercise on startup.
        // ============================================================================

        public void Handle(MangaEditedEvent message)
        {
            UpdatePendingReleases();
        }

        public void Handle(MangaUpdatedEvent message)
        {
            UpdatePendingReleases();
        }

        public void Handle(MangaDeletedEvent message)
        {
            // MangaDeletedEvent.Manga is SINGULAR (verified at MangaDeletedEvent.cs:11),
            // unlike TV SeriesDeletedEvent.Series which is a list. Wrap as single-element
            // list for the repo accessor (Plan 09-09 IMangaPendingReleaseRepository.DeleteByMangaIds).
            _repository.DeleteByMangaIds(new List<int> { message.Manga.Id });
            UpdatePendingReleases();
            _eventAggregator.PublishEvent(new MangaPendingReleasesUpdatedEvent());
        }

        public void Handle(ChapterGrabbedEvent message)
        {
            // ChapterGrabbedEvent.RemoteChapter (verified at ChapterGrabbedEvent.cs:13) — TV's
            // EpisodeGrabbedEvent.Episode equivalent property. Per Phase 6 D-21, manga is
            // 1-CBZ-per-chapter so the RemoteChapter.Chapters list typically holds one element
            // (one-shots / collected releases may carry more — RemoveGrabbed handles either).
            RemoveGrabbed(message.RemoteChapter);
            UpdatePendingReleases();
        }

        public void Handle(MangaRssSyncCompleteEvent message)
        {
            // Per Plan 09-12 audit gap-01 close-out: payload is a flat List<MangaDownloadDecision>
            // (NOT TV ProcessedDecisions tri-split). Filter via the .Rejected derived property
            // on MangaDownloadDecision to recover the rejected slice. RemoveRejected helper takes
            // the rejected list per the existing Plan 09-10 contract.
            var rejected = message.ProcessedDecisions.Where(d => d.Rejected).ToList();
            RemoveRejected(rejected);
            UpdatePendingReleases();
        }

        public void Handle(CustomFormatProfileUpdatedEvent message)
        {
            UpdatePendingReleases();
        }

        public void Handle(TranslationProfileUpdatedEvent message)
        {
            // 9th IHandle per Open Q §1 acceptance — symmetric treatment with
            // CustomFormatProfileUpdatedEvent. Translation-profile changes can flip the
            // language-acceptance gate on a pending release; rebuild the projection so
            // GetPending / GetPendingQueue reflect the new state.
            UpdatePendingReleases();
        }

        public void Handle(ConfigSavedEvent message)
        {
            UpdatePendingReleases();
        }

        public void Handle(ApplicationStartedEvent message)
        {
            UpdatePendingReleases();
        }

        // ============================================================================
        // Helpers — UpdatePendingReleases skeleton implemented; the 6 body-helpers below
        // are stubbed for Task 2a and filled in Task 2b.
        // ============================================================================

        private void UpdatePendingReleases()
        {
            // Mirror TV PendingReleaseService.cs:648 — atomic-by-reference assignment to the
            // static field (no lock per PATTERNS B). Future readers see the new list atomically.
            _pendingReleases = IncludeRemoteChapters(_repository.All().ToList());
        }

        private List<MangaPendingRelease> IncludeRemoteChapters(List<MangaPendingRelease> rows, Dictionary<string, RemoteChapter> knownRemoteChapters = null)
        {
            // Body filled in Task 2b — populates the not-persisted .RemoteChapter from
            // ParsedChapterInfo via _parsingService + _aggregationService.
            return rows;
        }

        private void RemoveGrabbed(RemoteChapter remoteChapter)
        {
            // Body filled in Task 2b — find pending releases whose chapter ids intersect
            // the grabbed RemoteChapter.Chapters and Delete each.
        }

        private void RemoveRejected(List<MangaDownloadDecision> rejected)
        {
            // Body filled in Task 2b — find pending releases whose release matches a
            // rejected decision (release-title + publish-date + indexer triple) and Delete each.
        }

        private MangaQueueItem MapToQueueItem(MangaPendingRelease pendingRelease, Lazy<DateTime> nextRssSync, List<NzbDrone.Core.Manga.Chapter> chapters)
        {
            // Body filled in Task 2b — projects a MangaPendingRelease row into a MangaQueueItem
            // for the GetPendingQueue path. Mirrors TV GetQueueItem(PendingRelease, Lazy, List<Episode>).
            return null;
        }

        private int GetQueueId(MangaPendingRelease pendingRelease)
        {
            // Body filled in Task 2b — HashConverter.GetHashInt31("pending-{id}") so SignalR
            // diff keys are stable across refreshes.
            return 0;
        }

        // ============================================================================
        // GetDelay — KNOWN LIMITATION per Open Q §3 / Pitfall 5
        // ============================================================================
        private int GetDelay(RemoteChapter remoteChapter)
        {
            var delayProfile = _delayProfileService.AllForTags(remoteChapter.Manga.Tags).OrderBy(d => d.Order).First();

            // KNOWN LIMITATION: DelayProfile.GetProtocolDelay(DownloadProtocol.Http) returns
            // UsenetDelay (DelayProfile.cs:25-28 — Http falls into the else branch since it's
            // neither Usenet nor Torrent). Manga delay-profile cooldown silently uses the
            // Usenet number, which most v1 users will leave at 0. Pending releases may never
            // advance from cooldown if user expects a non-zero Http delay.
            // TODO v1.1: extend DelayProfile with explicit HttpDelay column. Deferred per Open Q §3.
            // Logged in Plan 09-11 close-out to 08/deferred-items.md as "MangaDelayProfile v1.1 follow-up".
            var delay = delayProfile.GetProtocolDelay(DownloadProtocol.Http);

            var minimumAge = _configService.MinimumAge;
            return new[] { delay, minimumAge }.Max();
        }
    }
}
