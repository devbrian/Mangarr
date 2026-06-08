using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV QueueSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/QueueSpecification.cs (115 lines).
    //
    // Phase 6 D-20 STUB body replacement — wires the Phase 5 Accept-always STUB to the new
    // IMangaQueueService introduced in Plan 06-05. Iterates the in-flight projection and
    // rejects when any queued item shares a Chapter.Id with the subject's Chapters.
    //
    // SIMPLIFIED vs TV — no upgrade arithmetic (manga has no quality model per Phase 5
    // D-04). Phase 6 D-08 upgrade-on-better-release is enforced separately by the
    // TranslationProfile + CustomFormat scoring path; this spec is a pure dedup gate.
    //
    // The class still implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard
    // preserved); priority = Default (matches TV QueueSpecification); type = Permanent.
    // Class shape and interface implementation are unchanged from the Phase 5 STUB —
    // only the ctor adds an IMangaQueueService dependency. The 11-spec auto-discovery
    // count (F-01 fixture Be(11)) remains stable.
    //
    // PITFALL 6 GUARD: AutoMoqer-style fixtures consuming this spec MUST register
    // IMangaQueueService or the spec drops from IEnumerable<IMangaDecisionEngineSpecification>
    // resolution and the count fails 11→10. The Wave 5 F-01 fixture (Plan 06-12) covers this;
    // local fixtures in this plan inject the mock directly.
    //
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class QueueDuplicateSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IMangaQueueService _mangaQueueService;
        private readonly Logger _logger;

        public QueueDuplicateSpecification(IMangaQueueService mangaQueueService, Logger logger)
        {
            _mangaQueueService = mangaQueueService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // Phase 6 D-20 STUB body replacement — wires Phase 5 STUB.
            var queue = _mangaQueueService.GetMangaQueue();

            // Sonarr QueueSpecification FailedPending skip (debug auto-retry-one-release-exhaust):
            // a failed/failing download must NOT block its own auto-retry replacement. Sonarr skips
            // ONLY FailedPending because its DownloadEventHub removes Failed items from the queue.
            // Mangarr's RemoveFailedDownloads sweep (MangaDownloadProcessingService) does the same, BUT
            // (a) there is a transient window before the sweep runs and (b) #301 keeps Failed rows sticky
            // across polls AND the gateway RemoveItem is best-effort — so a Failed row can linger if its
            // removal throws. We therefore skip BOTH FailedPending and Failed (the manga-shaped widening
            // of Sonarr's FailedPending-only skip). Without this the re-search rejects every replacement
            // with ChapterAlreadyQueued against the dead row.
            var queuedChapterIds = queue
                .Where(q => !IsFailedOrFailing(q))
                .SelectMany(q => q.RemoteChapter?.Chapters?.Select(c => c.Id) ?? Enumerable.Empty<int>())
                .ToHashSet();

            if (queuedChapterIds.Count == 0)
            {
                return DownloadSpecDecision.Accept();
            }

            var subjectIds = subject.Chapters.Select(c => c.Id).ToHashSet();

            if (subjectIds.Overlaps(queuedChapterIds))
            {
                _logger.Debug("{0} is already in queue, rejecting", subject.Release.Title);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterAlreadyQueued,
                    "Chapter already in queue");
            }

            return DownloadSpecDecision.Accept();
        }

        // MangaQueueItem.TrackedDownloadState is td.State.ToString() (PascalCase enum name); parse it
        // back rather than string-matching so a rename of the enum can't silently break the guard.
        private static bool IsFailedOrFailing(MangaQueueItem item)
        {
            return Enum.TryParse<TrackedDownloadState>(item.TrackedDownloadState, ignoreCase: true, out var state)
                   && (state == TrackedDownloadState.FailedPending || state == TrackedDownloadState.Failed);
        }
    }
}
