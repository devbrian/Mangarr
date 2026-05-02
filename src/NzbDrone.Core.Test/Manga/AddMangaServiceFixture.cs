using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold for AddMangaService — META-02 + D-19 (link-validation gate) +
    // D-20 (symmetric reverse direction). RED until Plan 02-09.
    //
    // Mocker setup required (warning-8 fix per CONTEXT): the [SetUp] body MUST register
    // the metadata-source factory mock so AddMangaService.Add doesn't NRE on the cast:
    //
    //   Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
    //         .Returns(mockedSource.Object);
    //
    // Production-shape this fixture will exercise once 02-09 lands:
    //   public class AddMangaServiceFixture : CoreTest<AddMangaService>
    [TestFixture]
    public class AddMangaServiceFixture : CoreTest
    {
        [Test]
        [Ignore("RED — Plan 02-09 lands AddMangaService.")]
        public void Add_persists_manga_via_service() => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 lands AddMangaService + cross-source resolver wiring.")]
        public void Add_resolves_cross_source_ids_via_resolver() => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 lands AddMangaService + ChapterListService.SyncChapters.")]
        public void Add_calls_ChapterListService_SyncChapters() => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 publishes MangaAddedEvent on Add.")]
        public void Add_publishes_MangaAddedEvent() => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 queues RefreshMangaCommand on Add.")]
        public void Add_queues_RefreshMangaCommand() => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — META-02 acceptance: Add returns populated manga (cover/desc/tags/status/age + chapter rows OR IsSynthetic).")]
        public void After_Add_GetManga_returns_populated_with_chapter_rows_or_synthetic()
            => Assert.Inconclusive("Plan 02-09");

        // D-19 fixture case (locked acceptance literal): three sub-cases —
        //   (a) MangaDex link → AniList ID whose fetched title clears Jaro-Winkler ≥ 0.85 → AniListId KEPT
        //   (b) MangaDex link → AniList ID whose fetched title FAILS the gate → AniListId RESET to null + warning log
        //   (c) MangaDex link absent → fuzzy fallback path runs in step 3
        [Test]
        [Ignore("RED — D-19 link-validation gate; Plan 02-09 implements.")]
        public void D19_unvalidated_link_is_rejected() => Assert.Inconclusive("Plan 02-09");

        // D-20 symmetric (locked acceptance literal): primary=AniList, MangaDexId null →
        // MangaDexMetadataSource.SearchForNewManga is invoked AND newManga.MangaDexId
        // is populated from the highest-similarity D-21-passing hit.
        [Test]
        [Ignore("RED — D-20 symmetric reverse-direction resolver; Plan 02-09 implements.")]
        public void Symmetric_AniList_primary_resolves_MangaDexId_via_search()
            => Assert.Inconclusive("Plan 02-09");

        // D-20 mirror (primary=MAL).
        [Test]
        [Ignore("RED — D-20 mirror (MAL primary); Plan 02-09 implements.")]
        public void Symmetric_MAL_primary_resolves_MangaDexId_via_search()
            => Assert.Inconclusive("Plan 02-09");
    }
}
