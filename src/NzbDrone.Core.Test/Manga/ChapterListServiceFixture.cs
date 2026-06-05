// Phase 16.1 Wave 3 (REVERT-03): single-pass SyncChapters fixture. Reverted by
// behavior (NOT git restore per CONTEXT.md D-12) from the Phase 16 two-pass
// fixture assertions. Tests cover the upsert pattern (Insert + Update; NO DELETE —
// locked stale-handling decision per Phase 16.1 CONTEXT.md additional_context
// Pitfall #4 + PATTERNS.md Pitfall 6) plus the (MangaId, ChapterNumber) DistinctBy
// idempotency safety net.
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
    [TestFixture]
    public class ChapterListServiceFixture : CoreTest<ChapterListService>
    {
        private Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new Manga.Manga { Id = 1, Title = "M" };

            // Default: empty existing-chapters set.
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter>());
        }

        [Test]
        public void SyncChapters_inserts_new_canonical_rows_for_missing_chapter_numbers()
        {
            // No existing rows + 2 remote chapters → 2 inserts via InsertMany.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 1m, Title = "Ch1", Monitored = true, ChapterType = ChapterType.Regular },
                new() { ChapterNumber = 2m, Title = "Ch2", Monitored = true, ChapterType = ChapterType.Regular },
            };

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.InsertMany(It.Is<IList<Chapter>>(list => list.Count == 2)), Times.Once);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.UpdateMany(It.IsAny<IList<Chapter>>()), Times.Never);
        }

        [Test]
        public void SyncChapters_assigns_MangaId_on_inserted_rows()
        {
            // Inserted Chapter must carry the manga.Id from the manga argument.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 5m, Title = "Ch5", Monitored = true, ChapterType = ChapterType.Regular },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.InsertMany(It.IsAny<IList<Chapter>>()))
                .Callback<IList<Chapter>>(list => captured = list);

            Subject.SyncChapters(_manga, remote);

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1);
            captured[0].MangaId.Should().Be(1);
        }

        [Test]
        public void SyncChapters_updates_existing_canonical_rows_in_place()
        {
            // Existing chapter → UPDATE not INSERT. Mutable fields copied with null-coalesce.
            var existing = new Chapter
            {
                Id = 42,
                MangaId = 1,
                ChapterNumber = 5m,
                Title = "Original Title",
                Monitored = true,
                ChapterType = ChapterType.Regular,
            };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter> { existing });

            var when = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 5m, Title = "Updated Title", FirstReleaseDate = when, ChapterType = ChapterType.Regular },
            };

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.InsertMany(It.IsAny<IList<Chapter>>()), Times.Never);
            Mocker.GetMock<IChapterRepository>()
                .Verify(
                    r => r.UpdateMany(It.Is<IList<Chapter>>(list =>
                        list.Count == 1 &&
                        list[0].Id == 42 &&
                        list[0].Title == "Updated Title" &&
                        list[0].FirstReleaseDate == when)),
                    Times.Once);
        }

        [Test]
        public void SyncChapters_does_not_clobber_structural_fields_when_remote_is_null()
        {
            // Null-coalesce protects against losing data when the metadata source omits a
            // structural field (FirstReleaseDate / VolumeNumber / AbsoluteChapterNumber /
            // ExternalId). The Title field is intentionally excluded from this guard — see
            // SyncChapters_clobbers_existing_Title_with_null_remote below for the rationale.
            var existingWhen = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            var existing = new Chapter
            {
                Id = 42,
                MangaId = 1,
                ChapterNumber = 5m,
                Title = "Original",
                VolumeNumber = 7,
                AbsoluteChapterNumber = 99,
                FirstReleaseDate = existingWhen,
                ExternalId = "existing-external-id",
                ChapterType = ChapterType.Regular,
            };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter> { existing });

            var remote = new List<Chapter>
            {
                // All structural fields null on remote — coalesce must preserve existing values.
                new() { ChapterNumber = 5m, Title = "kept", ChapterType = ChapterType.Regular },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.UpdateMany(It.IsAny<IList<Chapter>>()))
                .Callback<IList<Chapter>>(list => captured = list);

            Subject.SyncChapters(_manga, remote);

            captured.Should().NotBeNull();
            captured[0].VolumeNumber.Should().Be(7);
            captured[0].AbsoluteChapterNumber.Should().Be(99);
            captured[0].FirstReleaseDate.Should().Be(existingWhen);
            captured[0].ExternalId.Should().Be("existing-external-id");
        }

        [Test]
        public void SyncChapters_clobbers_existing_Title_with_null_remote()
        {
            // Bug fix (manga-details-chapter-titles, 2026-05-10): Title is overwrite-with-null
            // on refresh (DIVERGES from the structural-field coalesce behavior immediately above).
            // MangaDexMetadataSource.MapChapter now suppresses non-English Title to null
            // (canonical Episode.Title-mirror — TVDB-English-only). For chapters whose Title was
            // ingested before the fix (e.g. Polish scanlator commentary on Solo Leveling: Ragnarok),
            // the coalesce would preserve the stale polluted value across refreshes. Treating Title
            // as fully remote-driven ensures the next refresh cleans up legacy stale-EN-pollution.
            var existing = new Chapter
            {
                Id = 42,
                MangaId = 1,
                ChapterNumber = 5m,
                Title = "POLISH SCANLATOR COMMENTARY (legacy)",
                ChapterType = ChapterType.Regular,
            };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter> { existing });

            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 5m, Title = null, ChapterType = ChapterType.Regular },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.UpdateMany(It.IsAny<IList<Chapter>>()))
                .Callback<IList<Chapter>>(list => captured = list);

            Subject.SyncChapters(_manga, remote);

            captured.Should().NotBeNull();
            captured[0].Title.Should().BeNull(
                "Title is overwrite-with-null on refresh so stale pre-fix titles get cleaned up");
        }

        [Test]
        public void SyncChapters_preserveExistingOnNull_does_not_clobber_real_Title_with_synthesized_null()
        {
            // Phase 40 WR-02: the synthesis caller passes preserveExistingOnNull: true. A
            // synthesized row carries Title = null ("unknown", NOT canonical). Under the TOCTOU
            // race where a concurrent refresh inserted the real chapter between the synthesis
            // absence-check and this sync, the synthesized null must NOT clobber the real Title
            // (nor the canonical ChapterType). Contrast with the clobber test above (default false).
            var existing = new Chapter
            {
                Id = 42,
                MangaId = 1,
                ChapterNumber = 5m,
                Title = "Real MangaDex Title",
                ChapterType = ChapterType.Special,
            };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter> { existing });

            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 5m, Title = null, ChapterType = ChapterType.Regular },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.UpdateMany(It.IsAny<IList<Chapter>>()))
                .Callback<IList<Chapter>>(list => captured = list);

            Subject.SyncChapters(_manga, remote, preserveExistingOnNull: true);

            captured.Should().NotBeNull();
            captured[0].Title.Should().Be("Real MangaDex Title",
                "a synthesized null Title must not clobber a real one under preserveExistingOnNull");
            captured[0].ChapterType.Should().Be(ChapterType.Special,
                "a synthesized row carries no authoritative ChapterType under preserveExistingOnNull");
        }

        [Test]
        public void SyncChapters_does_not_DELETE_stale_chapters()
        {
            // Locked stale-handling decision (Phase 16.1 PATTERNS.md Pitfall 6 +
            // CONTEXT.md additional_context Pitfall #4): NO DELETE branch in SyncChapters.
            // Existing chapters NOT in the remote feed must not be deleted.
            var existing1 = new Chapter { Id = 41, MangaId = 1, ChapterNumber = 1m, ChapterType = ChapterType.Regular };
            var existing2 = new Chapter { Id = 42, MangaId = 1, ChapterNumber = 2m, ChapterType = ChapterType.Regular };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter> { existing1, existing2 });

            // Remote feed contains only chapter 1; chapter 2 has gone stale.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular },
            };

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Delete(It.IsAny<Chapter>()), Times.Never);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Delete(It.IsAny<int>()), Times.Never);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<IEnumerable<int>>()), Times.Never);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
        }

        [Test]
        public void SyncChapters_dedups_remote_entries_on_canonical_natural_key()
        {
            // BL-05 multi-translation regression safety net: if the upstream feed yields
            // two entries with the same chapter number (e.g., one per translation grain
            // before MangaDexMetadataSource.MapChapters dedup), DistinctBy(ChapterNumber)
            // collapses them BEFORE the upsert pass — only ONE Insert call regardless.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 5m, Title = "First", ChapterType = ChapterType.Regular },
                new() { ChapterNumber = 5m, Title = "Second", ChapterType = ChapterType.Regular },
            };

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.InsertMany(It.Is<IList<Chapter>>(list => list.Count == 1)), Times.Once);
        }

        [Test]
        public void SyncChapters_does_not_publish_event()
        {
            // Pitfall 4: only RefreshMangaService publishes the post-sync ChapterListUpdatedEvent;
            // SyncChapters must NOT publish it itself.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular },
            };

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void SyncChapters_with_null_remote_is_a_noop()
        {
            // Defensive: null IEnumerable<Chapter> from the metadata source must not crash.
            Subject.SyncChapters(_manga, null);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.InsertMany(It.IsAny<IList<Chapter>>()), Times.Never);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.UpdateMany(It.IsAny<IList<Chapter>>()), Times.Never);
        }

        [Test]
        public void SyncChapters_idempotent_re_run_produces_same_db_state()
        {
            // Idempotency contract: identical remote feed across two runs must produce
            // identical DB state (no churn). First call: empty existing → INSERT.
            // Second call: now-existing → UPDATE only.
            var remote = new List<Chapter>
            {
                new() { ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular },
            };

            // Run 1: nothing exists → insert.
            var insertedFirst = false;
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.InsertMany(It.IsAny<IList<Chapter>>()))
                .Callback<IList<Chapter>>(list =>
                {
                    insertedFirst = true;
                    list[0].Id = 7;
                });

            Subject.SyncChapters(_manga, remote);
            insertedFirst.Should().BeTrue();

            // Run 2: existing row matches → update only.
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.GetByMangaId(1))
                .Returns(new List<Chapter>
                {
                    new() { Id = 7, MangaId = 1, ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular },
                });

            Subject.SyncChapters(_manga, remote);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.InsertMany(It.IsAny<IList<Chapter>>()), Times.Once);
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.UpdateMany(It.IsAny<IList<Chapter>>()), Times.AtLeastOnce);
        }
    }
}
