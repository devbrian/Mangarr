using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    // Phase 9 Plan 09-13 fixture: covers (a) MangaCoversUpdatedEvent publish on
    // HandleAsync(MangaUpdatedEvent) — Updated=true / Updated=false / never on EnsureCoversFolder
    // throw, (b) Pitfall 4 ordering (DownloadFile BEFORE PublishEvent), (c) ConvertToLocalUrls
    // saved-manga local URL with `?lastWrite=` cache-bust + saved-manga without lastWrite when
    // file absent + mangaId == 0 proxy fallback + MediaCoverTypes.Unknown skip.
    [TestFixture]
    public class MangaMediaCoverServiceFixture : CoreTest<MangaMediaCoverService>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, RemoteUrl = "https://example.org/poster.jpg" }
                })
                .Build();

            // Default: folder exists, so EnsureCoversFolder is a no-op (no CreateFolder
            // invocation). Individual tests override to false to exercise the WR-16 path.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IConfigFileProvider>()
                .SetupGet(c => c.UrlBase)
                .Returns(string.Empty);
        }

        // ===================== gap-03 — event publish =====================

        [Test]
        public void HandleAsync_should_publish_MangaCoversUpdatedEvent_with_Updated_true_when_at_least_one_cover_downloaded()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.Is<MangaCoversUpdatedEvent>(
                        evt => evt.Manga.Id == _manga.Id && evt.Updated == true)),
                    Times.Once);
        }

        [Test]
        public void HandleAsync_should_publish_MangaCoversUpdatedEvent_with_Updated_false_when_AlreadyExists_short_circuits_all_covers()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.Is<MangaCoversUpdatedEvent>(
                        evt => evt.Manga.Id == _manga.Id && evt.Updated == false)),
                    Times.Once);
        }

        [Test]
        public void HandleAsync_should_NOT_publish_event_when_EnsureCoversFolder_throws()
        {
            // Force EnsureCoversFolder to call CreateFolder, then make CreateFolder throw —
            // exercises the WR-16 try/catch early return.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(It.IsAny<string>()))
                .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.CreateFolder(It.IsAny<string>()))
                .Throws(new System.IO.IOException("simulated permissions failure"));

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaCoversUpdatedEvent>()), Times.Never);

            // Production deliberately Warn-logs the swallowed IOException at MangaMediaCoverService.cs:177
            // before returning early. Declare the expected Warn so TestBase.AssertNoUnexpectedLogs
            // (which calls ExpectedWarns(0) in TearDown) does not fail this test.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Pitfall_4_ordering_disk_writes_complete_BEFORE_event_publish()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            var sequence = new MockSequence();

            Mocker.GetMock<IHttpClient>()
                .InSequence(sequence)
                .Setup(h => h.DownloadFile(It.IsAny<string>(), It.IsAny<string>()));

            Mocker.GetMock<IEventAggregator>()
                .InSequence(sequence)
                .Setup(e => e.PublishEvent(It.IsAny<MangaCoversUpdatedEvent>()));

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            // Sequence violation throws during the verify; reaching here implies ordering held.
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaCoversUpdatedEvent>()), Times.Once);
        }

        // ===================== gap-04 — ConvertToLocalUrls =====================

        [Test]
        public void ConvertToLocalUrls_returns_local_url_with_lastWrite_when_saved_manga_and_file_exists()
        {
            var lastWrite = new DateTime(2026, 5, 5, 12, 30, 45, DateTimeKind.Utc);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileGetLastWrite(It.IsAny<string>()))
                .Returns(lastWrite);

            var covers = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, RemoteUrl = "https://example.org/poster.jpg" }
            };

            Subject.ConvertToLocalUrls(42, covers);

            covers[0].Url.Should().Be("/MediaCover/manga/42/poster.jpg?lastWrite=" + lastWrite.Ticks);
        }

        [Test]
        public void ConvertToLocalUrls_returns_local_url_without_lastWrite_when_saved_manga_and_file_absent()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(false);

            var covers = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, RemoteUrl = "https://example.org/poster.jpg" }
            };

            Subject.ConvertToLocalUrls(42, covers);

            covers[0].Url.Should().Be("/MediaCover/manga/42/poster.jpg");
        }

        [Test]
        public void ConvertToLocalUrls_returns_proxy_url_when_mangaId_is_zero()
        {
            Mocker.GetMock<IMediaCoverProxy>()
                .Setup(p => p.RegisterUrl(It.IsAny<string>()))
                .Returns("/api/v5/MediaCoverProxy/abc123/poster.jpg");

            var covers = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, RemoteUrl = "https://example.org/poster.jpg" }
            };

            Subject.ConvertToLocalUrls(0, covers);

            covers[0].Url.Should().Be("/api/v5/MediaCoverProxy/abc123/poster.jpg");
        }

        [Test]
        public void ConvertToLocalUrls_skips_Unknown_cover_type_in_saved_branch()
        {
            var covers = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover { CoverType = MediaCoverTypes.Unknown, RemoteUrl = "https://example.org/unknown.jpg", Url = "preserved" }
            };

            Subject.ConvertToLocalUrls(42, covers);

            covers[0].Url.Should().Be("preserved");
        }
    }
}
