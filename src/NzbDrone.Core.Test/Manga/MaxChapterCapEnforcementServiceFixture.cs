// Coverage for the NEW-in-Mangarr MaxChapterCapEnforcementService (quick-260619-o5q
// extension) — the destructive, user-edit-triggered companion to the ChapterSynthesisService
// cap clamp. The synthesis clamp PREVENTS new rows above the ceiling; this handler CLEANS UP
// existing rows (and their downloaded files) above it when the user sets/changes the cap.
//
// Effective deletion ceiling = max(MaxChapterNumber, TotalChapterCount ?? 0) — mirrors the
// synthesis-clamp contract: only the metadata chapter count can raise the ceiling above the cap.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    public class MaxChapterCapEnforcementServiceFixture : CoreTest<MaxChapterCapEnforcementService>
    {
        private Manga.Manga _manga;
        private List<ChapterFile> _files;

        [SetUp]
        public void Setup()
        {
            _manga = new Manga.Manga
            {
                Id = 7,
                Title = "Solo Leveling",
                Path = "/library/solo-leveling",
                TotalChapterCount = 38,
                MaxChapterNumber = 34,
            };

            _files = new List<ChapterFile>();

            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByManga(_manga.Id))
                .Returns(() => _files);
        }

        private void GivenChapters(params Chapter[] chapters)
        {
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_manga.Id))
                .Returns(chapters.ToList());
        }

        // Attaches a ChapterFile so the cap handler deletes the on-disk artifact too.
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

        private void Enforce() => Subject.Handle(new MangaEditedEvent(_manga, _manga));

        [Test]
        public void cap_below_metadata_deletes_chapters_above_metadata_ceiling()
        {
            // cap=34, metadata=38 → effective ceiling = max(34,38) = 38. Chapters 0..50 with files
            // on the high tail. Only the rows > 38 are deleted (rows AND their files); 0..38 stay.
            _manga.MaxChapterNumber = 34;
            _manga.TotalChapterCount = 38;

            var chapters = Enumerable.Range(0, 51)
                .Select(n => n > 38 ? WithFile(Ch(100 + n, n)) : Ch(100 + n, n))
                .ToArray();
            GivenChapters(chapters);

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            Enforce();

            deleted.Select(c => c.ChapterNumber)
                .Should().BeEquivalentTo(Enumerable.Range(39, 12).Select(n => (decimal)n));

            // Every with-file stray above 38 had its file recycle-binned.
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(_manga, It.IsAny<ChapterFile>()), Times.Exactly(12));
        }

        [Test]
        public void cap_above_metadata_deletes_chapters_above_cap_ceiling()
        {
            // cap=50, metadata=38 → effective ceiling = max(50,38) = 50. Chapters 0..60 → only >50.
            _manga.MaxChapterNumber = 50;
            _manga.TotalChapterCount = 38;

            var chapters = Enumerable.Range(0, 61).Select(n => Ch(100 + n, n)).ToArray();
            GivenChapters(chapters);

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            Enforce();

            deleted.Select(c => c.ChapterNumber)
                .Should().BeEquivalentTo(Enumerable.Range(51, 10).Select(n => (decimal)n));
        }

        [Test]
        public void null_cap_is_a_no_op()
        {
            _manga.MaxChapterNumber = null;
            _manga.TotalChapterCount = 38;
            GivenChapters(Ch(1, 1m), Ch(2, 999m));

            Enforce();

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);
        }

        [Test]
        public void nothing_above_ceiling_deletes_nothing()
        {
            // cap=34, metadata=38 → ceiling 38; all chapters at/below 38.
            _manga.MaxChapterNumber = 34;
            _manga.TotalChapterCount = 38;
            GivenChapters(Enumerable.Range(0, 39).Select(n => Ch(100 + n, n)).ToArray());

            Enforce();

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.DeleteMany(It.IsAny<List<Chapter>>()), Times.Never);
            Mocker.GetMock<IDeleteMediaFiles>()
                .Verify(s => s.DeleteChapterFile(It.IsAny<Manga.Manga>(), It.IsAny<ChapterFile>()), Times.Never);
        }

        [Test]
        public void fractional_chapter_just_above_ceiling_is_deleted()
        {
            // cap=34, metadata=38 → ceiling 38. A fractional 38.5 > 38 must be deleted; 38 stays.
            _manga.MaxChapterNumber = 34;
            _manga.TotalChapterCount = 38;
            GivenChapters(Ch(1, 38m), Ch(2, 38.5m));

            List<Chapter> deleted = null;
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.DeleteMany(It.IsAny<List<Chapter>>()))
                .Callback<List<Chapter>>(c => deleted = c);

            Enforce();

            deleted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 38.5m });
        }
    }
}
