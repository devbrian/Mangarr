using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.DecisionEngine.Manga.Aggregators;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;          // MangaRssSyncCompleteEvent (Plan 09-12) + MangaRssSyncCommand
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
    using Chapter = NzbDrone.Core.Manga.Chapter;
    using IMangaService = NzbDrone.Core.Manga.IMangaService;
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
    // chapter-id-set is preserved; protocol-priority via _delayProfileService.BestForTags
    // is preserved (only Http/Torrent matter on the manga side).
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
        private readonly IMangaService _mangaService;
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
                                          IMangaService mangaService,
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
            _mangaService = mangaService;
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
        // 9 public methods.
        // ============================================================================

        public void Add(MangaDownloadDecision decision, PendingReleaseReason reason)
        {
            // Mirror TV PendingReleaseService.Add (lines 97-100) — single-decision
            // delegation to AddMany.
            AddMany(new List<Tuple<MangaDownloadDecision, PendingReleaseReason>>
            {
                Tuple.Create(decision, reason)
            });
        }

        public void AddMany(List<Tuple<MangaDownloadDecision, PendingReleaseReason>> decisions)
        {
            // Mirror TV PendingReleaseService.AddMany (lines 102-170) — group by manga,
            // dedup vs already-pending (matching by release title + publish-date + indexer
            // triple), Insert any new entries via the Pitfall-4 wrapper, then refresh the
            // static projection at the end.
            foreach (var mangaDecisions in decisions.GroupBy(v => v.Item1.RemoteChapter.Manga.Id))
            {
                var manga = mangaDecisions.First().Item1.RemoteChapter.Manga;
                var alreadyPending = _pendingReleases
                    .Where(p => p.MangaId == manga.Id)
                    .SelectList(s => s.JsonClone());

                alreadyPending = IncludeRemoteChapters(
                    alreadyPending,
                    mangaDecisions.ToDictionaryIgnoreDuplicates(
                        v => v.Item1.RemoteChapter.Release.Title,
                        v => v.Item1.RemoteChapter));

                var alreadyPendingByChapter = CreateChapterLookup(alreadyPending);

                foreach (var pair in mangaDecisions)
                {
                    var decision = pair.Item1;
                    var reason = pair.Item2;

                    var chapterIds = decision.RemoteChapter.Chapters.Select(c => c.Id);

                    var existingReports = chapterIds.SelectMany(v => alreadyPendingByChapter[v])
                                                    .Distinct()
                                                    .ToList();

                    var matchingReports = existingReports
                        .Where(MatchingReleasePredicate(decision.RemoteChapter.Release))
                        .ToList();

                    if (matchingReports.Any())
                    {
                        var matchingReport = matchingReports.First();

                        if (matchingReport.Reason != reason)
                        {
                            if (matchingReport.Reason == PendingReleaseReason.DownloadClientUnavailable)
                            {
                                _logger.Debug(
                                    "The release {0} is already pending with reason {1}, not changing reason",
                                    decision.RemoteChapter,
                                    matchingReport.Reason);
                            }
                            else
                            {
                                _logger.Debug(
                                    "The release {0} is already pending with reason {1}, changing to {2}",
                                    decision.RemoteChapter,
                                    matchingReport.Reason,
                                    reason);
                                matchingReport.Reason = reason;
                                _repository.Update(matchingReport);
                            }
                        }
                        else
                        {
                            _logger.Debug(
                                "The release {0} is already pending with reason {1}, not adding again",
                                decision.RemoteChapter,
                                reason);
                        }

                        if (matchingReports.Count > 1)
                        {
                            _logger.Debug(
                                "The release {0} had {1} duplicate pending, removing duplicates.",
                                decision.RemoteChapter,
                                matchingReports.Count - 1);

                            foreach (var duplicate in matchingReports.Skip(1))
                            {
                                _repository.Delete(duplicate.Id);
                                alreadyPending.Remove(duplicate);
                                alreadyPendingByChapter = CreateChapterLookup(alreadyPending);
                            }
                        }

                        continue;
                    }

                    _logger.Debug(
                        "Adding release {0} to pending releases with reason {1}",
                        decision.RemoteChapter,
                        reason);

                    Insert(decision, reason);
                }
            }

            UpdatePendingReleases();
        }

        public List<ReleaseInfo> GetPending()
        {
            // Mirror TV GetPending (lines 172-189) — read straight from repo (NOT the
            // static cache) so callers picking up new entries inside the same dispatch
            // cycle see them, then filter blocked indexers.
            var releases = _repository.All().Select(p =>
            {
                var release = p.Release;
                release.PendingReleaseReason = p.Reason;
                return release;
            }).ToList();

            if (releases.Any())
            {
                releases = FilterBlockedIndexers(releases);
            }

            return releases;
        }

        public List<RemoteChapter> GetPendingRemoteChapters(int mangaId)
        {
            // Mirror TV GetPendingRemoteEpisodes (lines 191-194) — straight projection
            // of the cached static list.
            return _pendingReleases
                .Where(p => p.MangaId == mangaId)
                .Select(p => p.RemoteChapter)
                .ToList();
        }

        public List<MangaQueueItem> GetPendingQueue()
        {
            // Mirror TV GetPendingQueue (lines 196-229) but drop the QualityModelComparer
            // dedup axis (Phase 5 D-04 — manga has NO QualityProfile). Dedup by chapter-id
            // set + protocol priority is preserved.
            var queued = new List<MangaQueueItem>();
            var nextRssSync = new Lazy<DateTime>(() => _taskManager.GetNextExecution(typeof(MangaRssSyncCommand)));

            // Skip Fallback rows — they live in the table for housekeeping bookkeeping but
            // don't surface in the user-visible queue (TV verbatim).
            var pendingReleases = _pendingReleases
                .Where(p => p.Reason != PendingReleaseReason.Fallback)
                .ToList();

            foreach (var pendingRelease in pendingReleases)
            {
                if (pendingRelease.RemoteChapter == null || pendingRelease.RemoteChapter.Chapters.Empty())
                {
                    var noChapterItem = MapToQueueItem(pendingRelease, nextRssSync, new List<Chapter>());
                    if (noChapterItem != null)
                    {
                        noChapterItem.ErrorMessage = "Unable to find matching chapter(s)";
                        queued.Add(noChapterItem);
                    }

                    continue;
                }

                var item = MapToQueueItem(pendingRelease, nextRssSync, pendingRelease.RemoteChapter.Chapters);
                if (item != null)
                {
                    queued.Add(item);
                }
            }

            // Dedup: for each chapter-id set, keep the lowest-protocol-priority release
            // (TV runs OrderByDescending on QualityModelComparer first; manga drops that
            // and just picks the best protocol). Manga grouping key is the sorted chapter
            // ids — releases that span the same chapters compete with each other.
            var deduped = queued
                .Where(q => q.Chapters != null && q.Chapters.Any())
                .GroupBy(q => string.Join(",", q.Chapters.Select(c => c.Id).OrderBy(id => id)))
                .Select(g => g.OrderBy(q => PrioritizeDownloadProtocol(q.RemoteChapter, q.Protocol)).First());

            return deduped.ToList();
        }

        public MangaQueueItem FindPendingQueueItem(int queueId)
        {
            // Mirror TV FindPendingQueueItem (lines 272-275).
            return GetPendingQueue().SingleOrDefault(p => p.Id == queueId);
        }

        public void RemovePendingQueueItems(int queueId)
        {
            // Mirror TV RemovePendingQueueItems (lines 282-292) but key off chapter-number
            // set instead of (season + episode-number) tuple. Use the Pitfall-4 Delete
            // wrapper so each removal publishes MangaPendingReleasesUpdatedEvent.
            var targetItem = FindPendingRelease(queueId);
            if (targetItem == null)
            {
                return;
            }

            var mangaReleases = _repository.AllByMangaId(targetItem.MangaId);

            var releasesToRemove = mangaReleases.Where(c =>
                c.ParsedChapterInfo != null
                && targetItem.ParsedChapterInfo != null
                && c.ParsedChapterInfo.ChapterNumbers != null
                && targetItem.ParsedChapterInfo.ChapterNumbers != null
                && c.ParsedChapterInfo.ChapterNumbers.SequenceEqual(targetItem.ParsedChapterInfo.ChapterNumbers)
                && c.ParsedChapterInfo.TranslatedLanguage == targetItem.ParsedChapterInfo.TranslatedLanguage)
                .ToList();

            foreach (var release in releasesToRemove)
            {
                Delete(release);
            }
        }

        public RemoteChapter OldestPendingRelease(int mangaId, int[] chapterIds)
        {
            // Mirror TV OldestPendingRelease (lines 306-313) — pick the release with the
            // greatest AgeHours (longest in the queue) whose chapters intersect the
            // requested ids. AgeHours is a positive number (older = larger), so MaxBy is
            // semantically "oldest".
            var mangaReleases = GetPendingReleases(mangaId);

            return mangaReleases
                .Select(r => r.RemoteChapter)
                .Where(r => r != null && r.Chapters.Select(c => c.Id).Intersect(chapterIds).Any())
                .MaxBy(p => p.Release.AgeHours);
        }

        // ============================================================================
        // Pitfall 4 wrappers — Insert / Delete (PATTERNS section A; mirrors TV
        // PendingReleaseService.cs:530-548 VERBATIM ordering: DB write FIRST, event LAST).
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
        // Helpers.
        // ============================================================================

        private void UpdatePendingReleases()
        {
            // Mirror TV PendingReleaseService.cs:648 — atomic-by-reference assignment to the
            // static field (no lock per PATTERNS B). Future readers see the new list atomically.
            _pendingReleases = IncludeRemoteChapters(_repository.All().ToList());
        }

        private List<MangaPendingRelease> IncludeRemoteChapters(List<MangaPendingRelease> releases, Dictionary<string, RemoteChapter> knownRemoteChapters = null)
        {
            // Mirror TV IncludeRemoteEpisodes (lines 339-418) — populate the not-persisted
            // .RemoteChapter from the persisted ParsedChapterInfo via _parsingService +
            // _aggregationService. Pre-loaded knownRemoteChapters short-circuits the
            // per-row parser+repo round-trip when callers (AddMany) already have them.
            var result = new List<MangaPendingRelease>();
            var mangaMap = new Dictionary<int, Manga>();

            if (knownRemoteChapters != null)
            {
                foreach (var manga in knownRemoteChapters.Values.Where(v => v != null && v.Manga != null).Select(v => v.Manga))
                {
                    mangaMap.TryAdd(manga.Id, manga);
                }
            }

            var mangaIdsToFetch = releases
                .Select(v => v.MangaId)
                .Distinct()
                .Where(id => !mangaMap.ContainsKey(id))
                .ToList();

            if (mangaIdsToFetch.Any())
            {
                foreach (var manga in _mangaService.GetManga(mangaIdsToFetch))
                {
                    mangaMap[manga.Id] = manga;
                }
            }

            foreach (var release in releases)
            {
                var manga = mangaMap.GetValueOrDefault(release.MangaId);

                // Just in case the manga was removed but wasn't cleaned up yet (housekeeper
                // catches this); skip the row so the projection does not carry a dangling ref.
                if (manga == null)
                {
                    continue;
                }

                release.RemoteChapter = new RemoteChapter
                {
                    Manga = manga,
                    ParsedChapterInfo = release.ParsedChapterInfo,
                    Release = release.Release,
                };

                if (knownRemoteChapters != null
                    && knownRemoteChapters.TryGetValue(release.Release.Title, out var knownRemoteChapter)
                    && knownRemoteChapter != null)
                {
                    release.RemoteChapter.Chapters = knownRemoteChapter.Chapters;
                }
                else
                {
                    try
                    {
                        var mapped = _parsingService.Map(release.ParsedChapterInfo, manga, existingChapters: null);
                        release.RemoteChapter.Chapters = mapped?.Chapters ?? new List<Chapter>();
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.Debug(ex, ex.Message);
                        release.RemoteChapter.Chapters = new List<Chapter>();
                    }
                }

                _aggregationService.Augment(release.RemoteChapter);

                result.Add(release);
            }

            return result;
        }

        private void RemoveGrabbed(RemoteChapter remoteChapter)
        {
            // Mirror TV RemoveGrabbed (lines 565-595) — find pending releases for the same
            // manga whose chapter ids intersect the grabbed chapter ids and Delete each.
            //
            // Manga simplification: TV uses QualityModelComparer to keep higher-quality
            // pending releases. Manga has no QualityProfile (Phase 5 D-04), so we delete
            // every intersecting pending release without comparison. The CustomFormat score
            // axis is re-evaluated on the next RSS sync if a still-pending release ages
            // out of cooldown — keeping pending releases here would only add noise to
            // GetPendingQueue without changing grab outcome.
            if (remoteChapter == null || remoteChapter.Manga == null)
            {
                return;
            }

            var pendingReleases = GetPendingReleases(remoteChapter.Manga.Id);
            var chapterIds = remoteChapter.Chapters.Select(c => c.Id).ToList();

            var existingReports = pendingReleases
                .Where(r => r.RemoteChapter != null
                            && r.RemoteChapter.Chapters.Select(c => c.Id).Intersect(chapterIds).Any())
                .ToList();

            if (existingReports.Empty())
            {
                return;
            }

            foreach (var existingReport in existingReports)
            {
                _logger.Debug("Removing previously pending release, as it was grabbed.");
                Delete(existingReport);
            }
        }

        private void RemoveRejected(List<MangaDownloadDecision> rejected)
        {
            // Mirror TV RemoveRejected (lines 597-612) — for each newly-rejected release,
            // find pending entries that match by (title + publish-date + indexer) triple
            // and Delete each via the Pitfall-4 wrapper.
            _logger.Debug("Removing failed releases from pending");
            var pending = GetPendingReleases();

            foreach (var rejectedRelease in rejected)
            {
                if (rejectedRelease.RemoteChapter == null || rejectedRelease.RemoteChapter.Release == null)
                {
                    continue;
                }

                var matching = pending
                    .Where(MatchingReleasePredicate(rejectedRelease.RemoteChapter.Release))
                    .ToList();

                foreach (var pendingRelease in matching)
                {
                    _logger.Debug("Removing previously pending release, as it has now been rejected.");
                    Delete(pendingRelease);
                }
            }
        }

        private MangaQueueItem MapToQueueItem(MangaPendingRelease pendingRelease, Lazy<DateTime> nextRssSync, List<Chapter> chapters)
        {
            // Mirror TV GetQueueItem(PendingRelease, Lazy<DateTime>, List<Episode>) at lines 420-471
            // adapted for manga: drop QualityModel + Languages (D-04), add TranslatedLanguage +
            // ScanlationGroup as first-class fields (Phase 3 D-Q4 — already present on MangaQueueItem).
            if (pendingRelease.RemoteChapter == null)
            {
                return null;
            }

            var ect = pendingRelease.Release.PublishDate.AddMinutes(GetDelay(pendingRelease.RemoteChapter));

            if (ect < nextRssSync.Value)
            {
                ect = nextRssSync.Value;
            }
            else
            {
                // Manga uses MangaRssSyncInterval (per IConfigService.cs:122) — TV equivalent
                // is RssSyncInterval; both default 15.
                ect = ect.AddMinutes(_configService.MangaRssSyncInterval);
            }

            var timeLeft = ect.Subtract(DateTime.UtcNow);

            if (timeLeft.TotalSeconds < 0)
            {
                timeLeft = TimeSpan.Zero;
            }

            string downloadClientName = null;
            var indexer = _indexerFactory.Find(pendingRelease.Release.IndexerId);

            if (indexer is { DownloadClientId: > 0 })
            {
                var downloadClient = _downloadClientFactory.Find(indexer.DownloadClientId);
                downloadClientName = downloadClient?.Name;
            }

            var firstChapter = chapters?.FirstOrDefault();

            var queueItem = new MangaQueueItem
            {
                Id = GetQueueId(pendingRelease),
                MangaId = pendingRelease.RemoteChapter.Manga?.Id,
                ChapterId = firstChapter?.Id,
                Manga = pendingRelease.RemoteChapter.Manga,
                Chapter = firstChapter,
                Chapters = chapters?.ToList() ?? new List<Chapter>(),
                RemoteChapter = pendingRelease.RemoteChapter,
                TranslatedLanguage = pendingRelease.RemoteChapter.Release?.TranslatedLanguage,
                ScanlationGroup = pendingRelease.RemoteChapter.Release?.ScanlationGroup,
                Title = pendingRelease.Title,
                Size = pendingRelease.RemoteChapter.Release.Size,
                SizeLeft = pendingRelease.RemoteChapter.Release.Size,
                TimeLeft = timeLeft,
                EstimatedCompletionTime = ect,
                Added = pendingRelease.Added,
                Status = pendingRelease.Reason.ToString(),
                Protocol = pendingRelease.RemoteChapter.Release.DownloadProtocol,
                Indexer = pendingRelease.RemoteChapter.Release.Indexer,
                DownloadClient = downloadClientName,
            };

            return queueItem;
        }

        private int GetQueueId(MangaPendingRelease pendingRelease)
        {
            // Deterministic Id per the SignalR-diff-key contract — same pending release
            // keeps the same Id across UpdatePendingReleases() rebuilds.
            return HashConverter.GetHashInt31(string.Format("manga-pending-{0}", pendingRelease.Id));
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

        // ============================================================================
        // Internal helpers (no public exposure).
        // ============================================================================

        private static Func<MangaPendingRelease, bool> MatchingReleasePredicate(ReleaseInfo release)
        {
            // Mirror TV MatchingReleasePredicate (lines 694-699). Title + PublishDate +
            // Indexer triple uniquely identifies a release across feed rebuilds and is
            // the same shape Phase 6 history/blocklist already uses.
            return p => p.Title == release.Title
                        && p.Release != null
                        && p.Release.PublishDate == release.PublishDate
                        && p.Release.Indexer == release.Indexer;
        }

        private ILookup<int, MangaPendingRelease> CreateChapterLookup(IEnumerable<MangaPendingRelease> alreadyPending)
        {
            // Mirror TV CreateEpisodeLookup (lines 315-320). Flatten into a chapter-id ->
            // pending-release lookup so AddMany can find existing reports per chapter.
            return alreadyPending
                .Where(v => v.RemoteChapter != null)
                .SelectMany(v => v.RemoteChapter.Chapters.Select(d => new { Chapter = d, PendingRelease = v }))
                .ToLookup(v => v.Chapter.Id, v => v.PendingRelease);
        }

        private List<ReleaseInfo> FilterBlockedIndexers(List<ReleaseInfo> releases)
        {
            // Mirror TV FilterBlockedIndexers (lines 322-327). Drop releases whose source
            // indexer is currently blocked (back-off / bad-credentials state).
            var blockedIndexers = new HashSet<int>(_indexerStatusService.GetBlockedProviders().Select(v => v.ProviderId));
            return releases.Where(release => !blockedIndexers.Contains(release.IndexerId)).ToList();
        }

        private List<MangaPendingRelease> GetPendingReleases()
        {
            return _pendingReleases;
        }

        private List<MangaPendingRelease> GetPendingReleases(int mangaId)
        {
            return _pendingReleases.Where(p => p.MangaId == mangaId).ToList();
        }

        private MangaPendingRelease FindPendingRelease(int queueId)
        {
            // Mirror TV FindPendingRelease (lines 614-617). Match the deterministic queue
            // id back to its source row so RemovePendingQueueItems can find the chapter
            // tuple to delete by.
            return GetPendingReleases().FirstOrDefault(p => GetQueueId(p) == queueId);
        }

        private int PrioritizeDownloadProtocol(RemoteChapter remoteChapter, DownloadProtocol downloadProtocol)
        {
            // Mirror TV PrioritizeDownloadProtocol (lines 634-643). User-preferred protocol
            // returns 0 (sort first); others return 1.
            if (remoteChapter == null || remoteChapter.Manga == null)
            {
                return 1;
            }

            var delayProfile = _delayProfileService.BestForTags(remoteChapter.Manga.Tags);
            return downloadProtocol == delayProfile.PreferredProtocol ? 0 : 1;
        }
    }
}
