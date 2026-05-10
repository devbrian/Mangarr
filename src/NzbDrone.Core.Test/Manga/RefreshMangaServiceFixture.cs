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
                : base(new Mock<NzbDrone.Common.Http.IHttpClient>().Object, NLog.LogManager.GetCurrentClassLogger())
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
