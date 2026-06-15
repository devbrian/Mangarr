using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV AlreadyImportedSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/AlreadyImportedSpecification.cs (99 lines).
    //
    // Faithful Sonarr port (quick-260615): replaces the Phase 5/6 STUB whose body was
    // "any Imported history row → reject". That stub diverged badly from the TV analog and
    // permanently blocked delete→redownload (the chapter showed Missing but every release was
    // rejected "Chapter already imported"). The TV grab-decision spec is a NARROW guard against
    // re-grabbing the SAME release over and over; this port mirrors it for manga:
    //
    //   * Gap 1 — `if (!chapter has current file) continue`. Mirrors TV `!episode.HasFile`.
    //     A chapter whose ChapterFile was deleted has nothing to be "already imported" against,
    //     so the stale Imported history row must NOT block a re-grab. This is the bug fix.
    //   * Gap 3 — grab/import DownloadId pairing. Only the most recent Grabbed row matters, and
    //     it must have actually been Imported under the SAME DownloadId (mirrors TV lines 49-66).
    //   * Gap 2 — "same release" match. Only reject the SAME release that was grabbed AND
    //     imported, so a different/better scan stays grabbable. Manga has no torrent infohash;
    //     release identity is ReleaseInfo.Guid (mirrored onto ChapterHistory.ReleaseGuid at grab
    //     time — ChapterHistoryService.cs:147) with SourceTitle (line 140) as the TV-style title
    //     fallback (TV reject-by-SourceTitle, lines 88-93).
    //
    // DROPS the TV Quality.Equals comparison (analog lines 68-71) — manga has no quality model
    // per Phase 5 D-04. DROPS the CDH-enabled early-accept (TV lines 35-40) — manga's sole
    // download path (GatewayDownloadClient) always processes completed downloads; there is no
    // EnableCompletedDownloadHandling toggle to gate on.
    //
    // BL-01 GUARD (Phase 5 LEARNINGS): queries ChapterHistory.ChapterId via
    // IChapterHistoryService.FindByChapterId — NEVER IHistoryService.FindByEpisodeId
    // (cross-domain ID-collision class — Episode.Id and Chapter.Id come from independent
    // SQLite autoincrement sequences).
    //
    // Implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard preserved); priority =
    // Database (short-circuits before Default-priority specs); type = Permanent.
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
            var release = subject.Release;

            foreach (var chapter in subject.Chapters)
            {
                // Gap 1 (TV `if (!episode.HasFile) continue`): a chapter with no current
                // ChapterFile — e.g. the file was deleted to force a redownload — has nothing to
                // be "already imported" against. Without this guard the stale Imported history
                // row permanently blocks the chapter from ever being re-grabbed.
                if (chapter.ChapterFileId.GetValueOrDefault() == 0)
                {
                    _logger.Trace("Chapter {0} has no current file; skipping already-imported check", chapter.Id);
                    continue;
                }

                // BL-01 GUARD: ChapterHistory.ChapterId, NOT EpisodeHistory.EpisodeId.
                // FindByChapterId returns rows ordered Date-descending, so FirstOrDefault is the
                // most recent matching event (ChapterHistoryRepository.cs:30-35).
                var chapterHistory = _chapterHistoryService.FindByChapterId(chapter.Id);

                // Gap 3: only the most recent grab is relevant, and it must have actually been
                // imported under the SAME DownloadId.
                var lastGrabbed = chapterHistory.FirstOrDefault(h => h.EventType == ChapterHistoryEventType.Grabbed);

                if (lastGrabbed == null)
                {
                    continue;
                }

                // A null/empty DownloadId is NOT a download-session identifier — pairing on it
                // would correlate unrelated legacy/manual history rows (both null) and reintroduce
                // false "already imported" rejections. Require a real id before pairing.
                if (lastGrabbed.DownloadId.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var imported = chapterHistory.FirstOrDefault(h =>
                    h.EventType == ChapterHistoryEventType.Imported &&
                    h.DownloadId == lastGrabbed.DownloadId);

                if (imported == null)
                {
                    continue;
                }

                if (release == null)
                {
                    continue;
                }

                // Gap 2: reject ONLY the same release that was grabbed and imported. The stable
                // manga release identity is the Guid (ReleaseInfo.Guid → ChapterHistory.ReleaseGuid);
                // SourceTitle is a fallback for the TV-shaped case where a Guid is unavailable.
                if (lastGrabbed.ReleaseGuid.IsNotNullOrWhiteSpace() && release.Guid.IsNotNullOrWhiteSpace())
                {
                    // Both sides carry a usable Guid — it is authoritative. A Guid mismatch means a
                    // genuinely DIFFERENT release, so we must NOT fall through to the title check
                    // (a coincidental SourceTitle match would wrongly reject a different/better scan).
                    if (release.Guid.Equals(lastGrabbed.ReleaseGuid, StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.Debug("Chapter {0} has same release guid as a grabbed and imported release", chapter.Id);
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterAlreadyImported,
                            "Has same release guid as a grabbed and imported release");
                    }

                    continue;
                }

                // Guid unavailable on at least one side — fall back to the title (the TV spec's
                // only identity check).
                if (release.Title.IsNotNullOrWhiteSpace() &&
                    lastGrabbed.SourceTitle.IsNotNullOrWhiteSpace() &&
                    release.Title.Equals(lastGrabbed.SourceTitle, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.Debug("Chapter {0} has same release name as a grabbed and imported release", chapter.Id);
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterAlreadyImported,
                        "Has same release name as a grabbed and imported release");
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
