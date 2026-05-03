using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-01 Wave 0 fixture — exercises CRUD + projection methods on the real
    /// SQLite-backed <see cref="ChapterDownloadStateRepository"/>. NOT mock-based; extends
    /// <see cref="DbTest{TSubject, TModel}"/> so each test runs against a fresh migrated DB
    /// (<c>[SetUp]</c> from <c>DbTest</c> purges between tests — Phase 3 LEARNINGS exception
    /// to "test never owns the table" anti-pattern, since the harness does the purge).
    /// </summary>
    [TestFixture]
    public class ChapterDownloadStateRepositoryFixture : DbTest<ChapterDownloadStateRepository, ChapterDownloadState>
    {
        private static int _chapterIdSeed = 1000;

        private ChapterDownloadState MakeRow(ChapterDownloadStatus status, DateTime? retentionUntil)
        {
            // Unique ChapterId per row so FindByMangaAndChapter assertions are deterministic.
            return new ChapterDownloadState
            {
                MangaId = 1,
                ChapterId = System.Threading.Interlocked.Increment(ref _chapterIdSeed),
                Title = "T",
                RemoteChapterJson = "{}",   // NotNullable in schema; test stub
                ScratchDir = "/tmp/scratch/0",
                Status = status,
                RetentionUntil = retentionUntil,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        [Test]
        public void Insert_then_FindByMangaAndChapter_returns_row()
        {
            var row = new ChapterDownloadState
            {
                MangaId = 1,
                ChapterId = 100,
                Title = "Chapter 132",
                RemoteChapterJson = "{}",
                ScratchDir = "/tmp/scratch/1",
                Status = ChapterDownloadStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            Subject.Insert(row);

            var found = Subject.FindByMangaAndChapter(1, 100);
            found.Should().NotBeNull();
            found.Title.Should().Be("Chapter 132");
            found.Status.Should().Be(ChapterDownloadStatus.Queued);
        }

        [Test]
        public void AllInFlight_returns_Queued_Downloading_Completing_Completed_and_unexpired_Failed()
        {
            Subject.Insert(MakeRow(ChapterDownloadStatus.Queued, retentionUntil: null));
            Subject.Insert(MakeRow(ChapterDownloadStatus.Downloading, retentionUntil: null));
            Subject.Insert(MakeRow(ChapterDownloadStatus.Completing, retentionUntil: null));
            Subject.Insert(MakeRow(ChapterDownloadStatus.Completed, retentionUntil: null));
            var unexpiredFailed = Subject.Insert(MakeRow(ChapterDownloadStatus.Failed, retentionUntil: DateTime.UtcNow.AddDays(1)));
            var expiredFailed = Subject.Insert(MakeRow(ChapterDownloadStatus.Failed, retentionUntil: DateTime.UtcNow.AddDays(-1)));

            var inFlight = Subject.AllInFlight().ToList();

            // 4 non-Failed rows + 1 unexpired Failed row = 5 total. Expired Failed row excluded.
            inFlight.Should().HaveCount(5);
            inFlight.Should().Contain(r => r.Id == unexpiredFailed.Id);
            inFlight.Should().NotContain(r => r.Id == expiredFailed.Id);
        }

        [Test]
        public void DeleteOrphans_removes_rows_past_retention()
        {
            var stale = Subject.Insert(MakeRow(ChapterDownloadStatus.Failed, retentionUntil: DateTime.UtcNow.AddDays(-1)));
            var fresh = Subject.Insert(MakeRow(ChapterDownloadStatus.Failed, retentionUntil: DateTime.UtcNow.AddDays(1)));

            Subject.DeleteOrphans(DateTime.UtcNow);

            var remaining = Subject.All().ToList();
            remaining.Should().NotContain(r => r.Id == stale.Id);
            remaining.Should().Contain(r => r.Id == fresh.Id);
        }

        [Test]
        public void ByStatus_filters_to_single_status()
        {
            Subject.Insert(MakeRow(ChapterDownloadStatus.Queued, null));
            Subject.Insert(MakeRow(ChapterDownloadStatus.Downloading, null));

            Subject.ByStatus(ChapterDownloadStatus.Queued).Should().HaveCount(1);
            Subject.ByStatus(ChapterDownloadStatus.Downloading).Should().HaveCount(1);
        }
    }
}
