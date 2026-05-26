using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests.Manga
{
    // Phase 6 D-21 — ChapterHistory repository round-trip + BL-01 cross-domain isolation regression.
    //
    // BL-01 GUARD: ChapterHistory.ChapterId is INDEPENDENT of EpisodeHistory.EpisodeId. The two
    // tables live in different SQLite tables registered separately in TableMapping.cs — Dapper
    // Query<ChapterHistory> cannot hydrate from the History (TV) table even when both tables
    // contain rows whose foreign-key int columns happen to share a value.
    [TestFixture]
    public class ChapterHistoryRepositoryFixture : DbTest<ChapterHistoryRepository, ChapterHistory>
    {
        [Test]
        public void should_round_trip_chapter_history_with_independent_chapter_id()
        {
            // Use ctor (not Builder) so Data dictionary is initialized to empty per the
            // ChapterHistory() constructor. NBuilder property reflection bypasses ctor init.
            var history = new ChapterHistory
            {
                MangaId = 7,
                ChapterId = 42,
                EventType = ChapterHistoryEventType.Imported,
                Date = System.DateTime.UtcNow,
                SourceTitle = "Test Manga - Chapter 042 [English]",
                DownloadId = "dl-abc",
                TranslatedLanguage = "en",
                ScanlationGroup = "ScanGroup",
                SourceKey = "MangaDex",
                ReleaseGuid = "guid-1",
                Successful = true
            };

            history.Data.Add("DownloadClient", "InProcess");
            history.Data.Add("Indexer", "MangaDex");

            Subject.Insert(history);

            StoredModel.MangaId.Should().Be(7);
            StoredModel.ChapterId.Should().Be(42);
            StoredModel.EventType.Should().Be(ChapterHistoryEventType.Imported);
            StoredModel.TranslatedLanguage.Should().Be("en");
            StoredModel.ScanlationGroup.Should().Be("ScanGroup");
            StoredModel.SourceKey.Should().Be("MangaDex");
            StoredModel.ReleaseGuid.Should().Be("guid-1");
            StoredModel.Successful.Should().BeTrue();
            StoredModel.Data.Should().NotBeNull();
            StoredModel.Data.Should().HaveCount(2);

            // Keys are camelCased on round-trip via EmbeddedDocumentConverter (JSON serialization
            // of Dictionary<string,string>). Same behavior as TV EpisodeHistory.Data — see
            // const fields on EpisodeHistory.cs (DOWNLOAD_CLIENT = "downloadClient" lowercased).
            StoredModel.Data["downloadClient"].Should().Be("InProcess");
            StoredModel.Data["indexer"].Should().Be("MangaDex");
        }

        // Issue #270 (Migration 008): ChapterHistory.Date is a UTC-instant column swept to
        // timestamptz on Postgres. Round-trip must be bit-for-bit on both backends.
        [Test]
        public void should_round_trip_Date_as_utc()
        {
            var when = new System.DateTime(2026, 4, 20, 14, 5, 0, System.DateTimeKind.Utc);
            var history = new ChapterHistory
            {
                MangaId = 3,
                ChapterId = 9,
                EventType = ChapterHistoryEventType.Grabbed,
                Date = when,
                SourceTitle = "Test Manga - Chapter 009",
            };

            Subject.Insert(history);

            StoredModel.Date.Should().Be(when);
        }

        [Test]
        public void FindByChapterId_returns_only_rows_for_that_chapter()
        {
            var matching = Builder<ChapterHistory>.CreateNew()
                .With(h => h.MangaId = 1)
                .With(h => h.ChapterId = 100)
                .With(h => h.EventType = ChapterHistoryEventType.Grabbed)
                .BuildNew();
            var otherChapter = Builder<ChapterHistory>.CreateNew()
                .With(h => h.MangaId = 1)
                .With(h => h.ChapterId = 101)
                .With(h => h.EventType = ChapterHistoryEventType.Grabbed)
                .BuildNew();

            Subject.Insert(matching);
            Subject.Insert(otherChapter);

            var result = Subject.FindByChapterId(100);

            result.Should().HaveCount(1);
            result.Single().ChapterId.Should().Be(100);
        }

        [Test]
        public void DeleteForManga_cascades_only_for_specified_manga()
        {
            var keep = Builder<ChapterHistory>.CreateNew()
                .With(h => h.MangaId = 1)
                .With(h => h.ChapterId = 10)
                .BuildNew();
            var delete1 = Builder<ChapterHistory>.CreateNew()
                .With(h => h.MangaId = 2)
                .With(h => h.ChapterId = 20)
                .BuildNew();
            var delete2 = Builder<ChapterHistory>.CreateNew()
                .With(h => h.MangaId = 2)
                .With(h => h.ChapterId = 21)
                .BuildNew();

            Subject.Insert(keep);
            Subject.Insert(delete1);
            Subject.Insert(delete2);

            Subject.DeleteForManga(2);

            Subject.All().Should().HaveCount(1);
            Subject.All().Single().MangaId.Should().Be(1);
        }
    }
}
