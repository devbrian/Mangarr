// Coverage for the NEW-in-Mangarr StrayChapterPruneService — the one-time cleanup companion to
// the ChapterSynthesisService density-floor guard. Verifies the two-bucket split (file-less vs
// with-file), the metadata-baseline boundary, the dry-run no-mutation contract, and the
// deleteFiles opt-in gate on with-file strays.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    public class StrayChapterPruneServiceFixture : CoreTest<StrayChapterPruneService>
    {
        private Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new Manga.Manga
            {
                Id = 7,
                Title = "The Heavenly Demon Wants a Quiet Life",
                Path = "/library/heavenly-demon",
                TotalChapterCount = 86,
            };

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(_manga.Id))
                .Returns(_manga);

            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(new List<ChapterFile>());
        }

        private void GivenChapters(params Chapter[] chapters)
        {
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_manga.Id))
                .Returns(chapters.ToList());

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChapters(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids =>
                    chapters.Where(c => ids.Contains(c.Id)).ToList());
        }

        [Test]
        public void BuildReport_buckets_file_less_strays_above_baseline()
        {
            // 1..86 are trusted (<= baseline); 164/703/726 are file-less strays above it.
            var chapters = Enumerable.Range(1, 86)
                .Select(n => new Chapter { Id = n, MangaId = 7, ChapterNumber = n })
                .Concat(new[]
                {
                    new Chapter { Id = 200, MangaId = 7, ChapterNumber = 164m },
                    new Chapter { Id = 201, MangaId = 7, ChapterNumber = 703m },
                    new Chapter { Id = 202, MangaId = 7, ChapterNumber = 726m },
                })
                .ToArray();
            GivenChapters(chapters);

            var report = Subject.BuildReport(_manga.Id);

            report.BaselineKnown.Should().BeTrue();
            report.DryRun.Should().BeTrue();
            report.FileLessStrays.Select(s => s.ChapterNumber)
                .Should().BeEquivalentTo(new[] { 164m, 703m, 726m });
            report.WithFileStrays.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_buckets_strays_with_a_file_separately()
        {
            GivenChapters(
                new Chapter { Id = 200, MangaId = 7, ChapterNumber = 164m, ChapterFileId = 9001 });

            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(new List<ChapterFile>
                {
                    new() { Id = 9001, MangaId = 7, ChapterId = 200, RelativePath = "ch164.cbz", Size = 1234 },
                });

            var report = Subject.BuildReport(_manga.Id);

            report.FileLessStrays.Should().BeEmpty();
            report.WithFileStrays.Should().ContainSingle();
            report.WithFileStrays[0].ChapterNumber.Should().Be(164m);
            report.WithFileStrays[0].Files.Should().ContainSingle();
            report.WithFileStrays[0].Files[0].Path.Should().Contain("ch164.cbz");
        }

        [Test]
        public void BuildReport_skips_when_no_metadata_baseline()
        {
            _manga.TotalChapterCount = null;
            GivenChapters(new Chapter { Id = 200, MangaId = 7, ChapterNumber = 999m });

            var report = Subject.BuildReport(_manga.Id);

            report.BaselineKnown.Should().BeFalse();
            report.FileLessStrays.Should().BeEmpty();
            report.WithFileStrays.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_never_mutates()
        {
            GivenChapters(new Chapter { Id = 200, MangaId = 7, ChapterNumber = 726m });

            Subject.BuildReport(_manga.Id);

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);
        }

        [Test]
        public void Prune_deletes_file_less_strays()
        {
            GivenChapters(
                new Chapter { Id = 1, MangaId = 7, ChapterNumber = 1m },
                new Chapter { Id = 200, MangaId = 7, ChapterNumber = 164m },
                new Chapter { Id = 201, MangaId = 7, ChapterNumber = 726m });

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.DeletedChapterRowCount.Should().Be(2);
            deleted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 164m, 726m });
        }

        [Test]
        public void Prune_leaves_with_file_strays_untouched_when_deleteFiles_false()
        {
            GivenChapters(
                new Chapter { Id = 200, MangaId = 7, ChapterNumber = 164m, ChapterFileId = 9001 });
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(new List<ChapterFile>
                {
                    new() { Id = 9001, MangaId = 7, ChapterId = 200, RelativePath = "ch164.cbz" },
                });

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.DeletedFileCount.Should().Be(0);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);

            // The with-file stray is reported but its row is NOT deleted.
            report.WithFileStrays.Should().ContainSingle();
        }

        [Test]
        public void Prune_recycle_bins_with_file_strays_when_deleteFiles_true()
        {
            var file = new ChapterFile { Id = 9001, MangaId = 7, ChapterId = 200, RelativePath = "ch164.cbz" };
            GivenChapters(
                new Chapter { Id = 200, MangaId = 7, ChapterNumber = 164m, ChapterFileId = 9001 });
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(new List<ChapterFile> { file });
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.Get(9001))
                .Returns(file);

            var report = Subject.Prune(_manga.Id, deleteFiles: true);

            report.DeletedFileCount.Should().Be(1);
            report.DeletedChapterRowCount.Should().Be(1);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(_manga, file), Times.Once);
        }
    }
}
