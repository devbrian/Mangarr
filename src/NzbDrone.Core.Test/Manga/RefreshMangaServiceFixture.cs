using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaTests
{
    // Phase 16.1 Wave 3 (REVERT-03): RefreshMangaService fixture reverted by behavior
    // (NOT git restore per CONTEXT.md D-12) from the Phase 16 tuple-feed assertions.
    // Tests now exercise the Sonarr-canonical single-pass SyncChapters call shape.
    // Orthogonal improvements preserved verbatim: gap-12 disk-scan tests, gap-08-01-08
    // chapter snapshot dictionary, gap-12 catch-block disk-scan, user-mutable
    // preservation (issue #28), WR-07 batch-tolerance, WR-08 missing-source-id Warn.
    [TestFixture]
    public class RefreshMangaServiceFixture : CoreTest<RefreshMangaService>
    {
        private MetadataSourceDefinition _primaryDef;

        [SetUp]
        public void Setup()
        {
            _primaryDef = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                IsPrimary = true,
            };

            // Default primary mock = MangaDex. Tests can re-route by re-setting this.
            // PER WARNING-8 FIX: register IMetadataSourceFactory.GetInstance so the
            // RefreshMangaService cast (IProvideMangaInfo) does not NRE.
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(BuildMockProvider<MangaDexMetadataSource>());

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetPrimary())
                  .Returns(_primaryDef);

            // CrossSourceIdResolver is a concrete class — inject a real instance so the
            // auto-relink path (RefreshMangaService.TryRelinkPrimaryId) exercises the
            // genuine Jaro-Winkler + multi-axis gate. Mirrors AddMangaServiceFixture.
            Mocker.SetConstant(new CrossSourceIdResolver(NLog.LogManager.GetCurrentClassLogger()));

            // Phase 8 cluster-01 cascade: production now snapshots chapters via
            // IChapterService.GetChaptersByManga before/after the chapter-sync pass to compute
            // ChapterInfoRefreshedEvent deltas (gap-08-01-08). Default to empty so
            // existing fixtures don't NRE on .ToDictionary() — tests that assert on
            // the delta override this per-test.
            Mocker.GetMock<IChapterService>()
                  .Setup(c => c.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(new List<Chapter>());

            // gap-12 (refresh-also-scan-disk): production now reads
            // IConfigService.RescanAfterRefresh inside the per-manga loop. Default
            // the mock to RescanAfterRefreshType.Always so the disk-scan chain
            // exercises on every test (matches the production default in
            // ConfigService.RescanAfterRefresh getter). Tests that assert on
            // skip-paths override this per-test.
            Mocker.GetMock<IConfigService>()
                  .Setup(c => c.RescanAfterRefresh)
                  .Returns(RescanAfterRefreshType.Always);
        }

        // Phase 8 cluster-01 cascade: production now normalizes Manga.Path on every
        // refresh (gap-08-01-06). Use the OS temp dir as a guaranteed-existing path so
        // DirectoryInfo(...).FullName.GetActualCasing() succeeds without disk side effects.
        private static readonly string TestMangaPath = Path.GetTempPath();

        [Test]
        public void Execute_calls_primary_GetMangaInfo_for_each_id()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            // Set up provider so the cast succeeds AND GetMangaInfo returns a tuple.
            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().HaveCount(1);
            stub.GetMangaInfoCalls[0].Should().Be(manga.MangaDexId.Value.ToString());
        }

        [Test]
        public void Execute_enriches_missing_cross_source_ids_from_full_refresh_record()
        {
            // Manga already linked to the primary (MangaBaka, has mangaBakaId) so NO relink
            // runs — but its other cross-source links are partial (only the original three).
            // The full GetMangaInfo record carries the complete source block; the refresh must
            // fill the missing ids (never overwriting the ones already set).
            var bakaPrimary = new MetadataSourceDefinition { Id = 4, Name = "MangaBaka", IsPrimary = true };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(bakaPrimary);

            var existing = new Manga.Manga
            {
                Id = 1,
                Title = "Solo Leveling",
                MangaBakaId = 3397,
                MangaDexId = Guid.NewGuid(),
                KitsuId = null,
                MangaUpdatesId = null,
                Path = TestMangaPath,
            };
            var originalDexId = existing.MangaDexId;
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(existing);
            Mocker.GetMock<IMangaService>()
                  .Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>()))
                  .Returns<Manga.Manga, bool>((m, _) => m);

            // The authoritative by-id record carries the full source block, incl. a DIFFERENT
            // MangaDexId that must NOT clobber the existing one (fill-null only).
            var fullRecord = new Manga.Manga
            {
                Title = "Solo Leveling",
                MangaBakaId = 3397,
                MangaDexId = Guid.NewGuid(),
                KitsuId = 55555,
                MangaUpdatesId = "abc123",
            };
            var stub = new StubMangaBakaProvider(null, fullRecord);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            existing.KitsuId.Should().Be(55555, "missing id filled from the full record");
            existing.MangaUpdatesId.Should().Be("abc123", "missing id filled from the full record");
            existing.MangaDexId.Should().Be(originalDexId, "an already-set id is never overwritten");
            stub.GetMangaInfoCalls.Should().Contain("3397");
        }

        [Test]
        public void Execute_uses_MangaDexId_when_primary_is_MangaDex()
        {
            var mdx = Guid.NewGuid();
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = mdx, Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().Contain(mdx.ToString());
        }

        [Test]
        public void Execute_uses_AniListId_when_primary_is_AniList()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", AniListId = 42, MangaDexId = null, Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            _primaryDef = new MetadataSourceDefinition { Id = 2, Name = "AniList", IsPrimary = true };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(_primaryDef);

            var stub = new StubAniListProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().Contain("42");
        }

        [Test]
        public void Execute_uses_MalId_when_primary_is_MAL()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MalId = 99, MangaDexId = null, Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            _primaryDef = new MetadataSourceDefinition { Id = 3, Name = "MyAnimeList", IsPrimary = true };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(_primaryDef);

            var stub = new StubMalProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().Contain("99");
        }

        [Test]
        public void Execute_skips_manga_with_no_source_id_for_active_primary()
        {
            // Primary = MangaDex but manga has no MangaDexId
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = null, AniListId = 1, Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().BeEmpty();

            // WR-08: the skip is now logged at Warn so users see they need to relink.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Execute_auto_relinks_manga_to_new_primary_when_id_missing_and_title_matches()
        {
            // A manga added under MangaDex (has MangaDexId, no MangaBakaId) after the user
            // promoted MangaBaka to primary. The active primary has no source id for it, so
            // RefreshMangaService must auto-resolve the MangaBakaId by a confirmed title
            // match and proceed with the refresh instead of skipping.
            var bakaPrimary = new MetadataSourceDefinition { Id = 4, Name = "MangaBaka", IsPrimary = true };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(bakaPrimary);

            var existing = new Manga.Manga
            {
                Id = 1,
                Title = "Dungeons and Crayons",
                MangaDexId = Guid.NewGuid(),
                MangaBakaId = null,
                PublicationYear = 2020,
                PrimaryAuthor = "Some Author",
                TotalChapterCount = 50,
                Path = TestMangaPath,
            };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(existing);
            Mocker.GetMock<IMangaService>()
                  .Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>()))
                  .Returns<Manga.Manga, bool>((m, _) => m);

            // The MangaBaka search hit clears the CrossSourceIdResolver gate (identical
            // title + matching year/author/chapter-count → 3/3 axes).
            var hit = new Manga.Manga
            {
                Title = "Dungeons and Crayons",
                MangaBakaId = 999,
                PublicationYear = 2020,
                PrimaryAuthor = "Some Author",
                TotalChapterCount = 50,
            };
            var refreshed = new Manga.Manga { Id = 1, Title = "Dungeons and Crayons", MangaBakaId = 999 };
            var stub = new StubMangaBakaProvider(hit, refreshed);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            // The resolved MangaBakaId was persisted (relink) and the refresh proceeded
            // through GetMangaInfo with that id — no skip, no warn.
            existing.MangaBakaId.Should().Be(999);
            existing.MangaDexId.Should().NotBeNull("the original MangaDex link is preserved as a fallback");
            Mocker.GetMock<IMangaService>()
                  .Verify(m => m.UpdateManga(It.Is<Manga.Manga>(x => x.MangaBakaId == 999), false), Times.AtLeastOnce());
            stub.GetMangaInfoCalls.Should().Contain("999");
        }

        [Test]
        public void Execute_skips_manga_when_auto_relink_finds_no_confident_match()
        {
            // Same setup as the happy path but the MangaBaka search hit has a wildly
            // different title so the resolver gate fails — the manga is skip-warned and
            // its existing metadata is left untouched (never repointed to a wrong title).
            var bakaPrimary = new MetadataSourceDefinition { Id = 4, Name = "MangaBaka", IsPrimary = true };
            Mocker.GetMock<IMetadataSourceFactory>().Setup(f => f.GetPrimary()).Returns(bakaPrimary);

            var existing = new Manga.Manga
            {
                Id = 1,
                Title = "Dungeons and Crayons",
                MangaDexId = Guid.NewGuid(),
                MangaBakaId = null,
                Path = TestMangaPath,
            };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(existing);

            var hit = new Manga.Manga { Title = "Totally Unrelated Series", MangaBakaId = 999 };
            var stub = new StubMangaBakaProvider(hit, new Manga.Manga { Id = 1 });
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            existing.MangaBakaId.Should().BeNull("no confident match → no relink");
            stub.GetMangaInfoCalls.Should().BeEmpty("the manga was skipped, not refreshed");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Execute_publishes_MangaUpdatedEvent_and_ChapterListUpdatedEvent()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            // Phase 16.1 Wave 3 (REVERT-03) + Pitfall 4: RefreshMangaService publishes
            // MangaUpdatedEvent exactly once and ChapterListUpdatedEvent exactly once per
            // refresh, AFTER the SyncChapters call completes. With an empty feed (stub
            // returns empty IEnumerable<Chapter>) SyncChapters is a no-op but the trailing
            // event still fires.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()), Times.Once());
        }

        // WR-07 regression: when a refresh of one manga throws a non-MangaNotFound
        // exception (HTTP 503, JSON deser error, …), the loop must continue with the
        // next manga rather than abort the entire batch.
        [Test]
        public void Execute_continues_after_one_manga_throws_unexpected_exception()
        {
            var mangaA = new Manga.Manga { Id = 1, Title = "A", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            var mangaB = new Manga.Manga { Id = 2, Title = "B", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };

            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(mangaA);
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(2)).Returns(mangaB);

            var stub = new ThrowOnFirstStubMangaDexProvider(throwOn: mangaA.MangaDexId.Value.ToString(),
                                                            successResult: mangaB);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1, 2 }));

            // Both ids were attempted; the failure on id=1 logged a Warn and the
            // loop moved on to id=2.
            stub.GetMangaInfoCalls.Should().HaveCount(2);
            ExceptionVerification.ExpectedWarns(1);
        }

        // ---- gap-12 (refresh-also-scan-disk) regression coverage ----
        // After successful metadata refresh, RefreshMangaService.Execute must invoke
        // IMangaDiskScanService.Scan(manga) — mirroring the now-deleted Sonarr
        // RefreshSeriesService.RescanSeries chain. Gating on IConfigService.RescanAfterRefresh
        // mirrors TV verbatim.

        [Test]
        public void Execute_calls_disk_scan_after_successful_metadata_refresh()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            // Default RescanAfterRefresh = Always (set in [SetUp]) — disk scan must fire.
            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.Is<Manga.Manga>(m => m.Id == manga.Id)), Times.Once());
        }

        [Test]
        public void Execute_skips_disk_scan_when_RescanAfterRefresh_is_Never()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);
            Mocker.GetMock<IConfigService>().Setup(c => c.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.IsAny<Manga.Manga>()), Times.Never());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.Is<MangaScanSkippedEvent>(
                              x => x.Reason == MangaScanSkippedReason.NeverRescanAfterRefresh)),
                          Times.Once());
        }

        [Test]
        public void Execute_skips_disk_scan_when_RescanAfterRefresh_is_AfterManual_and_trigger_is_scheduled()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);
            Mocker.GetMock<IConfigService>().Setup(c => c.RescanAfterRefresh).Returns(RescanAfterRefreshType.AfterManual);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            // explicit-IDs branch with non-manual trigger reproduces the scheduled path.
            var cmd = new RefreshMangaCommand(new List<int> { 1 }) { Trigger = CommandTrigger.Scheduled };
            Subject.Execute(cmd);

            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.IsAny<Manga.Manga>()), Times.Never());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.Is<MangaScanSkippedEvent>(
                              x => x.Reason == MangaScanSkippedReason.RescanAfterManualRefreshOnly)),
                          Times.Once());
        }

        [Test]
        public void Execute_force_scans_when_IsNewManga_even_if_RescanAfterRefresh_is_Never()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);
            Mocker.GetMock<IConfigService>().Setup(c => c.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            // IsNewManga = true → force-scan regardless of config (post-add lifecycle).
            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }, isNewManga: true));

            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.Is<Manga.Manga>(m => m.Id == manga.Id)), Times.Once());
        }

        [Test]
        public void Execute_still_calls_disk_scan_when_metadata_refresh_throws()
        {
            // gap-12 invariant: even when metadata refresh fails (one bad manga in the
            // batch), the disk-scan side must still run for that manga so local file
            // changes get reconciled. Mirrors TV RefreshSeriesService catch-block calling
            // RescanSeries(...) before continuing the loop.
            var mangaA = new Manga.Manga { Id = 1, Title = "A", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            var mangaB = new Manga.Manga { Id = 2, Title = "B", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };

            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(mangaA);
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(2)).Returns(mangaB);

            var stub = new ThrowOnFirstStubMangaDexProvider(throwOn: mangaA.MangaDexId.Value.ToString(),
                                                            successResult: mangaB);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1, 2 }));

            // Both A (failure path) and B (success path) must have had disk scans triggered.
            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.Is<Manga.Manga>(m => m.Id == 1)), Times.Once());
            Mocker.GetMock<IMangaDiskScanService>()
                  .Verify(s => s.Scan(It.Is<Manga.Manga>(m => m.Id == 2)), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Execute_preserves_user_mutable_fields_across_refresh()
        {
            // issue #28 regression — the metadata-fetch path constructs a fresh Manga
            // from the source response (defaults for MonitorNewItems / TranslationProfileId
            // / CustomFormatProfileId). Without explicit preservation, ApplyChanges
            // clobbers the user's choice on every refresh — and MangaEditedService
            // queues a refresh after every UI single-edit, so without this guard every
            // Save round-trip silently undoes itself.
            var existing = new Manga.Manga
            {
                Id = 1,
                Title = "M",
                MangaDexId = Guid.NewGuid(),
                Path = TestMangaPath,
                Monitored = true,
                MonitorNewItems = MangaMonitorNewItems.None,
                TranslationProfileId = 99,
                CustomFormatProfileId = 42,
                Tags = new HashSet<int> { 7 },
                RootFolderPath = TestMangaPath,
            };

            // The stub returns a DIFFERENT instance carrying metadata defaults
            // (MonitorNewItems = All / TranslationProfileId = null / etc.) — this
            // mirrors what real metadata sources hand back: they don't know the
            // user's choices.
            var fromMetadata = new Manga.Manga
            {
                Id = 1,
                Title = "M (from source)",
                MangaDexId = existing.MangaDexId,
                Path = TestMangaPath,
                Monitored = false,
                MonitorNewItems = MangaMonitorNewItems.All,
                TranslationProfileId = null,
                CustomFormatProfileId = null,
                Tags = new HashSet<int>(),
                RootFolderPath = null,
            };

            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(existing);

            var stub = new StubMangaDexProvider(fromMetadata);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Manga.Manga captured = null;
            Mocker.GetMock<IMangaService>()
                  .Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>()))
                  .Callback<Manga.Manga, bool>((m, _) => captured = m)
                  .Returns(existing);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            captured.Should().NotBeNull("UpdateManga must be invoked on the refresh path");
            captured.MonitorNewItems.Should().Be(MangaMonitorNewItems.None,
                "user-set MonitorNewItems must survive the metadata-fetch ApplyChanges");
            captured.TranslationProfileId.Should().Be(99,
                "user-set TranslationProfileId must survive the metadata-fetch ApplyChanges");
            captured.CustomFormatProfileId.Should().Be(42,
                "user-set CustomFormatProfileId must survive the metadata-fetch ApplyChanges");

            // Sanity: the existing preservation block already covered these — assert
            // we did not regress them while extending the block.
            captured.Monitored.Should().BeTrue("user-set Monitored must survive (existing invariant)");
            captured.Tags.Should().BeEquivalentTo(new[] { 7 }, "user-set Tags must survive (existing invariant)");
        }

        // Phase 16.1 Wave 3 (REVERT-03): single-pass SyncChapters call shape.
        [Test]
        public void Execute_invokes_SyncChapters_once_per_refresh_with_remote_chapter_list()
        {
            // RefreshMangaService must pass the IEnumerable<Chapter> from the metadata source
            // straight through to ChapterListService.SyncChapters — exactly one call per
            // refresh, with the same manga + chapter list.
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var feed = new List<Chapter>
            {
                new() { ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular, Monitored = true },
                new() { ChapterNumber = 2m, Title = "Ch2", ChapterType = ChapterType.Regular, Monitored = true },
            };
            var stub = new StubMangaDexProvider(manga) { Chapters = feed };
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            Mocker.GetMock<IChapterListService>()
                  .Verify(c => c.SyncChapters(It.Is<Manga.Manga>(m => m.Id == 1),
                                              It.IsAny<IEnumerable<Chapter>>()),
                          Times.Once());
        }

        [Test]
        public void Execute_publishes_ChapterListUpdatedEvent_after_SyncChapters_per_Pitfall_4()
        {
            // Pitfall 4: SyncChapters (DB write) FIRST, then ChapterListUpdatedEvent emit.
            // Sequence enforced by call-order assertions on the mocks.
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var stub = new StubMangaDexProvider(manga)
            {
                Chapters = new List<Chapter>
                {
                    new() { ChapterNumber = 1m, Title = "Ch1", ChapterType = ChapterType.Regular },
                },
            };
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            var sequence = new List<string>();
            Mocker.GetMock<IChapterListService>()
                  .Setup(c => c.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>()))
                  .Callback(() => sequence.Add("sync"));
            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<ChapterListUpdatedEvent>()))
                  .Callback(() => sequence.Add("event"));

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            // sync must come BEFORE event (Pitfall 4 — DB write FIRST, event LAST).
            var syncIdx = sequence.IndexOf("sync");
            var eventIdx = sequence.IndexOf("event");
            syncIdx.Should().BeGreaterThanOrEqualTo(0, "sync should have fired");
            eventIdx.Should().BeGreaterThanOrEqualTo(0, "event should have fired");
            syncIdx.Should().BeLessThan(eventIdx, "Pitfall 4: DB write FIRST, event LAST");
        }

        // ---- gh199: MangaRefreshCompleteEvent scope payload ----
        // The trailing MangaRefreshCompleteEvent must carry the actually-refreshed
        // ids on the explicit-IDs branch, and null on the refresh-all branch.
        // Subscribers (MangaAutoTaggingApplier) scope their iteration on this
        // payload to avoid the gh199 RootFolderPath-amplification.

        [Test]
        public void Execute_publishes_MangaRefreshCompleteEvent_with_refreshed_MangaIds_on_explicit_branch()
        {
            // Single-id refresh: published event must carry MangaIds = [1].
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            MangaRefreshCompleteEvent captured = null;
            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<MangaRefreshCompleteEvent>()))
                  .Callback<MangaRefreshCompleteEvent>(evt => captured = evt);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            captured.Should().NotBeNull("MangaRefreshCompleteEvent must be published on explicit-IDs branch");
            captured.MangaIds.Should().NotBeNull("gh199: explicit-IDs branch must carry refreshed scope");
            captured.MangaIds.Should().BeEquivalentTo(new[] { 1 },
                "gh199: published scope must equal the refreshed manga ids");
        }

        [Test]
        public void Execute_publishes_MangaRefreshCompleteEvent_with_null_MangaIds_on_refresh_all_branch()
        {
            // Refresh-all branch (no MangaIds): published event must carry null scope so
            // subscribers preserve their full-library re-eval behavior.
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.AllMangaIds()).Returns(new List<int> { 1 });
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);
            Mocker.GetMock<IShouldRefreshManga>().Setup(s => s.ShouldRefresh(It.IsAny<Manga.Manga>())).Returns(true);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            MangaRefreshCompleteEvent captured = null;
            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<MangaRefreshCompleteEvent>()))
                  .Callback<MangaRefreshCompleteEvent>(evt => captured = evt);

            // Empty MangaIds list triggers the refresh-all branch via isRefreshAll.
            Subject.Execute(new RefreshMangaCommand(new List<int>()));

            captured.Should().NotBeNull("MangaRefreshCompleteEvent must be published on refresh-all branch");
            captured.MangaIds.Should().BeNull(
                "gh199: refresh-all branch must carry null MangaIds to preserve full-library re-eval");
        }

        [Test]
        public void Execute_publishes_MangaRefreshCompleteEvent_with_MangaIds_excluding_no_source_id_skips()
        {
            // gh199 detail: mangas that hit the WR-08 no-source-id skip MUST NOT be in
            // the published MangaIds — their data did NOT change so re-tagging is wasteful.
            var withId = new Manga.Manga { Id = 1, Title = "Has ID", MangaDexId = Guid.NewGuid(), Path = TestMangaPath };
            var withoutId = new Manga.Manga { Id = 2, Title = "No ID", MangaDexId = null, AniListId = 99, Path = TestMangaPath };

            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(withId);
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(2)).Returns(withoutId);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(withId);

            var stub = new StubMangaDexProvider(withId);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            MangaRefreshCompleteEvent captured = null;
            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<MangaRefreshCompleteEvent>()))
                  .Callback<MangaRefreshCompleteEvent>(evt => captured = evt);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1, 2 }));

            captured.Should().NotBeNull();
            captured.MangaIds.Should().BeEquivalentTo(new[] { 1 },
                "gh199: WR-08 skip path must NOT contribute to the refreshed scope");
            ExceptionVerification.ExpectedWarns(1);
        }

        // ---- Stub providers ----
        // Test stubs: type-derived from MangaDexMetadataSource / AniListMetadataSource /
        // MyAnimeListMetadataSource so the RefreshMangaService.Execute switch routes by
        // concrete type. Each captures the sourceId passed to GetMangaInfo.

        private static IMetadataSource BuildMockProvider<T>()
            where T : IMetadataSource
        {
            // Default — returns null until tests override per scenario.
            var m = new Mock<IMetadataSource>();
            return m.Object;
        }

        private class StubMangaDexProvider : MangaDexMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<string> GetMangaInfoCalls { get; } = new();

            public StubMangaDexProvider(Manga.Manga result)
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, NLog.LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 1, Name = "MangaDex", IsPrimary = true };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, (IEnumerable<Chapter>)(Chapters ?? Enumerable.Empty<Chapter>()));
            }

            public IEnumerable<Chapter> Chapters { get; set; }
        }

        private class StubAniListProvider : AniListMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<string> GetMangaInfoCalls { get; } = new();

            public StubAniListProvider(Manga.Manga result)
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, new Mock<NzbDrone.Core.MetadataSource.AniList.IAniListGraphQlTransport>().Object, NLog.LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 2, Name = "AniList", IsPrimary = false };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, (IEnumerable<Chapter>)(Chapters ?? Enumerable.Empty<Chapter>()));
            }

            public IEnumerable<Chapter> Chapters { get; set; }
        }

        private class StubMalProvider : MyAnimeListMetadataSource
        {
            private readonly Manga.Manga _result;
            public List<string> GetMangaInfoCalls { get; } = new();

            public StubMalProvider(Manga.Manga result)
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, NLog.LogManager.GetCurrentClassLogger())
            {
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 3, Name = "MyAnimeList", IsPrimary = false };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, (IEnumerable<Chapter>)(Chapters ?? Enumerable.Empty<Chapter>()));
            }

            public IEnumerable<Chapter> Chapters { get; set; }
        }

        // Auto-relink helper: a MangaBaka primary that returns a single search hit for
        // any title and records both the search terms and the GetMangaInfo source ids it
        // is asked for. Lets the auto-relink test assert that a MangaDex-added manga is
        // repointed at MangaBaka and the refresh proceeds with the resolved id.
        private class StubMangaBakaProvider : MangaBakaMetadataSource
        {
            private readonly Manga.Manga _searchHit;
            private readonly Manga.Manga _result;
            public List<string> SearchCalls { get; } = new();
            public List<string> GetMangaInfoCalls { get; } = new();

            public StubMangaBakaProvider(Manga.Manga searchHit, Manga.Manga result)
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, NLog.LogManager.GetCurrentClassLogger())
            {
                _searchHit = searchHit;
                _result = result;
                Definition = new MetadataSourceDefinition { Id = 4, Name = "MangaBaka", IsPrimary = true };
            }

            public override List<Manga.Manga> SearchForNewManga(string title)
            {
                SearchCalls.Add(title);
                return _searchHit != null ? new List<Manga.Manga> { _searchHit } : new List<Manga.Manga>();
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, (IEnumerable<Chapter>)(Chapters ?? Enumerable.Empty<Chapter>()));
            }

            public IEnumerable<Chapter> Chapters { get; set; }
        }

        // WR-07 helper: throws an arbitrary exception for the first sourceId, then
        // returns success for any other id. Models the "one bad manga" mid-batch
        // scenario covered by Execute_continues_after_one_manga_throws_unexpected_exception.
        private class ThrowOnFirstStubMangaDexProvider : MangaDexMetadataSource
        {
            private readonly string _throwOn;
            private readonly Manga.Manga _successResult;
            public List<string> GetMangaInfoCalls { get; } = new();

            public ThrowOnFirstStubMangaDexProvider(string throwOn, Manga.Manga successResult)
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, NLog.LogManager.GetCurrentClassLogger())
            {
                _throwOn = throwOn;
                _successResult = successResult;
                Definition = new MetadataSourceDefinition { Id = 1, Name = "MangaDex", IsPrimary = true };
            }

            public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                if (sourceId == _throwOn)
                {
                    throw new InvalidOperationException("simulated upstream 503");
                }

                return Tuple.Create(_successResult, Enumerable.Empty<Chapter>());
            }
        }
    }
}
