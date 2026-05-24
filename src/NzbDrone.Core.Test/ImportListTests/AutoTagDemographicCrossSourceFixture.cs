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
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 31 Plan 31-01 Task 1 (TDD RED) — verifies the IL2-01 D-01 reframe
    // mechanical contract: when an ImportList item arrives carrying AniListId or
    // MalId only (no MangaDexId), the cross-source resolver at
    // ImportListSyncService.ProcessListItems must invoke the primary metadata
    // source (MangaDex post-D-02) and use the resolved candidate's MangaDexId to
    // mutate the ImportListItemInfo so downstream AddMangaService.PrepareForAdd
    // hits MangaDex.MapManga which populates Demographic + Artist per Phase 24 v1.1.
    //
    // R-8 closure path verified in RESEARCH.md §Item 4 (AddMangaService.cs:236-274
    // — primary.GetMangaInfo + newManga.ApplyChanges copies Demographic into the
    // in-flight POCO). DemographicSpecification then matches the populated value
    // → AutoTagging rule applies the configured tag.
    //
    // This fixture asserts the LINKAGE contract at the cross-source-resolver layer
    // because that's where Plan 31-01's mechanical-soundness claim lives: post-D-02
    // when MangaDex is the sole primary, the resolver always calls MangaDex.SearchForNewManga
    // which returns Demographic-populated candidates → item.MangaDexId is set → the
    // downstream AddManga chain is the verified mechanical-soundness path. Full
    // end-to-end ImportList → AddMangaService → MangaService → AutoTagging assertion
    // requires DryIoc full-DI wiring beyond the unit-fixture scope.
    //
    // Per feedback_verify_ui_state_not_just_rendering, the assertion verifies STATE
    // (item.MangaDexId was MUTATED + the cross-source resolver was INVOKED) — not
    // mere rendering / passthrough.
    //
    // RED → GREEN transition:
    //   * Task 1 (this commit) — fixture compiles; assertions fail because the
    //     IsDeprecated virtual is missing (Task 2) and the schema-filter pipeline
    //     hasn't shipped (Task 3). The cross-source resolver behavior IS already
    //     in place (verified RESEARCH §Item 4), so the RED signal comes from the
    //     mock that asserts MangaDex (not AniList/MAL) is consulted as the primary.
    //   * Task 2 lands IsDeprecated overrides + Migration 005 — Migration005Fixture
    //     goes green; this fixture's primary-source-assertions still depend on the
    //     active primary being MangaDex (which is the v1 default but
    //     post-Migration-005 it's the SOLE survivor).
    //   * Task 3 ships the controller schema-emit filter — this fixture's full
    //     contract surface goes green.
    [TestFixture]
    public class AutoTagDemographicCrossSourceFixture : CoreTest<ImportListSyncService>
    {
        private const string AniListItemTitle = "Cross-Source AniList Item";
        private const string MalItemTitle = "Cross-Source MAL Item";
        private const string ResolvedMangaDexGuid = "11111111-1111-1111-1111-111111111111";
        private const int AniListSeedId = 12345;
        private const int MalSeedId = 67890;

        private ImportListDefinition _defaultDef;
        private Mock<IMetadataSource> _mangaDexPrimary;
        private Manga.Manga _mangaDexCandidateForAniList;
        private Manga.Manga _mangaDexCandidateForMal;

        [SetUp]
        public void Setup()
        {
            _defaultDef = new ImportListDefinition
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
                  .Returns(new List<IMangaImportList> { MakeFakeProvider(_defaultDef) });

            Mocker.GetMock<IImportListFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ImportListDefinition> { _defaultDef });

            Mocker.GetMock<IImportListFactory>()
                  .Setup(f => f.Get(1))
                  .Returns(_defaultDef);

            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.All())
                  .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllMangaDexIds())
                  .Returns(new List<Guid>());

            // The cross-source resolver path inspects existing AniListId/MalId for
            // already-in-library short-circuit (ImportListSyncService.cs:204-214);
            // return empty so the cross-source lookup actually fires.
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllAniListIds())
                  .Returns(new List<int>());

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.AllMalIds())
                  .Returns(new List<int>());

            // Mock candidate carrying full Demographic — what MangaDex.MapManga returns
            // in the real wiring (MangaDexMetadataSource.cs:235-315 — Phase 24 v1.1).
            _mangaDexCandidateForAniList = new Manga.Manga
            {
                MangaDexId = Guid.Parse(ResolvedMangaDexGuid),
                AniListId = AniListSeedId,
                Title = AniListItemTitle,
                Demographic = MangaDemographic.Shonen,
                Artist = "Studio Test (AniList)",
            };

            _mangaDexCandidateForMal = new Manga.Manga
            {
                MangaDexId = Guid.Parse(ResolvedMangaDexGuid),
                MalId = MalSeedId,
                Title = MalItemTitle,
                Demographic = MangaDemographic.Seinen,
                Artist = "Studio Test (MAL)",
            };

            // Post-D-02, MangaDex is the SOLE primary. Mock the factory's GetPrimary
            // to return a MangaDex definition; mock GetInstance to return our typed
            // mock provider. The cross-source resolver in ImportListSyncService
            // queries primary.SearchForNewManga(item.Title) — return our candidate.
            _mangaDexPrimary = new Mock<IMetadataSource>();
            _mangaDexPrimary
                .Setup(p => p.SearchForNewManga(AniListItemTitle))
                .Returns(new List<Manga.Manga> { _mangaDexCandidateForAniList });
            _mangaDexPrimary
                .Setup(p => p.SearchForNewManga(MalItemTitle))
                .Returns(new List<Manga.Manga> { _mangaDexCandidateForMal });

            var mangaDexDefinition = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = "MangaDexMetadataSource",
                ConfigContract = "MangaDexMetadataSourceSettings",
                IsPrimary = true,
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(mangaDexDefinition);

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(_mangaDexPrimary.Object);
        }

        private static IMangaImportList MakeFakeProvider(ImportListDefinition def)
        {
            var mock = new Mock<IMangaImportList>();
            mock.SetupGet(p => p.Definition).Returns(def);
            mock.SetupGet(p => p.Name).Returns(def.Name);
            return mock.Object;
        }

        // ============================================================
        // Test 1 — AniList-only item triggers MangaDex.SearchForNewManga
        //          (post-D-02 sole-primary contract). The resolved MangaDexId
        //          flows into the item so downstream PrepareForAdd hits
        //          MangaDex.MapManga and Demographic + Artist populate.
        // ============================================================
        [Test]
        public void anilist_only_item_cross_resolves_through_mangadex_primary()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo
                {
                    ImportListId = _defaultDef.Id,
                    Title = AniListItemTitle,
                    AniListId = AniListSeedId,
                }
            };

            var fetchResult = new ImportListFetchResult { Manga = items };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(fetchResult);

            Subject.Execute(new ImportListSyncCommand());

            // Phase 31 D-01 mechanical-soundness assertion: MangaDex (sole primary
            // post-D-02) was consulted via SearchForNewManga(item.Title). Once the
            // controller schema-filter (Task 3) hides AniList + MAL from the Add
            // picker, MangaDex is operationally the only primary candidate.
            _mangaDexPrimary.Verify(
                p => p.SearchForNewManga(AniListItemTitle),
                Times.AtLeastOnce,
                "post-D-02 MangaDex (sole primary) MUST be the cross-source resolver target for AniList-only items — closes the IL2-01 Demographic+Artist gap by deprecation");

            // Phase 31 fix-forward (REVIEW.md §WR-04 remediation, 2026-05-24):
            // Replaced the tautological `_mangaDexCandidateForAniList.Demographic
            // .Should().NotBeNull()` + `.Artist.Should().NotBeNullOrEmpty()` pair —
            // those assertions read the test's OWN mock object initialized in
            // SetUp with the asserted values, providing zero production-behavior
            // signal. The load-bearing post-execute state mutation is
            // ImportListSyncService.cs:259 (`item.MangaDexId = match.MangaDexId
            // .ToString()`). Asserting that mutation proves the cross-source
            // resolver wrote the resolved ID back into the in-flight item, which
            // is the actual D-01 mechanical-soundness contract: the downstream
            // AddMangaService.PrepareForAdd refetches via primary.GetMangaInfo
            // + newManga.ApplyChanges (RESEARCH §Item 4) keyed on the mutated
            // MangaDexId — Demographic + Artist then populate from the
            // re-fetched MangaDex.MapManga record (Phase 24 v1.1 contract).
            items[0].MangaDexId.Should().Be(ResolvedMangaDexGuid,
                "ImportListSyncService.ProcessListItems must mutate the in-flight item with the resolved MangaDexId so downstream AddMangaService re-fetches via MangaDex.MapManga (where Demographic + Artist populate per Phase 24 v1.1)");
        }

        // ============================================================
        // Test 2 — MAL-only item triggers MangaDex.SearchForNewManga
        // ============================================================
        [Test]
        public void mal_only_item_cross_resolves_through_mangadex_primary()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo
                {
                    ImportListId = _defaultDef.Id,
                    Title = MalItemTitle,
                    MalId = MalSeedId,
                }
            };

            var fetchResult = new ImportListFetchResult { Manga = items };

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(f => f.Fetch())
                  .Returns(fetchResult);

            Subject.Execute(new ImportListSyncCommand());

            _mangaDexPrimary.Verify(
                p => p.SearchForNewManga(MalItemTitle),
                Times.AtLeastOnce,
                "post-D-02 MangaDex (sole primary) MUST be the cross-source resolver target for MAL-only items — closes the IL2-01 Demographic+Artist gap by deprecation");

            // Phase 31 fix-forward (REVIEW.md §WR-04 remediation, 2026-05-24):
            // Same tautological-assertion replacement as Test 1. The MAL-only
            // path runs through the identical ImportListSyncService.cs:259
            // mutation surface — assert the post-execute MangaDexId, which is
            // the actual D-01 mechanical-soundness contract.
            items[0].MangaDexId.Should().Be(ResolvedMangaDexGuid,
                "ImportListSyncService.ProcessListItems must mutate the in-flight item with the resolved MangaDexId so downstream AddMangaService re-fetches via MangaDex.MapManga (where Demographic + Artist populate per Phase 24 v1.1)");
        }

        // ============================================================
        // Test 3 — RED signal: post-Phase-31 the FACTORY must yield MangaDex as
        // the SOLE primary (AniList + MAL deprecated and filtered from Add picker).
        // Asserts the IsDeprecated contract via the MetadataSourceBase virtual.
        // Uses reflection so the fixture compiles BEFORE the virtual ships in Task 2.
        // ============================================================
        [Test]
        public void post_phase_31_only_mangadex_is_a_non_deprecated_primary_candidate()
        {
            var mangaDex = Activate<NzbDrone.Core.MetadataSource.MangaDex.MangaDexMetadataSource>();
            var aniList = Activate<NzbDrone.Core.MetadataSource.AniList.AniListMetadataSource>();
            var mal = Activate<NzbDrone.Core.MetadataSource.MyAnimeList.MyAnimeListMetadataSource>();

            ReadIsDeprecated(mangaDex).Should().BeFalse(
                "MangaDex stays as the user-facing primary post-D-01 reframe");
            ReadIsDeprecated(aniList).Should().BeTrue(
                "AniList is deprecated per D-02 — hidden from Add picker");
            ReadIsDeprecated(mal).Should().BeTrue(
                "MAL is deprecated per D-02 — hidden from Add picker");

            // Post-filter, the only non-deprecated primary candidate is MangaDex.
            var providers = new IMetadataSource[] { mangaDex, aniList, mal };
            var nonDeprecated = providers
                .Where(p => !ReadIsDeprecated(p))
                .Select(p => p.GetType().Name)
                .ToList();

            nonDeprecated.Should().BeEquivalentTo(new[] { "MangaDexMetadataSource" },
                "Phase 31 D-01 reframe: MangaDex is the sole non-deprecated primary metadata source candidate");
        }

        // ============================================================
        // Helper — reflection-only IsDeprecated read. Returns false when the
        // property doesn't exist yet (pre-Task-2 RED state) — which makes the
        // "deprecated" assertions FAIL until the virtual + overrides ship.
        // ============================================================
        private static bool ReadIsDeprecated(IMetadataSource provider)
        {
            var prop = provider.GetType().GetProperty("IsDeprecated");
            return prop != null && (bool)(prop.GetValue(provider) ?? false);
        }

        private static T Activate<T>()
            where T : class
        {
            return (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
    }
}
