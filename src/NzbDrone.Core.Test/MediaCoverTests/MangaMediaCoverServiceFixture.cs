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

            // Default: cover folder holds only this manga's own poster files, so the
            // defense-in-depth PruneOrphanedCovers pass is a no-op. Tests that exercise the
            // prune (or assert it never runs) override this.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), false))
                .Returns(new[] { "poster.jpg", "poster-250.jpg", "poster-500.jpg" });

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

        // ===================== debug wrong-cover-image-after-add — stale resized variant =====================

        // RED before the primary fix / GREEN after.
        // Repro: a delete + re-add lands a new manga on a previously-used Id (SQLite rowid
        // reuse — the `Manga` table has no AUTOINCREMENT). The previous tenant's poster-250.jpg
        // / poster-500.jpg are still on disk. HandleAsync(MangaUpdatedEvent) redownloads the
        // base poster.jpg (AlreadyExists returns false via the MangaDex HEAD-405 path) but the
        // OLD skip-if-exists EnsureResized branch left the stale resized variants in place, so
        // Library + Details rendered the previous manga's cover. After the fix the freshly
        // downloaded base file forces the resized variants to be deleted and regenerated.
        [Test]
        public void HandleAsync_should_delete_and_regenerate_stale_resized_variants_when_base_cover_redownloaded()
        {
            // Base cover is (re)downloaded — AlreadyExists short-circuit does not fire.
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            // Stale resized variants from the previous tenant of this reused Id are on disk.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            // Both poster heights (250, 500) must be deleted before re-resizing so the new
            // manga's freshly downloaded base poster.jpg is the source of truth.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.Is<string>(p => p.Contains("poster-250"))), Times.Once);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.Is<string>(p => p.Contains("poster-500"))), Times.Once);

            // ...and then regenerated from the fresh base file.
            Mocker.GetMock<IImageResizer>()
                .Verify(r => r.Resize(It.IsAny<string>(), It.Is<string>(p => p.Contains("poster-250")), 250), Times.Once);
            Mocker.GetMock<IImageResizer>()
                .Verify(r => r.Resize(It.IsAny<string>(), It.Is<string>(p => p.Contains("poster-500")), 500), Times.Once);
        }

        [Test]
        public void HandleAsync_should_NOT_delete_resized_variants_when_base_cover_already_exists()
        {
            // AlreadyExists short-circuits — the base file is NOT redownloaded, so the
            // resized variants must be left untouched (no needless delete/regenerate churn).
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);

            // No orphaned files — every on-disk file belongs to the manga's poster cover.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), false))
                .Returns(new[] { "poster.jpg", "poster-250.jpg", "poster-500.jpg" });

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IImageResizer>()
                .Verify(r => r.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        // ===================== debug wrong-cover-image-after-add — orphaned cover-type prune =====================

        // Defense-in-depth: a reused SQLite rowid can inherit a previous tenant's cover
        // folder. If the prior manga had a Banner cover and the new one only has a Poster,
        // the orphaned banner.jpg / banner-70.jpg / banner-110.jpg must be pruned before
        // the cover sync — the primary EnsureResized-regenerate fix only covers cover TYPES
        // the new manga still carries.
        [Test]
        public void HandleAsync_should_prune_orphaned_cover_type_files_from_reused_folder()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            // _manga has only a Poster cover. The folder still holds the prior tenant's
            // Banner files (orphaned cover type).
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), false))
                .Returns(new[]
                {
                    "poster.jpg", "poster-250.jpg", "poster-500.jpg",
                    "banner.jpg", "banner-70.jpg", "banner-110.jpg"
                });

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            // Orphaned banner files are pruned...
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile("banner.jpg"), Times.Once);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile("banner-70.jpg"), Times.Once);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile("banner-110.jpg"), Times.Once);

            // ...and the manga's own poster files are left alone by the prune.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.Is<string>(p => p.Contains("poster"))), Times.Never);
        }

        [Test]
        public void HandleAsync_prune_failure_is_swallowed_and_does_not_abort_cover_sync()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(s => s.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            // GetFiles throws — prune must swallow it (Warn) and the cover sync must still run.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), false))
                .Throws(new System.IO.IOException("simulated enumeration failure"));

            Subject.HandleAsync(new MangaUpdatedEvent(_manga));

            // Cover download still happened despite the prune failure.
            Mocker.GetMock<IHttpClient>()
                .Verify(h => h.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaCoversUpdatedEvent>()), Times.Once);

            // Production Warn-logs the swallowed IOException; declare it so AssertNoUnexpectedLogs passes.
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
