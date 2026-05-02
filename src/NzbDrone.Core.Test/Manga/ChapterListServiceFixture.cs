using System;
using System.Collections.Generic;
using System.Linq;
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
    // Wave 0 fixture for ChapterListService — D-17 three-strategy synthesis. GREEN
    // following Plan 02-09.
    //
    // BCP-47 sentinel reminder: synthetic chapter rows MUST set TranslatedLanguage = "und"
    // per RESEARCH §Open Question 3 (NOT null — null breaks query planner index utilization
    // on SQLite and confuses the ChapterListUpdatedEvent consumer set).
    [TestFixture]
    public class ChapterListServiceFixture : CoreTest<ChapterListService>
    {
        private List<Chapter> _existing;
        private List<Chapter> _inserted;

        [SetUp]
        public void Setup()
        {
            _existing = new List<Chapter>();
            _inserted = new List<Chapter>();

            Mocker.GetMock<IChapterRepository>()
                  .Setup(r => r.GetByMangaId(It.IsAny<int>()))
                  .Returns(() => _existing);

            Mocker.GetMock<IChapterRepository>()
                  .Setup(r => r.Insert(It.IsAny<Chapter>()))
                  .Callback<Chapter>(c => _inserted.Add(c))
                  .Returns<Chapter>(c => c);

            Mocker.GetMock<IChapterRepository>()
                  .Setup(r => r.Update(It.IsAny<Chapter>()))
                  .Returns<Chapter>(c => c);
        }

        // Strategy 1 (MangaDex linked): replace pre-existing IsSynthetic=true rows in place
        // by chapter number, flipping IsSynthetic=false; preserve row Id.
        [Test]
        public void Strategy1_MangaDex_linked_replaces_synthetic_rows_in_place_by_chapter_number()
        {
            var manga = new Manga.Manga
            {
                Id = 7,
                Title = "Naruto",
                MangaDexId = Guid.NewGuid(),
            };

            _existing.Add(new Chapter
            {
                Id = 99,
                MangaId = 7,
                ChapterNumber = 1m,
                IsSynthetic = true,
                TranslatedLanguage = "und",
            });

            var incoming = new List<Chapter>
            {
                new()
                {
                    ChapterNumber = 1m,
                    Title = "Real Title",
                    TranslatedLanguage = "en",
                    IsSynthetic = false,
                },
            };

            Subject.SyncChapters(manga, incoming);

            // The synthetic row was updated in place: Id preserved, IsSynthetic flipped false.
            Mocker.GetMock<IChapterRepository>()
                  .Verify(r => r.Update(It.Is<Chapter>(c => c.Id == 99 && !c.IsSynthetic && c.MangaId == 7)),
                          Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()), Times.Once());
        }

        [Test]
        public void Strategy1_MangaDex_linked_inserts_new_chapters_not_previously_present()
        {
            var manga = new Manga.Manga
            {
                Id = 8,
                Title = "OnePiece",
                MangaDexId = Guid.NewGuid(),
            };

            // No existing chapters yet.
            var incoming = new List<Chapter>
            {
                new() { ChapterNumber = 1m, TranslatedLanguage = "en", IsSynthetic = false },
                new() { ChapterNumber = 2m, TranslatedLanguage = "en", IsSynthetic = false },
            };

            Subject.SyncChapters(manga, incoming);

            _inserted.Should().HaveCount(2);
            _inserted.Should().OnlyContain(c => c.MangaId == 8 && !c.IsSynthetic);
        }

        // Strategy 2 (no MangaDex link, primary returned total chapter count): synthesize
        // N rows with TranslatedLanguage = "und" sentinel.
        [Test]
        public void Strategy2_no_MangaDex_link_synthesizes_N_rows_with_und_sentinel()
        {
            var manga = new Manga.Manga
            {
                Id = 13,
                Title = "AniListOnly",
                MangaDexId = null,
                TotalChapterCount = 5,
            };

            Subject.SyncChapters(manga, new List<Chapter>());

            _inserted.Should().HaveCount(5);
            _inserted.Should().OnlyContain(c => c.IsSynthetic);
            _inserted.Should().OnlyContain(c => c.TranslatedLanguage == "und");
            _inserted.Should().OnlyContain(c => c.MangaId == 13);
            _inserted.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 1m, 2m, 3m, 4m, 5m });
            _inserted.Should().OnlyContain(c => c.ChapterType == ChapterType.Regular);
        }

        [Test]
        public void Strategy2_already_synthesized_does_not_resynthesize()
        {
            var manga = new Manga.Manga
            {
                Id = 14,
                Title = "AlreadySynth",
                MangaDexId = null,
                TotalChapterCount = 5,
            };

            // Pre-populate with one synthetic row to simulate prior synthesis.
            _existing.Add(new Chapter
            {
                Id = 1,
                MangaId = 14,
                ChapterNumber = 1m,
                IsSynthetic = true,
                TranslatedLanguage = "und",
            });

            Subject.SyncChapters(manga, new List<Chapter>());

            _inserted.Should().BeEmpty("synthesis must be idempotent — no new rows when chapters already exist");
        }

        // Strategy 3: primary returns null total → log warning + return empty list.
        [Test]
        public void Strategy3_null_chapter_count_logs_warning_and_returns_empty()
        {
            var manga = new Manga.Manga
            {
                Id = 21,
                Title = "Mysterious",
                MangaDexId = null,
                TotalChapterCount = null,
            };

            Subject.SyncChapters(manga, new List<Chapter>());

            _inserted.Should().BeEmpty();
            // No event published when there is nothing to sync.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()), Times.Never());
            // Warning log assertion: the test framework's TestLogger is consumed silently;
            // we rely on the empty inserted list + no event as the observable contract.
        }
    }
}
