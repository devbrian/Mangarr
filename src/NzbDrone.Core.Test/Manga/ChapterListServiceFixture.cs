// Sonarr divergence: REWRITTEN per Phase 16 STRUCT-05 + D-01 (stale retention) + D-04
// (zero-release Missing rendering) — see DIVERGENCE.md. ChapterListService is split
// into EnsureChapter (canonical Chapter row) + SyncChapterReleases (per-translation
// upsert). The split mirrors Sonarr's Episode + EpisodeFile boundary, with
// ChapterRelease as the manga-domain divergence (multilingual scanlations).
// DROPPED tests: all 7 Phase-2 strategy-1/2/3 synthesis tests + BL-05 fix tests removed.
// All synthetic-row concerns removed (STRUCT-03); all per-language Chapter rows removed
// (STRUCT-01); zero-release Chapter renders as Missing per D-04. See Plan 2 fixture
// history if you need the deleted method names.
//
// ADDED skeleton tests (Plan 16-03 / Wave 2 fills bodies):
//   - EnsureChapter_idempotent_re_run_produces_same_row_count_and_ids
//   - EnsureChapter_creates_canonical_row_with_FirstReleaseDate_from_inputs
//   - SyncChapterReleases_inserts_new_natural_keys
//   - SyncChapterReleases_keeps_missing_rows                       (D-01)
//   - SyncChapterReleases_does_not_publish_event                   (Pitfall 4)
using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 fixture for the post-Phase-16 split ChapterListService API
    // (EnsureChapter + SyncChapterReleases). Plan 16-03 lands the production-side split,
    // at which point the [Ignore] markers on individual tests are removed.
    [TestFixture]
    public class ChapterListServiceFixture
    {
        [Test]
        [Ignore("Wave 2 dependency: EnsureChapter API lands in Plan 16-03")]
        public void EnsureChapter_idempotent_re_run_produces_same_row_count_and_ids()
        {
            // STRUCT-05 idempotency contract: two consecutive EnsureChapter calls with
            // identical input produce the same row count and the same row Ids (no churn).
            Assert.Inconclusive("Wave 2");
        }

        [Test]
        [Ignore("Wave 2 dependency: EnsureChapter reads FirstReleaseDate from inputs per D-02")]
        public void EnsureChapter_creates_canonical_row_with_FirstReleaseDate_from_inputs()
        {
            // D-02: EnsureChapter populates Chapter.FirstReleaseDate from the upstream
            // metadata source's chapter.publishedAt — independent of any specific
            // translation's upload time.
            Assert.Inconclusive("Wave 2");
        }

        [Test]
        [Ignore("Wave 2 dependency: SyncChapterReleases upsert-on-natural-key per D-01")]
        public void SyncChapterReleases_inserts_new_natural_keys()
        {
            // STRUCT-05: SyncChapterReleases is upsert-on-natural-key
            // (ChapterId, TranslatedLanguage, ScanlationGroup). New keys → INSERT.
            Assert.Inconclusive("Wave 2");
        }

        [Test]
        [Ignore("Wave 2 dependency: D-01 stale-release retention — no DELETE-missing branch")]
        public void SyncChapterReleases_keeps_missing_rows()
        {
            // D-01: stale ChapterRelease rows STAY when the upstream feed no longer
            // lists them. Mirrors Sonarr's Episode handling — never DELETE missing.
            Assert.Inconclusive("Wave 2");
        }

        [Test]
        [Ignore("Wave 2 dependency: Pitfall 4 — single-event invariant lives in RefreshMangaService, not ChapterListService")]
        public void SyncChapterReleases_does_not_publish_event()
        {
            // Pitfall 4: only RefreshMangaService publishes the post-sync event;
            // SyncChapterReleases must NOT publish ChapterListUpdatedEvent itself.
            Assert.Inconclusive("Wave 2");
        }
    }
}
