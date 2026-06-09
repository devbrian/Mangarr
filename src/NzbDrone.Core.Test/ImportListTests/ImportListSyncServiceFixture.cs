using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 26 Plan 26-04 (IL-03) — ImportListSyncService.Execute dispatch + exclusion +
    // already-in-library filter behavior. Each test names the exact behavior under
    // assertion per feedback_verify_ui_state_not_just_rendering.
    //
    // No live HTTP / no live IFetchAndParseImportList provider — every collaborator is
    // Moq-stubbed; the focus is the orchestrator's flow control + exclusion + skip
    // logic against the manga-ID triplet.
    [TestFixture]
    public class ImportListSyncServiceFixture : CoreTest<ImportListSyncService>
    {
        private const string TripletA = "11111111-1111-1111-1111-111111111111";
        private const string TripletB = "22222222-2222-2222-2222-222222222222";
        private const string TripletC = "33333333-3333-3333-3333-333333333333";

        [SetUp]
        public void Setup()
        {
            // Default: factory has one enabled definition (id=1) so SyncAll doesn't short-circuit.
            var defaultDef = new ImportListDefinition
            {
                Id = 1,
                Name = "TestList",
                EnableAutomaticAdd = true,
                Implementation = "TestImportList",
                ConfigContract = "TestImportListSettings",
                RootFolderPath = @"C:\Manga",
                TranslationProfileId = 1,
                CustomFormatProfileId = 1,
                ShouldMonitor = MonitorTypes.All
            };

            Mocker.GetMock<IImportListFactory>()
                  .Setup(f => f.AutomaticAddEnabled(It.IsAny<bool>()))
                  .Returns(new List<IMangaImportList>
                  {
                      // GetAvailableProviders contract — for SyncAll's empty-check we need
                      // a non-empty list; provider Definition is what matters downstream.
                      MakeFakeProvider(defaultDef)
                  });

            Mocker.GetMock<IImportListFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ImportListDefinition> { defaultDef });

            Mocker.GetMock<IImportListFactory>()
                  .Setup(f => f.Get(1))
                  .Returns(defaultDef);

            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.All())
                  .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllMangaDexIds())
                  .Returns(new List<Guid>());

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult());

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.FetchSingleList(It.IsAny<ImportListDefinition>()))
                  .Returns(new ImportListFetchResult());
        }

        private static IMangaImportList MakeFakeProvider(ImportListDefinition def)
        {
            var mock = new Mock<IMangaImportList>();
            mock.SetupGet(p => p.Definition).Returns(def);
            mock.SetupGet(p => p.Name).Returns(def.Name);
            return mock.Object;
        }

        [Test]
        public void execute_with_definition_id_calls_sync_list()
        {
            // execute with DefinitionId set → SyncList path → FetchSingleList consulted, Fetch not.
            Subject.Execute(new ImportListSyncCommand(1));

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Verify(
                      f => f.FetchSingleList(It.Is<ImportListDefinition>(d => d.Id == 1)),
                      Times.Once,
                      "DefinitionId.HasValue dispatch routes through FetchSingleList");
            Mocker.GetMock<IFetchAndParseImportList>()
                  .Verify(
                      f => f.Fetch(),
                      Times.Never,
                      "single-list dispatch must NOT touch the full SyncAll Fetch path");
        }

        [Test]
        public void execute_without_definition_id_calls_sync_all()
        {
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Verify(
                      f => f.Fetch(),
                      Times.Once,
                      "DefinitionId-null dispatch routes through SyncAll → Fetch");
        }

        [Test]
        public void process_list_items_filters_via_exclusion_join()
        {
            // 3 items, MangaDexId in {A, B, C}. Exclusion list contains MangaDexId=A.
            // Expected: AddManga called with [B, C] (2 items).
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "A", MangaDexId = TripletA },
                new ImportListItemInfo { ImportListId = 1, Title = "B", MangaDexId = TripletB },
                new ImportListItemInfo { ImportListId = 1, Title = "C", MangaDexId = TripletC }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.All())
                  .Returns(new List<ImportListExclusion>
                  {
                      new ImportListExclusion { MangaDexId = TripletA, Title = "A" }
                  });

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull("AddMangaService.AddManga was called by SyncAll → ProcessListItems");
            captured.Should().HaveCount(2, "exclusion row matching TripletA filters that item out");
            captured.Select(m => m.MangaDexId?.ToString()).Should().BeEquivalentTo(new[] { TripletB, TripletC });
        }

        [Test]
        public void process_list_items_filters_via_already_in_library()
        {
            // 3 items in feed; AllMangaDexIds() returns 1 of them as already-in-library.
            // Expected: AddManga called with 2 items.
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "A", MangaDexId = TripletA },
                new ImportListItemInfo { ImportListId = 1, Title = "B", MangaDexId = TripletB },
                new ImportListItemInfo { ImportListId = 1, Title = "C", MangaDexId = TripletC }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllMangaDexIds())
                  .Returns(new List<Guid> { Guid.Parse(TripletB) });

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull();
            captured.Should().HaveCount(2,
                "TripletB already-in-library short-circuits the auto-add fan-out");
            captured.Select(m => m.MangaDexId?.ToString()).Should().NotContain(TripletB);
        }

        [Test]
        public void process_list_items_skips_when_MangaDexId_is_null()
        {
            // Phase 26 ships zero providers; AniList-only / MAL-only items are skipped
            // because cross-source resolution (AniList → MangaDexId) lives in Phase 27.
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "AniListOnly", AniListId = 42 },
                new ImportListItemInfo { ImportListId = 1, Title = "MalOnly", MalId = 99 },
                new ImportListItemInfo { ImportListId = 1, Title = "HasDex", MangaDexId = TripletA }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1,
                "with no primary metadata source mocked (-> Unknown -> legacy MangaDexId path), MAL/AniList-only items cannot resolve and are skipped");
            captured[0].MangaDexId?.ToString().Should().Be(TripletA);
        }

        // quick-260608-vf9 follow-up: under a MangaBaka primary (the v1.3 default) a MAL-only
        // import-list item (e.g. from the MyAnimeList Stack list) MUST resolve to the primary's
        // own id and be staged for add — pre-fix it was silently rejected at the hardcoded
        // MangaDexId gate because MangaBaka results never carry a MangaDexId (D-03a).
        private void SetupMangaBakaPrimary(IEnumerable<Manga.Manga> searchResults)
        {
            var def = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaBaka",
                Implementation = nameof(MangaBakaMetadataSource),
                IsPrimary = true
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(def);

            var sourceMock = new Mock<IMetadataSource>();
            sourceMock.Setup(s => s.SearchForNewManga(It.IsAny<string>()))
                      .Returns(searchResults.ToList());

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(sourceMock.Object);
        }

        [Test]
        public void process_list_items_resolves_mal_only_item_to_primary_id_under_mangabaka_primary()
        {
            // Item carries ONLY MalId (the MyAnimeList Stack shape). MangaBaka primary search
            // returns a candidate with the matching MalId AND a MangaBakaId (but NO MangaDexId).
            // Expected: AddManga staged with the candidate's MangaBakaId so PrepareForAdd ->
            // ResolveSourceIdForPrimary(MangaBaka) succeeds.
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "Solo Leveling", MalId = 121496 }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            SetupMangaBakaPrimary(new[]
            {
                new Manga.Manga { Title = "Solo Leveling", MangaBakaId = 555, MalId = 121496, MangaDexId = null }
            });

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1, "the MAL-only item resolves to a MangaBaka candidate carrying MangaBakaId");
            captured[0].MangaBakaId.Should().Be(555, "the primary (MangaBaka) id must be carried so AddManga can resolve the active primary's source id");
            captured[0].MalId.Should().Be(121496);
        }

        [Test]
        public void process_list_items_rejects_when_mangabaka_match_lacks_mangabaka_id()
        {
            // MangaBaka primary returns a MalId-agreeing candidate that has NO MangaBakaId.
            // AddManga would throw "no source ID for active primary", so the item must be
            // rejected (not staged) rather than crashing the sync.
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "Orphan", MalId = 999 }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            SetupMangaBakaPrimary(new[]
            {
                new Manga.Manga { Title = "Orphan", MangaBakaId = null, MalId = 999 }
            });

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull();
            captured.Should().BeEmpty("a candidate with no MangaBakaId cannot be added under a MangaBaka primary");
        }

        [Test]
        public void process_list_items_skips_search_when_mal_item_already_in_library_under_mangabaka_primary()
        {
            // The MAL id is already in the library — the item must short-circuit WITHOUT a
            // primary search (saves an API call per item per sync).
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 1, Title = "Owned", MalId = 121496 }
            };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(new ImportListFetchResult(items, anyFailure: false));

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllMalIds())
                  .Returns(new List<int> { 121496 });

            var def = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaBaka",
                Implementation = nameof(MangaBakaMetadataSource),
                IsPrimary = true
            };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(def);
            var sourceMock = new Mock<IMetadataSource>();
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(sourceMock.Object);

            List<Manga.Manga> captured = null;
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<List<Manga.Manga>>(), It.IsAny<bool>()))
                  .Callback<List<Manga.Manga>, bool>((list, _) => captured = list)
                  .Returns<List<Manga.Manga>, bool>((list, _) => list);

            Subject.Execute(new ImportListSyncCommand());

            captured.Should().NotBeNull();
            captured.Should().BeEmpty("the MAL id is already in the library");
            sourceMock.Verify(
                s => s.SearchForNewManga(It.IsAny<string>()),
                Times.Never,
                "an already-in-library item must not trigger a primary search");
        }
    }
}
