using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting.Manga
{
    // Phase 6 D-11 — MangaBlocklist repository round-trip + per-manga isolation regression.
    // The repository's job is to narrow to candidate rows by MangaId; case-insensitive
    // title/guid matching with Pitfall 5 mitigation is the SERVICE-layer concern (see
    // MangaBlocklistServiceFixture). Repository tests cover persistence + cascade delete only.
    [TestFixture]
    public class MangaBlocklistRepositoryFixture : DbTest<MangaBlocklistRepository, MangaBlocklist>
    {
        [Test]
        public void should_round_trip_manga_blocklist_with_release_identity_triple()
        {
            var blocklist = new MangaBlocklist
            {
                MangaId = 7,
                ChapterIds = new List<int> { 42, 43 },
                SourceTitle = "Vinland Saga - Chapter 0001",
                SourceKey = "MangaDex",
                ReleaseGuid = "guid-1",
                ReleaseInfoJson = "{\"Title\":\"Vinland Saga - Chapter 0001\"}",
                Date = System.DateTime.UtcNow,
                Reason = "404 image missing",
                Source = "ImageDownload"
            };

            Subject.Insert(blocklist);

            StoredModel.MangaId.Should().Be(7);
            StoredModel.ChapterIds.Should().BeEquivalentTo(new[] { 42, 43 });
            StoredModel.SourceTitle.Should().Be("Vinland Saga - Chapter 0001");
            StoredModel.SourceKey.Should().Be("MangaDex");
            StoredModel.ReleaseGuid.Should().Be("guid-1");
            StoredModel.ReleaseInfoJson.Should().Be("{\"Title\":\"Vinland Saga - Chapter 0001\"}");
            StoredModel.Reason.Should().Be("404 image missing");
            StoredModel.Source.Should().Be("ImageDownload");
        }

        [Test]
        public void BlocklistedByTitle_returns_only_rows_for_specified_manga()
        {
            var matching = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 1)
                .With(b => b.SourceTitle = "Manga A - Chapter 001")
                .With(b => b.ChapterIds = new List<int> { 10 })
                .BuildNew();
            var otherManga = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 2)
                .With(b => b.SourceTitle = "Manga B - Chapter 001")
                .With(b => b.ChapterIds = new List<int> { 20 })
                .BuildNew();

            Subject.Insert(matching);
            Subject.Insert(otherManga);

            var result = Subject.BlocklistedByTitle(1, "Manga A - Chapter 001");

            result.Should().HaveCount(1);
            result.Single().MangaId.Should().Be(1);
        }

        [Test]
        public void BlocklistedByReleaseGuid_filters_by_manga_and_guid()
        {
            var matching = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 1)
                .With(b => b.ReleaseGuid = "guid-a")
                .With(b => b.ChapterIds = new List<int> { 10 })
                .BuildNew();
            var sameMangaDifferentGuid = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 1)
                .With(b => b.ReleaseGuid = "guid-b")
                .With(b => b.ChapterIds = new List<int> { 11 })
                .BuildNew();
            var differentManga = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 2)
                .With(b => b.ReleaseGuid = "guid-a")
                .With(b => b.ChapterIds = new List<int> { 20 })
                .BuildNew();

            Subject.Insert(matching);
            Subject.Insert(sameMangaDifferentGuid);
            Subject.Insert(differentManga);

            var result = Subject.BlocklistedByReleaseGuid(1, "guid-a");

            result.Should().HaveCount(1);
            result.Single().MangaId.Should().Be(1);
            result.Single().ReleaseGuid.Should().Be("guid-a");
        }

        [Test]
        public void DeleteForManga_cascades_only_for_specified_manga()
        {
            var keep = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 1)
                .With(b => b.ChapterIds = new List<int> { 10 })
                .BuildNew();
            var delete1 = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 2)
                .With(b => b.ChapterIds = new List<int> { 20 })
                .BuildNew();
            var delete2 = Builder<MangaBlocklist>.CreateNew()
                .With(b => b.MangaId = 2)
                .With(b => b.ChapterIds = new List<int> { 21 })
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
