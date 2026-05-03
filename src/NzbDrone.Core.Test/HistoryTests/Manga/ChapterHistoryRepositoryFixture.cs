using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.History;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Qualities;
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
        public void FindByChapterId_does_NOT_cross_query_EpisodeHistory_table_BL01_regression()
        {
            // BL-01 cross-domain ID-collision regression. Seed an EpisodeHistory row whose
            // EpisodeId == 42 (the value we'll query against ChapterHistory.ChapterId). If the
            // repository erroneously queried the History (TV) table, we'd get back the TV row.
            // The two tables are registered separately in TableMapping — Dapper cannot leak
            // EpisodeHistory rows into Query<ChapterHistory>.
            var tvDb = Mocker.Resolve<NzbDrone.Core.Datastore.IMainDatabase>();
            var episodeHistory = Builder<EpisodeHistory>.CreateNew()
                .With(h => h.EpisodeId = 42)
                .With(h => h.SeriesId = 999)
                .With(h => h.Languages = new List<Language> { Language.English })
                .With(h => h.Quality = new QualityModel())
                .BuildNew();
            new NzbDrone.Core.History.HistoryRepository(tvDb, Mocker.Resolve<NzbDrone.Core.Messaging.Events.IEventAggregator>())
                .Insert(episodeHistory);

            // ChapterHistory has NO row with ChapterId=42.
            var result = Subject.FindByChapterId(42);

            result.Should().BeEmpty(
                "ChapterHistory.ChapterId is independent of EpisodeHistory.EpisodeId per BL-01 fix; "
                + "Dapper Query<ChapterHistory> cannot return rows from the History (TV) table.");
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
