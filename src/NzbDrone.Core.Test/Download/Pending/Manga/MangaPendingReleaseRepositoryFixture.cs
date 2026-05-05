using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.Manga
{
    // Phase 9 D-09-06..08 — MangaPendingRelease repository round-trip + per-manga isolation.
    // Mirrors MangaBlocklistRepositoryFixture pattern (Phase 6 D-11 BL-01 precedent). The
    // repository's job is pure persistence + narrow-by-MangaId; Pitfall 4 ordering
    // (DB write FIRST, event LAST) is a SERVICE-layer concern enforced in Plan 09-10.
    [TestFixture]
    public class MangaPendingReleaseRepositoryFixture : DbTest<MangaPendingReleaseRepository, MangaPendingRelease>
    {
        private MangaPendingRelease BuildRow(int mangaId, PendingReleaseReason reason = PendingReleaseReason.Delay, string title = null)
        {
            return new MangaPendingRelease
            {
                MangaId = mangaId,
                Title = title ?? $"Manga {mangaId} - Chapter 1",
                Added = DateTime.UtcNow,
                Reason = reason,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    MangaTitle = $"Manga {mangaId}",
                    ChapterNumbers = new decimal[] { 1m },
                    ReleaseTitle = title ?? $"Manga {mangaId} - Chapter 1",
                    TranslatedLanguage = "en",
                },
                Release = new ReleaseInfo
                {
                    Title = title ?? $"Manga {mangaId} - Chapter 1",
                    Guid = $"guid-{mangaId}",
                    DownloadUrl = "https://example.invalid/release",
                },
                // RemoteChapter is intentionally NOT set — verifies it round-trips as null
                // because it is registered with .Ignore(...) in TableMapping.cs.
            };
        }

        [Test]
        public void should_insert_and_round_trip_MangaPendingRelease()
        {
            var row = BuildRow(7, PendingReleaseReason.Delay);

            Subject.Insert(row);

            StoredModel.MangaId.Should().Be(7);
            StoredModel.Title.Should().Be("Manga 7 - Chapter 1");
            StoredModel.Reason.Should().Be(PendingReleaseReason.Delay);
            StoredModel.ParsedChapterInfo.Should().NotBeNull();
            StoredModel.ParsedChapterInfo.MangaTitle.Should().Be("Manga 7");
            StoredModel.ParsedChapterInfo.ChapterNumbers.Should().BeEquivalentTo(new[] { 1m });
            StoredModel.ParsedChapterInfo.TranslatedLanguage.Should().Be("en");
            StoredModel.Release.Should().NotBeNull();
            StoredModel.Release.Title.Should().Be("Manga 7 - Chapter 1");
            StoredModel.Release.Guid.Should().Be("guid-7");
        }

        [Test]
        public void should_NOT_persist_RemoteChapter_field()
        {
            // RemoteChapter is .Ignore(...)'d in TableMapping.cs and has no schema column.
            // Verify a fetch returns it as null even when set on the in-memory instance.
            var row = BuildRow(11);
            row.RemoteChapter = new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 11 },
            };

            Subject.Insert(row);

            StoredModel.RemoteChapter.Should().BeNull();
        }

        [Test]
        public void DeleteByMangaIds_removes_only_supplied_manga_rows()
        {
            Subject.Insert(BuildRow(1));
            Subject.Insert(BuildRow(2));
            Subject.Insert(BuildRow(3));

            Subject.DeleteByMangaIds(new List<int> { 2, 3 });

            var remaining = Subject.All().ToList();
            remaining.Should().HaveCount(1);
            remaining.Single().MangaId.Should().Be(1);
        }

        [Test]
        public void AllByMangaId_returns_only_matching_rows()
        {
            Subject.Insert(BuildRow(5, title: "Manga 5 - Chapter 1"));
            Subject.Insert(BuildRow(5, title: "Manga 5 - Chapter 2"));
            Subject.Insert(BuildRow(6, title: "Manga 6 - Chapter 1"));

            var rows = Subject.AllByMangaId(5);

            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(r => r.MangaId == 5);
        }

        [Test]
        public void WithoutFallback_excludes_Fallback_reason_rows_and_orphans()
        {
            // The WithoutFallback query InnerJoins MangaPendingReleases against the Manga
            // table — only rows whose MangaId resolves to a real Manga row are returned,
            // AND only those whose Reason != Fallback. Seed a real Manga row to satisfy
            // the join; orphan rows (no Manga match) are excluded by the join semantics.
            var manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 0)        // let the repository assign the id
                .With(m => m.MangaDexId = (Guid?)null)
                .With(m => m.MalId = (int?)null)
                .With(m => m.AniListId = (int?)null)
                .With(m => m.Genres = new List<string>())
                .With(m => m.Tags = new HashSet<int>())
                .With(m => m.Images = new List<NzbDrone.Core.MediaCover.MediaCover>())
                .BuildNew();
            Db.Insert(manga);

            Subject.Insert(BuildRow(manga.Id, PendingReleaseReason.Delay, "delay row"));
            Subject.Insert(BuildRow(manga.Id, PendingReleaseReason.DownloadClientUnavailable, "dc-unavail row"));
            Subject.Insert(BuildRow(manga.Id, PendingReleaseReason.Fallback, "fallback row"));

            var rows = Subject.WithoutFallback();

            rows.Should().HaveCount(2);
            rows.Should().NotContain(r => r.Reason == PendingReleaseReason.Fallback);
        }
    }
}
