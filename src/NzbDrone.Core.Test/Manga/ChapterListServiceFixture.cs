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
// Plan 16-03 lands the production-side EnsureChapter+SyncChapterReleases split, at
// which point the [Ignore] markers from Plan 16-01 were removed and these tests went live.
using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 fixture for the post-Phase-16 split ChapterListService API
    // (EnsureChapter + SyncChapterReleases). Bodied in Plan 16-03.
    [TestFixture]
    public class ChapterListServiceFixture : CoreTest<ChapterListService>
    {
        [Test]
        public void EnsureChapter_idempotent_re_run_produces_same_row_count_and_ids()
        {
            // STRUCT-05 idempotency contract: two consecutive EnsureChapter calls with
            // identical input produce the same row count and the same row Ids (no churn).

            // First call: Find returns null → INSERT.
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(1, 5m))
                .Returns((Chapter)null);

            Chapter inserted = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Insert(It.IsAny<Chapter>()))
                .Callback<Chapter>(c =>
                {
                    c.Id = 42;
                    inserted = c;
                });

            var inputs = new ChapterEnsureInputs("Ch5", null, null, ChapterType.Regular, DateTime.UtcNow, "ext-5");
            var c1 = Subject.EnsureChapter(1, 5m, inputs);
            c1.Id.Should().Be(42);

            // Second call: Find returns the inserted row → UPDATE, NOT Insert.
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(1, 5m))
                .Returns(inserted);

            var c2 = Subject.EnsureChapter(1, 5m, inputs);
            c2.Id.Should().Be(42);

            Mocker.GetMock<IChapterRepository>().Verify(r => r.Insert(It.IsAny<Chapter>()), Times.Once);
            Mocker.GetMock<IChapterRepository>().Verify(r => r.Update(It.IsAny<Chapter>()), Times.AtLeastOnce);
        }

        [Test]
        public void EnsureChapter_creates_canonical_row_with_FirstReleaseDate_from_inputs()
        {
            // D-02: EnsureChapter populates Chapter.FirstReleaseDate from the upstream
            // metadata source's chapter.publishedAt — independent of any specific
            // translation's upload time.
            var when = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(1, 5m))
                .Returns((Chapter)null);

            Chapter captured = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Insert(It.IsAny<Chapter>()))
                .Callback<Chapter>(c =>
                {
                    c.Id = 1;
                    captured = c;
                });

            Subject.EnsureChapter(1, 5m, new ChapterEnsureInputs("Ch5", null, null, ChapterType.Regular, when, "ext-5"));

            captured.Should().NotBeNull();
            captured.FirstReleaseDate.Should().Be(when);
        }

        [Test]
        public void SyncChapterReleases_inserts_new_natural_keys()
        {
            // STRUCT-05: SyncChapterReleases is upsert-on-natural-key
            // (ChapterId, TranslatedLanguage, ScanlationGroup). New keys → INSERT.
            Mocker.GetMock<IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterId(7))
                .Returns(new List<ChapterRelease>());

            Subject.SyncChapterReleases(7, new List<ChapterReleaseFeedRow>
            {
                new("en", "MangaPlus", DateTime.UtcNow, "ext-en"),
                new("es", "MangaPlus", DateTime.UtcNow, "ext-es"),
            });

            Mocker.GetMock<IChapterReleaseRepository>()
                .Verify(r => r.InsertMany(It.Is<List<ChapterRelease>>(list => list.Count == 2)), Times.Once);
        }

        [Test]
        public void SyncChapterReleases_keeps_missing_rows()
        {
            // D-01: stale ChapterRelease rows STAY when the upstream feed no longer
            // lists them. Mirrors Sonarr's Episode handling — never DELETE missing.
            // Existing rows: en + es. New feed: only en. Expect: NO DELETE — es row stays.
            var existing = new List<ChapterRelease>
            {
                new() { Id = 1, ChapterId = 7, TranslatedLanguage = "en", ScanlationGroup = "MangaPlus" },
                new() { Id = 2, ChapterId = 7, TranslatedLanguage = "es", ScanlationGroup = "MangaPlus" },
            };
            Mocker.GetMock<IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterId(7))
                .Returns(existing);

            Subject.SyncChapterReleases(7, new List<ChapterReleaseFeedRow>
            {
                new("en", "MangaPlus", DateTime.UtcNow, "ext-en"),
            });

            // D-01 contract: NEVER DELETE on stale.
            Mocker.GetMock<IChapterReleaseRepository>()
                .Verify(r => r.Delete(It.IsAny<ChapterRelease>()), Times.Never);
            Mocker.GetMock<IChapterReleaseRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<List<ChapterRelease>>()), Times.Never);
            Mocker.GetMock<IChapterReleaseRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<System.Collections.Generic.IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void SyncChapterReleases_does_not_publish_event()
        {
            // Pitfall 4: only RefreshMangaService publishes the post-sync event;
            // SyncChapterReleases must NOT publish ChapterListUpdatedEvent itself.
            Mocker.GetMock<IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterId(7))
                .Returns(new List<ChapterRelease>());

            Subject.SyncChapterReleases(7, new List<ChapterReleaseFeedRow>
            {
                new("en", "MangaPlus", DateTime.UtcNow, "ext-en"),
            });

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()), Times.Never);
        }
    }
}
