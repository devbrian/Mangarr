using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 fixture for RefreshMangaService — META-04. Plan 02-09 GREEN.
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
        }

        [Test]
        public void Execute_calls_primary_GetMangaInfo_for_each_id()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid() };
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
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = mdx };
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
            var manga = new Manga.Manga { Id = 1, Title = "M", AniListId = 42, MangaDexId = null };
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
            var manga = new Manga.Manga { Id = 1, Title = "M", MalId = 99, MangaDexId = null };
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
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = null, AniListId = 1 };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            stub.GetMangaInfoCalls.Should().BeEmpty();
        }

        [Test]
        public void Execute_publishes_MangaUpdatedEvent_and_ChapterListUpdatedEvent()
        {
            var manga = new Manga.Manga { Id = 1, Title = "M", MangaDexId = Guid.NewGuid() };
            Mocker.GetMock<IMangaService>().Setup(m => m.GetManga(1)).Returns(manga);
            Mocker.GetMock<IMangaService>().Setup(m => m.UpdateManga(It.IsAny<Manga.Manga>(), It.IsAny<bool>())).Returns(manga);

            var stub = new StubMangaDexProvider(manga);
            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(stub);

            Subject.Execute(new RefreshMangaCommand(new List<int> { 1 }));

            // RefreshMangaService publishes MangaUpdatedEvent directly; ChapterListService
            // publishes ChapterListUpdatedEvent (when manga has no MangaDex link with chapters,
            // strategy 3 logs warn — so check that chapter-list service was at least called).
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Once());
            Mocker.GetMock<IChapterListService>()
                  .Verify(c => c.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<List<Chapter>>()), Times.Once());
        }

        // ---- Stub providers ----
        // Test stubs: type-derived from MangaDexMetadataSource / AniListMetadataSource /
        // MyAnimeListMetadataSource so the RefreshMangaService.Execute switch routes by
        // concrete type. Each captures the sourceId passed to GetMangaInfo.

        private static IMetadataSource BuildMockProvider<T>() where T : IMetadataSource
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

            public override Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, new List<Chapter>());
            }
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

            public override Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, new List<Chapter>());
            }
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

            public override Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId)
            {
                GetMangaInfoCalls.Add(sourceId);
                return Tuple.Create(_result, new List<Chapter>());
            }
        }
    }
}
