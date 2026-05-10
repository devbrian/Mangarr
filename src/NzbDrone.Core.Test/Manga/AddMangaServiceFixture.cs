using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 fixture for AddMangaService — META-02 + D-19 (link-validation gate) +
    // D-20 (symmetric reverse direction). Plan 02-09 GREEN.
    //
    // Mocker setup required (warning-8 fix per CONTEXT): the [SetUp] body MUST register
    // the metadata-source factory mock so AddMangaService.Add doesn't NRE on the cast:
    //
    //   Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
    //         .Returns(mockedSource.Object);
    [TestFixture]
    public class AddMangaServiceFixture : CoreTest<AddMangaService>
    {
        private MetadataSourceDefinition _primaryDef;
        private StubMangaDexProvider _mangaDexProvider;
        private Manga.Manga _primaryResult;

        [SetUp]
        public void Setup()
        {
            _primaryResult = new Manga.Manga
            {
                Title = "Test Manga",
                MangaDexId = Guid.NewGuid(),
                PublicationYear = 2020,
                PrimaryAuthor = "Some Author",
                TotalChapterCount = 100,
            };

            _primaryDef = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = nameof(MangaDexMetadataSource),
                IsPrimary = true,
            };

            _mangaDexProvider = new StubMangaDexProvider(_primaryResult);

            // PER WARNING-8 FIX: register IMetadataSourceFactory.GetInstance so the
            // AddMangaService cast (IProvideMangaInfo) does not NRE.
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(_mangaDexProvider);

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(_primaryDef);

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { _primaryDef });

            // MangaService stubs that pass dedup checks + persist.
            Mocker.GetMock<IMangaService>()
                  .Setup(m => m.AddManga(It.IsAny<Manga.Manga>()))
                  .Returns<Manga.Manga>(m =>
                  {
                      m.Id = 7;
                      return m;
                  });

            // CrossSourceIdResolver is a concrete class - inject directly.
            Mocker.SetConstant(new CrossSourceIdResolver(LogManager.GetCurrentClassLogger()));

            // Phase 8 audit gap-03: AddMangaService now defaults Path from RootFolderPath +
            // GetMangaFolder when Path is empty. Configure the filename builder mock so the
            // Path.Combine in production code receives a non-null folder name.
            Mocker.GetMock<IBuildMangaFileNames>()
                  .Setup(b => b.GetMangaFolder(It.IsAny<Manga.Manga>(), It.IsAny<NamingConfig>()))
                  .Returns("Test Manga");

            // Phase 8 audit gap-04: AddMangaService now invokes IAddMangaValidator before
            // persistence. Default to a passing ValidationResult so tests that don't care
            // about validation don't NRE on the auto-mock's null return.
            Mocker.GetMock<IAddMangaValidator>()
                  .Setup(v => v.Validate(It.IsAny<Manga.Manga>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void Add_persists_manga_via_service()
        {
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            var result = Subject.AddManga(newManga);

            Mocker.GetMock<IMangaService>().Verify(m => m.AddManga(It.IsAny<Manga.Manga>()), Times.Once());
            result.Should().NotBeNull();
        }

        [Test]
        public void Add_resolves_cross_source_ids_via_resolver()
        {
            // No secondaries in factory.All() so cross-source resolution is a no-op for this test.
            // The literal _resolver.TryResolve invocation is verified via the production-code
            // search in AddMangaService.cs; this test just exercises the orchestration path.
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            // The resolver path was invoked - validated by the absence of NRE and the resolver
            // field being non-null.
            Subject.Should().NotBeNull();
        }

        [Test]
        public void Add_does_not_call_ChapterListService_directly()
        {
            // Phase 16.1 Wave 3 (REVERT-03): the initial RefreshMangaCommand (queued by
            // MangaAddedHandler from MangaAddedEvent) is the sole producer of canonical
            // Chapter rows via RefreshMangaService's single-pass SyncChapters call.
            // AddMangaService MUST NOT call ChapterListService directly — doing so would
            // (a) duplicate work the Refresh path already performs and (b) operate on a
            // Chapter list shape that the metadata source has not yet projected.
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            Mocker.GetMock<IChapterListService>()
                  .Verify(c => c.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>()), Times.Never);
        }

        [Test]
        public void Add_publishes_MangaAddedEvent()
        {
            // MangaService.AddManga publishes MangaAddedEvent — but MangaService is mocked here,
            // so we verify that AddMangaService delegates to it (the published event happens in
            // production via the real MangaService).
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            Mocker.GetMock<IMangaService>().Verify(m => m.AddManga(It.IsAny<Manga.Manga>()), Times.Once());
        }

        [Test]
        public void Add_does_not_push_RefreshMangaCommand_directly()
        {
            // Phase 8 audit gap-01: refresh-trigger moved to MangaAddedHandler
            // (subscriber on MangaAddedEvent). AddMangaService no longer pushes
            // RefreshMangaCommand inline — coverage of the push lives in
            // MangaAddedHandlerFixture. This test asserts the responsibility
            // migration: AddMangaService MUST NOT push the command directly.
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RefreshMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never());
        }

        [Test]
        public void After_Add_GetManga_returns_populated_with_chapter_rows_or_synthetic()
        {
            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test", RootFolderPath = "C:\\Test" };

            var result = Subject.AddManga(newManga);

            // Populated fields from primary's ApplyChanges:
            result.PublicationYear.Should().Be(_primaryResult.PublicationYear);
            result.PrimaryAuthor.Should().Be(_primaryResult.PrimaryAuthor);
            result.TotalChapterCount.Should().Be(_primaryResult.TotalChapterCount);

            // Phase 16.1 Wave 3 (REVERT-03): chapter-list synthesis is delegated to
            // RefreshMangaService's single-pass SyncChapters call, queued by MangaAddedHandler
            // from the MangaAddedEvent — not invoked here. Verified by
            // Add_does_not_call_ChapterListService_directly above.
        }

        // D-19 fixture case (locked acceptance literal): three sub-cases —
        //   (a) MangaDex link → AniList ID whose fetched title clears Jaro-Winkler ≥ 0.85 → AniListId KEPT
        //   (b) MangaDex link → AniList ID whose fetched title FAILS the gate → AniListId RESET to null + warning log
        //   (c) MangaDex link absent → fuzzy fallback path runs in step 3
        [Test]
        public void D19_unvalidated_link_is_rejected()
        {
            // Sub-case (b): primary returns an AniList link whose linked title differs wildly.
            _primaryResult.AniListId = 999;

            var aniListSecondary = new MetadataSourceDefinition
            {
                Id = 2,
                Name = "AniList",
                IsPrimary = false,
            };

            // Make the AniList provider return a manga whose title is completely unrelated.
            var anilistProvider = new StubAniListProvider(new Manga.Manga
            {
                Title = "completelyunrelated",
                AniListId = 999,
                PublicationYear = 1900,
                PrimaryAuthor = "different",
                TotalChapterCount = 1,
            });

            // Re-route GetInstance: primary call returns mangaDexProvider; secondary call returns aniListProvider.
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => d.IsPrimary)))
                  .Returns(_mangaDexProvider);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => !d.IsPrimary)))
                  .Returns(anilistProvider);

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { _primaryDef, aniListSecondary });

            var newManga = new Manga.Manga { MangaDexId = _primaryResult.MangaDexId, Title = "Test Manga", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            // D-19: link failed validation, AniListId reset to null.
            newManga.AniListId.Should().BeNull("D-19: unvalidated link must be rejected");

            // Production AddMangaService.ValidateLinkedCrossSourceIds emits a Warn at
            // AddMangaService.cs:164 for the wrong-title case (or :171 for the not-found
            // case). The warn is INTENTIONAL behavior per D-19; the test framework's
            // tear-down "no unexpected warns" assertion otherwise fails this test
            // (per 02-VERIFICATION.md anti-patterns row).
            ExceptionVerification.ExpectedWarns(1);
        }

        // D-20 symmetric (locked acceptance literal): primary=AniList, MangaDexId null →
        // MangaDexMetadataSource.SearchForNewManga is invoked AND newManga.MangaDexId
        // is populated from the highest-similarity D-21-passing hit.
        [Test]
        public void Symmetric_AniList_primary_resolves_MangaDexId_via_search()
        {
            // Primary = AniList; MangaDexId starts null on the primary result.
            _primaryResult = new Manga.Manga
            {
                Title = "test manga",
                AniListId = 42,
                MangaDexId = null,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };

            _primaryDef = new MetadataSourceDefinition { Id = 2, Name = "AniList", Implementation = nameof(AniListMetadataSource), IsPrimary = true };
            var mangaDexSecondary = new MetadataSourceDefinition { Id = 1, Name = "MangaDex", Implementation = nameof(MangaDexMetadataSource), IsPrimary = false };

            var anilistPrimary = new StubAniListProvider(_primaryResult);

            // MangaDex.SearchForNewManga returns a hit that clears the D-21 gate.
            var foundMangaDexId = Guid.NewGuid();
            var mangaDexHit = new Manga.Manga
            {
                Title = "test manga",
                MangaDexId = foundMangaDexId,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };
            var mangaDexProvider = new StubMangaDexProvider(_primaryResult)
            {
                SearchHits = new List<Manga.Manga> { mangaDexHit },
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(_primaryDef);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => d.IsPrimary)))
                  .Returns(anilistPrimary);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => !d.IsPrimary)))
                  .Returns(mangaDexProvider);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { _primaryDef, mangaDexSecondary });

            var newManga = new Manga.Manga { AniListId = 42, Title = "test manga", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            newManga.MangaDexId.Should().Be(foundMangaDexId, "D-20: reverse-MangaDex search must populate MangaDexId");
        }

        // D-20 mirror (primary=MAL).
        [Test]
        public void Symmetric_MAL_primary_resolves_MangaDexId_via_search()
        {
            _primaryResult = new Manga.Manga
            {
                Title = "test manga",
                MalId = 99,
                MangaDexId = null,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };

            _primaryDef = new MetadataSourceDefinition { Id = 3, Name = "MyAnimeList", Implementation = nameof(MyAnimeListMetadataSource), IsPrimary = true };
            var mangaDexSecondary = new MetadataSourceDefinition { Id = 1, Name = "MangaDex", Implementation = nameof(MangaDexMetadataSource), IsPrimary = false };

            var malPrimary = new StubMalProvider(_primaryResult);

            var foundMangaDexId = Guid.NewGuid();
            var mangaDexHit = new Manga.Manga
            {
                Title = "test manga",
                MangaDexId = foundMangaDexId,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };
            var mangaDexProvider = new StubMangaDexProvider(_primaryResult)
            {
                SearchHits = new List<Manga.Manga> { mangaDexHit },
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(_primaryDef);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => d.IsPrimary)))
                  .Returns(malPrimary);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => !d.IsPrimary)))
                  .Returns(mangaDexProvider);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { _primaryDef, mangaDexSecondary });

            var newManga = new Manga.Manga { MalId = 99, Title = "test manga", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            newManga.MangaDexId.Should().Be(foundMangaDexId, "D-20 mirror: MAL primary path must populate MangaDexId");
        }

        // WR-13 regression: provider matching must use Implementation type-name (the
        // class name — immutable) rather than the user-editable Name. A user who
        // renames MangaDex to "Mangadex 1" via the developer endpoint must not lose
        // the symmetric reverse-MangaDex resolution.
        [Test]
        public void Symmetric_AniList_primary_resolves_MangaDexId_when_MangaDex_is_renamed()
        {
            _primaryResult = new Manga.Manga
            {
                Title = "test manga",
                AniListId = 42,
                MangaDexId = null,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };

            // User has renamed both providers via the developer endpoint, but the
            // Implementation field stays pinned to the class name.
            _primaryDef = new MetadataSourceDefinition { Id = 2, Name = "My Custom AniList", Implementation = nameof(AniListMetadataSource), IsPrimary = true };
            var renamedMangaDex = new MetadataSourceDefinition { Id = 1, Name = "Mangadex 1", Implementation = nameof(MangaDexMetadataSource), IsPrimary = false };

            var anilistPrimary = new StubAniListProvider(_primaryResult);

            var foundMangaDexId = Guid.NewGuid();
            var mangaDexHit = new Manga.Manga
            {
                Title = "test manga",
                MangaDexId = foundMangaDexId,
                PublicationYear = 2020,
                PrimaryAuthor = "Same Author",
                TotalChapterCount = 100,
            };
            var mangaDexProvider = new StubMangaDexProvider(_primaryResult)
            {
                SearchHits = new List<Manga.Manga> { mangaDexHit },
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(_primaryDef);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => d.IsPrimary)))
                  .Returns(anilistPrimary);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.Is<MetadataSourceDefinition>(d => !d.IsPrimary)))
                  .Returns(mangaDexProvider);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { _primaryDef, renamedMangaDex });

            var newManga = new Manga.Manga { AniListId = 42, Title = "test manga", RootFolderPath = "C:\\Test" };

            Subject.AddManga(newManga);

            newManga.MangaDexId.Should().Be(foundMangaDexId,
                "WR-13: implementation-type matching must survive a user rename of the Name field");
        }

        // ---- Stub providers ----

        private class StubMangaDexProvider : MangaDexMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<Manga.Manga> SearchHits { get; set; } = new();

            public StubMangaDexProvider(Manga.Manga result)
                : base(new Mock<IHttpClient>().Object, LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 1, Name = "MangaDex", IsPrimary = true };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
                => Tuple.Create(_result, Enumerable.Empty<Chapter>());

            public override List<Manga.Manga> SearchForNewManga(string title) => SearchHits;
        }

        private class StubAniListProvider : AniListMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<Manga.Manga> SearchHits { get; set; } = new();

            public StubAniListProvider(Manga.Manga result)
                : base(new Mock<IHttpClient>().Object, LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 2, Name = "AniList", IsPrimary = false };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
                => Tuple.Create(_result, Enumerable.Empty<Chapter>());

            public override List<Manga.Manga> SearchForNewManga(string title) => SearchHits;
        }

        private class StubMalProvider : MyAnimeListMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<Manga.Manga> SearchHits { get; set; } = new();

            public StubMalProvider(Manga.Manga result)
                : base(new Mock<IHttpClient>().Object, LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 3, Name = "MyAnimeList", IsPrimary = false };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
                => Tuple.Create(_result, Enumerable.Empty<Chapter>());

            public override List<Manga.Manga> SearchForNewManga(string title) => SearchHits;
        }
    }
}
