using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold for IMangaRepository (CRUD + finders by external IDs).
    // RED until Plan 02-03 lands MangaRepository / IMangaRepository.
    //
    // Production-shape this fixture will exercise once 02-03 lands:
    //   public class MangaRepositoryFixture : DbTest<MangaRepository, Manga>
    [TestFixture]
    public class MangaRepositoryFixture : CoreTest
    {
        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void Insert_returns_id() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void Get_by_id_returns_inserted() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void FindByMangaDexId_returns_match() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void FindByMalId_returns_match() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void FindByAniListId_returns_match() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void Update_persists_changes() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void Delete_removes_row() => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands MangaRepository / IMangaRepository.")]
        public void All_returns_all() => Assert.Inconclusive("Plan 02-03");
    }
}
