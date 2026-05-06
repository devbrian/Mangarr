using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;
using MangaRepository = NzbDrone.Core.Manga.MangaRepository;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 1 live fixture for IMangaRepository (CRUD + finders by external IDs).
    // Plan 02-03 lands MangaRepository / IMangaRepository.
    [TestFixture]
    public class MangaRepositoryFixture : DbTest<MangaRepository, MangaModel>
    {
        private MangaModel BuildManga(string title = "Naruto", Guid? mangaDexId = null, int? malId = null, int? aniListId = null)
        {
            return new MangaModel
            {
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                SortTitle = title.ToLowerInvariant(),
                Status = MangaStatusType.Ongoing,
                Path = $"C:\\manga\\{title}",
                Monitored = true,
                Added = DateTime.UtcNow,
                MangaDexId = mangaDexId,
                MalId = malId,
                AniListId = aniListId,
            };
        }

        [Test]
        public void Insert_returns_id()
        {
            var manga = BuildManga();
            Subject.Insert(manga);
            manga.Id.Should().BeGreaterThan(0);
        }

        [Test]
        public void Get_by_id_returns_inserted()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            var fetched = Subject.Get(manga.Id);

            fetched.Should().NotBeNull();
            fetched.Title.Should().Be("Naruto");
            fetched.CleanTitle.Should().Be("naruto");
        }

        [Test]
        public void FindByMangaDexId_returns_match()
        {
            var mangaDexId = Guid.NewGuid();
            Subject.Insert(BuildManga("Naruto", mangaDexId: mangaDexId));
            Subject.Insert(BuildManga("Bleach", mangaDexId: Guid.NewGuid()));

            var found = Subject.FindByMangaDexId(mangaDexId);

            found.Should().NotBeNull();
            found.Title.Should().Be("Naruto");
        }

        [Test]
        public void FindByMalId_returns_match()
        {
            Subject.Insert(BuildManga("Naruto", malId: 11));
            Subject.Insert(BuildManga("Bleach", malId: 22));

            var found = Subject.FindByMalId(22);

            found.Should().NotBeNull();
            found.Title.Should().Be("Bleach");
        }

        [Test]
        public void FindByAniListId_returns_match()
        {
            Subject.Insert(BuildManga("Naruto", aniListId: 111));
            Subject.Insert(BuildManga("Bleach", aniListId: 222));

            var found = Subject.FindByAniListId(111);

            found.Should().NotBeNull();
            found.Title.Should().Be("Naruto");
        }

        [Test]
        public void Update_persists_changes()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            manga.Status = MangaStatusType.Completed;
            manga.Overview = "Story of a ninja";
            Subject.Update(manga);

            var fetched = Subject.Get(manga.Id);
            fetched.Status.Should().Be("completed");
            fetched.Overview.Should().Be("Story of a ninja");
        }

        [Test]
        public void Delete_removes_row()
        {
            var manga = BuildManga();
            Subject.Insert(manga);

            Subject.Delete(manga.Id);

            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void All_returns_all()
        {
            Subject.Insert(BuildManga("Naruto"));
            Subject.Insert(BuildManga("Bleach"));

            Subject.All().ToList().Should().HaveCount(2);
        }
    }
}
