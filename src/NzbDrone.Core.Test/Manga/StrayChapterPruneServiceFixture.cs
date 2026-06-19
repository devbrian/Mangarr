// Coverage for the NEW-in-Mangarr StrayChapterPruneService — the cleanup companion to the
// ChapterSynthesisService density-floor guard. Verifies the WITH-FILE-anchored density cut
// classification (legit-extension vs confident-junk vs uncertain vs with-file), the metadata-
// baseline boundary, the dry-run no-mutation contract, and the deleteFiles / pruneUncertain
// opt-in gates.
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
        private List<ChapterFile> _files;

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

            _files = new List<ChapterFile>();

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(_manga.Id))
                .Returns(_manga);

            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(() => _files);
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

        // Attaches a ChapterFile to a chapter so HasFile + the density anchor see on-disk evidence.
        private Chapter WithFile(Chapter chapter, long size = 1234)
        {
            var fileId = 9000 + chapter.Id;
            chapter.ChapterFileId = fileId;
            var file = new ChapterFile
            {
                Id = fileId,
                MangaId = _manga.Id,
                ChapterId = chapter.Id,
                RelativePath = $"ch{chapter.ChapterNumber}.cbz",
                Size = size,
            };
            _files.Add(file);
            Mocker.GetMock<IChapterFileService>().Setup(s => s.Get(fileId)).Returns(file);
            return chapter;
        }

        private Chapter Ch(int id, decimal number) => new() { Id = id, MangaId = _manga.Id, ChapterNumber = number };

        [Test]
        public void BuildReport_classifies_sparse_file_less_outliers_as_uncertain_without_disk_evidence()
        {
            // 1..86 trusted; 164/703/726 are sparse file-less rows with NOTHING on disk above the
            // baseline to anchor a cut — so they are UNCERTAIN (could be wanted-legit OR phantom).
            var chapters = Enumerable.Range(1, 86).Select(n => Ch(n, n))
                .Concat(new[] { Ch(200, 164m), Ch(201, 703m), Ch(202, 726m) })
                .ToArray();
            GivenChapters(chapters);

            var report = Subject.BuildReport(_manga.Id);

            report.BaselineKnown.Should().BeTrue();
            report.DryRun.Should().BeTrue();
            report.DiskEvidenceAboveBaseline.Should().BeFalse();
            report.DensityCut.Should().Be(86m);
            report.UncertainStrays.Select(s => s.ChapterNumber)
                .Should().BeEquivalentTo(new[] { 164m, 703m, 726m });
            report.FileLessStrays.Should().BeEmpty();
            report.WithFileStrays.Should().BeEmpty();
            report.LegitExtension.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_classifies_file_less_outliers_as_confident_junk_with_disk_evidence()
        {
            // The Heavenly-Demon shape: a handful of mislabeled releases got grabbed (on disk at
            // 703/726) and a pre-fix reconciliation backfilled file-less phantoms (164/200/300).
            // The with-file numbers are sparse far-out (density << floor) so the cut falls back to
            // the baseline — and because on-disk evidence DOES exist above the baseline, the
            // file-less rows are CONFIDENT junk, not uncertain.
            var chapters = Enumerable.Range(1, 86).Select(n => Ch(n, n))
                .Concat(new[]
                {
                    Ch(200, 164m), Ch(201, 200m), Ch(202, 300m),
                    WithFile(Ch(301, 703m)), WithFile(Ch(302, 726m)),
                })
                .ToArray();
            GivenChapters(chapters);

            var report = Subject.BuildReport(_manga.Id);

            report.DiskEvidenceAboveBaseline.Should().BeTrue();
            report.DensityCut.Should().Be(86m);
            report.FileLessStrays.Select(s => s.ChapterNumber)
                .Should().BeEquivalentTo(new[] { 164m, 200m, 300m });
            report.WithFileStrays.Select(s => s.ChapterNumber)
                .Should().BeEquivalentTo(new[] { 703m, 726m });
            report.UncertainStrays.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_spares_dense_extension_past_a_stale_metadata_count()
        {
            // Stale metadata count: metadata says 86 but 87..120 are real, dense, on disk. The cut
            // climbs to 120 and EVERY row at/below it is a legit extension — NONE are pruned. This
            // is the bug the density anchor fixes (the old crude "> count" signal deleted these).
            _manga.TotalChapterCount = 86;
            var extension = Enumerable.Range(87, 34).Select(n => WithFile(Ch(n, n))).ToArray();
            var chapters = Enumerable.Range(1, 86).Select(n => Ch(n, n))
                .Concat(extension)
                .ToArray();
            GivenChapters(chapters);

            var report = Subject.BuildReport(_manga.Id);

            report.DensityCut.Should().Be(120m);
            report.LegitExtension.Select(s => s.ChapterNumber)
                .Should().BeEquivalentTo(Enumerable.Range(87, 34).Select(n => (decimal)n));
            report.FileLessStrays.Should().BeEmpty();
            report.WithFileStrays.Should().BeEmpty();
            report.UncertainStrays.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_keeps_dense_cluster_but_flags_the_far_outlier()
        {
            // Dense real extension 87..120 on disk (cut=120) PLUS a far file-less outlier at 500.
            // The cluster is spared; only the outlier above the cut is flagged.
            _manga.TotalChapterCount = 86;
            var extension = Enumerable.Range(87, 34).Select(n => WithFile(Ch(n, n))).ToArray();
            var chapters = Enumerable.Range(1, 86).Select(n => Ch(n, n))
                .Concat(extension)
                .Concat(new[] { Ch(900, 500m) })
                .ToArray();
            GivenChapters(chapters);

            var report = Subject.BuildReport(_manga.Id);

            report.DensityCut.Should().Be(120m);
            report.LegitExtension.Should().HaveCount(34);
            report.FileLessStrays.Select(s => s.ChapterNumber).Should().BeEquivalentTo(new[] { 500m });
            report.UncertainStrays.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_buckets_strays_with_a_file_separately()
        {
            GivenChapters(WithFile(Ch(200, 164m)));

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
            GivenChapters(Ch(200, 999m));

            var report = Subject.BuildReport(_manga.Id);

            report.BaselineKnown.Should().BeFalse();
            report.FileLessStrays.Should().BeEmpty();
            report.WithFileStrays.Should().BeEmpty();
            report.UncertainStrays.Should().BeEmpty();
            report.LegitExtension.Should().BeEmpty();
        }

        [Test]
        public void BuildReport_never_mutates()
        {
            GivenChapters(Ch(200, 726m));

            Subject.BuildReport(_manga.Id);

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);
        }

        [Test]
        public void Prune_deletes_confident_file_less_junk()
        {
            // 164/726 are file-less; 703 is on disk (anchors the cut at baseline → confident junk).
            GivenChapters(
                Ch(1, 1m),
                Ch(200, 164m),
                Ch(201, 726m),
                WithFile(Ch(202, 703m)));

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.DeletedChapterRowCount.Should().Be(2);
            deleted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 164m, 726m });
            report.WithFileStrays.Should().ContainSingle(); // 703 left untouched (deleteFiles false)
        }

        [Test]
        public void Prune_leaves_uncertain_strays_untouched_by_default()
        {
            // Sparse file-less, NO disk evidence → uncertain → not deleted without the opt-in.
            GivenChapters(Ch(200, 164m), Ch(201, 726m));

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.UncertainStrays.Should().HaveCount(2);
            report.DeletedChapterRowCount.Should().Be(0);
            Mocker.GetMock<IChapterService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
        }

        [Test]
        public void Prune_deletes_uncertain_strays_when_pruneUncertain_true()
        {
            GivenChapters(Ch(200, 164m), Ch(201, 726m));

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            var report = Subject.Prune(_manga.Id, deleteFiles: false, pruneUncertain: true);

            report.DeletedChapterRowCount.Should().Be(2);
            deleted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 164m, 726m });
        }

        [Test]
        public void Prune_never_deletes_legit_extension_rows()
        {
            // 87..120 are a dense real extension (spared); 500 is a confident file-less outlier.
            _manga.TotalChapterCount = 86;
            var extension = Enumerable.Range(87, 34).Select(n => WithFile(Ch(n, n))).ToArray();
            GivenChapters(extension.Concat(new[] { Ch(900, 500m) }).ToArray());

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.LegitExtension.Should().HaveCount(34);
            report.DeletedChapterRowCount.Should().Be(1);
            deleted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 500m });
        }

        [Test]
        public void Prune_leaves_with_file_strays_untouched_when_deleteFiles_false()
        {
            GivenChapters(WithFile(Ch(200, 164m)));

            var report = Subject.Prune(_manga.Id, deleteFiles: false);

            report.DeletedFileCount.Should().Be(0);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);
            report.WithFileStrays.Should().ContainSingle();
        }

        [Test]
        public void Prune_recycle_bins_with_file_strays_when_deleteFiles_true()
        {
            var chapter = WithFile(Ch(200, 164m));
            var file = _files.Single();
            GivenChapters(chapter);

            var report = Subject.Prune(_manga.Id, deleteFiles: true);

            report.DeletedFileCount.Should().Be(1);
            report.DeletedChapterRowCount.Should().Be(1);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(_manga, file), Times.Once);
        }
    }
}
