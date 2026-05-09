using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: REWRITTEN per Phase 16 STRUCT-05 — see DIVERGENCE.md.
    // Pre-Phase-16 SyncChapters body had 3 strategies (MangaDex-linked, count-based synthesis, empty)
    // with a BL-05 in-place synthetic-upgrade loop. Post-Phase-16 the synthetic concept is gone
    // (STRUCT-03), zero-release Chapters render as Missing (D-04), and stale ChapterReleases are
    // retained on re-sync (D-01). The new two-method split mirrors Sonarr's RefreshEpisodeService
    // two-pass: canonical row upsert + per-release upsert. Single ChapterListUpdatedEvent emit
    // moves to the orchestrating caller (RefreshMangaService — Plan 16-03 Task 2) per Pitfall 4.
    //
    // Idempotency proof:
    //   (a) EnsureChapter upserts on (MangaId, ChapterNumber) — UNIQUE index forbids dups.
    //   (b) SyncChapterReleases upserts on (ChapterId, TranslatedLanguage, ScanlationGroup) — UNIQUE forbids dups.
    //   (c) No DELETE branch — input set never shrinks.
    //   (d) Re-running RefreshMangaCommand produces identical (Chapter, ChapterRelease) state.
    //   (e) Failure mode: any algorithmic regression that violates a natural key fails LOUD via
    //       UNIQUE-violation thrown at INSERT time (caught by Plan 16-02 acceptance tests).
    public sealed class ChapterListService : IChapterListService
    {
        private readonly IChapterRepository _chapterRepo;
        private readonly IChapterReleaseRepository _releaseRepo;
        private readonly Logger _logger;

        public ChapterListService(IChapterRepository chapterRepo,
                                  IChapterReleaseRepository releaseRepo,
                                  Logger logger)
        {
            _chapterRepo = chapterRepo;
            _releaseRepo = releaseRepo;
            _logger = logger;
        }

        public Chapter EnsureChapter(int mangaId, decimal chapterNumber, ChapterEnsureInputs inputs)
        {
            // D-04: always creates a real canonical Chapter row.
            // Idempotent on (MangaId, ChapterNumber) — UNIQUE index from Plan 16-02 enforces.
            var existing = _chapterRepo.Find(mangaId, chapterNumber);
            if (existing == null)
            {
                var chapter = new Chapter
                {
                    MangaId = mangaId,
                    ChapterNumber = chapterNumber,
                    AbsoluteChapterNumber = inputs.AbsoluteChapterNumber,
                    VolumeNumber = inputs.VolumeNumber,
                    ChapterType = inputs.ChapterType,
                    Title = inputs.Title,
                    FirstReleaseDate = inputs.FirstReleaseDate,
                    ExternalId = inputs.ExternalId,
                    Monitored = true,
                };
                _chapterRepo.Insert(chapter);
                return chapter;
            }

            // Update mutable fields (last-write-wins on canonical fields per Pitfall 3 expectation).
            // Null-coalesce so a missing input field does NOT clobber a previously-set canonical value.
            existing.Title = inputs.Title ?? existing.Title;
            existing.AbsoluteChapterNumber = inputs.AbsoluteChapterNumber ?? existing.AbsoluteChapterNumber;
            existing.VolumeNumber = inputs.VolumeNumber ?? existing.VolumeNumber;
            existing.ChapterType = inputs.ChapterType;
            existing.FirstReleaseDate = inputs.FirstReleaseDate ?? existing.FirstReleaseDate;
            existing.ExternalId = inputs.ExternalId ?? existing.ExternalId;
            _chapterRepo.Update(existing);
            return existing;
        }

        public void SyncChapterReleases(int chapterId, IList<ChapterReleaseFeedRow> feedRows)
        {
            // D-01: upsert-on-natural-key, NEVER DELETE missing. Sonarr-mirror of Episode retention.
            // The natural key is (ChapterId, TranslatedLanguage, ScanlationGroup) — UNIQUE index from
            // Plan 16-02 enforces. No DELETE branch — input set never shrinks.
            // Pitfall 4: this method does NOT publish ChapterListUpdatedEvent — caller does.
            if (feedRows == null || feedRows.Count == 0)
            {
                return;
            }

            var existing = _releaseRepo.GetByChapterId(chapterId);

            // Index existing rows by natural key for O(1) lookups (avoids N+1 inside the loop).
            var existingByKey = existing
                .ToDictionary(r => (r.TranslatedLanguage, r.ScanlationGroup));

            var inserts = new List<ChapterRelease>();
            var updates = new List<ChapterRelease>();

            foreach (var row in feedRows)
            {
                var key = (row.TranslatedLanguage, row.ScanlationGroup);
                if (existingByKey.TryGetValue(key, out var match))
                {
                    // Update mutable fields. Null-coalesce so a missing field does NOT clobber.
                    match.ReleaseDate = row.ReleaseDate ?? match.ReleaseDate;
                    match.ExternalId = row.ExternalId ?? match.ExternalId;
                    updates.Add(match);
                }
                else
                {
                    inserts.Add(new ChapterRelease
                    {
                        ChapterId = chapterId,
                        TranslatedLanguage = row.TranslatedLanguage,
                        ScanlationGroup = row.ScanlationGroup,
                        ReleaseDate = row.ReleaseDate,
                        ExternalId = row.ExternalId,
                    });
                }
            }

            if (inserts.Count > 0)
            {
                _releaseRepo.InsertMany(inserts);
            }

            if (updates.Count > 0)
            {
                _releaseRepo.UpdateMany(updates);
            }

            // NO DELETE branch — D-01 stale retention contract.
        }
    }
}
