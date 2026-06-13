using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Download.History.Manga;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.History.Manga
{
    // Phase 36 Plan 36-01 (LOOP-02) — contract tests for the LEAN DownloadId-keyed matching join
    // writer. DISTINCT from ChapterHistoryServiceFixture (the user-facing surface). Covers the five
    // locked behaviors: Insert-FIRST Grabbed row, multi-chapter ChapterIds, GetLatestGrab hit + miss,
    // DownloadImported row on ChapterDownloadCompletedEvent, and JSON round-trip for ChapterIds + Data.
    [TestFixture]
    public class MangaDownloadHistoryServiceFixture : CoreTest<MangaDownloadHistoryService>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" };
        }

        private static RemoteChapter BuildRemote(NzbDrone.Core.Manga.Manga manga, params int[] chapterIds)
        {
            return new RemoteChapter
            {
                Manga = manga,
                Chapters = chapterIds
                    .Select(id => new NzbDrone.Core.Manga.Chapter { Id = id, MangaId = manga.Id, ChapterNumber = id })
                    .ToList(),
                Release = new ReleaseInfo
                {
                    Title = "Test Manga - Chapter 001",
                    Indexer = "MangaDex",
                    Guid = "guid-1",
                    DownloadProtocol = DownloadProtocol.Http
                }
            };
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_inserts_one_grabbed_row()
        {
            var remote = BuildRemote(_manga, 42);

            Subject.Handle(new ChapterGrabbedEvent(remote, "dl-1", "InProcess"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>().Verify(r => r.Insert(It.Is<MangaDownloadHistory>(h =>
                h.EventType == MangaDownloadHistoryEventType.DownloadGrabbed &&
                h.DownloadId == "dl-1" &&
                h.MangaId == 7 &&
                h.SourceTitle == "Test Manga - Chapter 001" &&
                h.ChapterIds.Count == 1 &&
                h.ChapterIds[0] == 42)),
                Times.Once);
        }

        // GH-362 — the grab handler persists the gateway release guid into the Data dictionary so the
        // failure-path MapFromHistory can rehydrate RemoteChapter.Release.Guid (BuildRemote sets
        // Guid = "guid-1"). Without this key the blocklist row stores ReleaseGuid = null and a
        // transient failure over-blocks a same-titled federated mirror.
        [Test]
        public void Handle_ChapterGrabbedEvent_persists_release_guid_in_data_GH362()
        {
            var remote = BuildRemote(_manga, 42);

            Subject.Handle(new ChapterGrabbedEvent(remote, "dl-g", "InProcess"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>().Verify(r => r.Insert(It.Is<MangaDownloadHistory>(h =>
                h.Data.ContainsKey("guid") &&
                h.Data["guid"] == "guid-1")),
                Times.Once);
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_multi_chapter_resolves_all_chapter_ids()
        {
            var remote = BuildRemote(_manga, 179, 180, 181);

            Subject.Handle(new ChapterGrabbedEvent(remote, "dl-pack", "InProcess"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>().Verify(r => r.Insert(It.Is<MangaDownloadHistory>(h =>
                h.ChapterIds.SequenceEqual(new[] { 179, 180, 181 }))),
                Times.Once);
        }

        // CR-a — a grab carrying no chapter ids must NOT insert a chapterless join row (it would
        // poison the GetLatestGrab matcher path: a history HIT with nothing to bind).
        [Test]
        public void Handle_ChapterGrabbedEvent_with_no_chapter_ids_does_not_insert()
        {
            var remote = BuildRemote(_manga); // no chapter ids

            Subject.Handle(new ChapterGrabbedEvent(remote, "dl-empty", "InProcess"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.IsAny<MangaDownloadHistory>()), Times.Never);

            ExceptionVerification.ExpectedWarns(1);
        }

        // CR-a — same guard on the imported handler.
        [Test]
        public void Handle_ChapterDownloadCompletedEvent_with_no_chapter_ids_does_not_insert()
        {
            var tracked = new TrackedDownloadBuilder().WithDownloadId("dl-imp-empty").WithChapters().Completed().Build();

            Subject.Handle(new ChapterDownloadCompletedEvent(tracked, "dl-imp-empty"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>()
                .Verify(r => r.Insert(It.IsAny<MangaDownloadHistory>()), Times.Never);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void GetLatestGrab_returns_row_then_null_on_miss()
        {
            var row = new MangaDownloadHistory { DownloadId = "dl-1", EventType = MangaDownloadHistoryEventType.DownloadGrabbed };
            Mocker.GetMock<IMangaDownloadHistoryRepository>().Setup(r => r.GetLatestGrab("dl-1")).Returns(row);
            Mocker.GetMock<IMangaDownloadHistoryRepository>().Setup(r => r.GetLatestGrab("missing")).Returns((MangaDownloadHistory)null);

            Subject.GetLatestGrab("dl-1").Should().BeSameAs(row);
            Subject.GetLatestGrab("missing").Should().BeNull();
        }

        [Test]
        public void Handle_ChapterDownloadCompletedEvent_inserts_imported_row()
        {
            var tracked = new TrackedDownloadBuilder().WithDownloadId("dl-imp").Completed().Build();

            Subject.Handle(new ChapterDownloadCompletedEvent(tracked, "dl-imp"));

            Mocker.GetMock<IMangaDownloadHistoryRepository>().Verify(r => r.Insert(It.Is<MangaDownloadHistory>(h =>
                h.EventType == MangaDownloadHistoryEventType.DownloadImported &&
                h.DownloadId == "dl-imp" &&
                h.MangaId == 7 &&
                h.ChapterIds.Count == 1)),
                Times.Once);
        }

        // JSON round-trip — proves ChapterIds (List<int>) + Data (Dictionary<string,string>) survive
        // the embedded-document converter the TableMapping registers for these columns, without loss.
        // The converter is a Dapper TypeHandler<T> (SetValue → DB string, Parse → object); round-trip
        // via a stub IDbDataParameter to capture the serialized string the way Dapper would persist it.
        [Test]
        public void ChapterIds_and_Data_round_trip_through_embedded_document_converter()
        {
            var converter = new EmbeddedDocumentConverter<List<int>>();
            var dataConverter = new EmbeddedDocumentConverter<Dictionary<string, string>>();

            var ids = new List<int> { 179, 180, 181 };
            var idParam = new StubDbDataParameter();
            converter.SetValue(idParam, ids);
            var idsBack = converter.Parse(idParam.Value);
            idsBack.Should().Equal(ids);

            // The shared EmbeddedDocumentConverter camelCases dictionary keys (DictionaryKeyPolicy);
            // this is the SAME round-trip ChapterHistory.Data uses in production, so we assert the
            // values survive under the camelCased keys the converter actually persists.
            var data = new Dictionary<string, string> { { "downloadClient", "InProcess" }, { "indexer", "MangaDex" } };
            var dataParam = new StubDbDataParameter();
            dataConverter.SetValue(dataParam, data);
            var dataBack = dataConverter.Parse(dataParam.Value);
            dataBack.Should().BeEquivalentTo(data);
        }

        // Minimal IDbDataParameter to capture the serialized DB value from SetValue.
        private sealed class StubDbDataParameter : System.Data.IDbDataParameter
        {
            public byte Precision { get; set; }
            public byte Scale { get; set; }
            public int Size { get; set; }
            public System.Data.DbType DbType { get; set; }
            public System.Data.ParameterDirection Direction { get; set; }
            public bool IsNullable => true;
            public string ParameterName { get; set; }
            public string SourceColumn { get; set; }
            public System.Data.DataRowVersion SourceVersion { get; set; }
            public object Value { get; set; }
        }
    }
}
