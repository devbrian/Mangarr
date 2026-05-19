using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.RootFolders
{
    // Phase 25.1 Plan 25.1-01 — Option B substrate regression net.
    // Pins RootFolderService.AllWithUnmappedFolders against the post-Phase-15
    // canonical filter source (_mangaRepository.AllMangaPaths()) so a future
    // regression to stale Series.Path turns into a red build instead of a
    // silent breakage of the 25.1-02 frontend library-import scan.
    [TestFixture]
    public class RootFolderServiceFixture : CoreTest<RootFolderService>
    {
        private string _rootFolderPath;
        private string _titleAPath;
        private string _titleBPath;
        private RootFolder _rootFolder;

        [SetUp]
        public void Setup()
        {
            _rootFolderPath = @"C:\data\manga".AsOsAgnostic();
            _titleAPath = @"C:\data\manga\Title A".AsOsAgnostic();
            _titleBPath = @"C:\data\manga\Title B".AsOsAgnostic();

            _rootFolder = new RootFolder
            {
                Id = 1,
                Path = _rootFolderPath
            };

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<RootFolder> { _rootFolder });

            // Phase 5 D-13 / Phase 15 Plan 15-10 — MangaFolderFormat replaced
            // SeriesFolderFormat on NamingConfig. Empty string ⇒ subFolderDepth = 0
            // so GetUnmappedFolders walks one level of subfolders only (matches
            // Sonarr-canonical behaviour for unmapped-folder discovery at depth 0).
            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(new NamingConfig { MangaFolderFormat = string.Empty });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_rootFolderPath))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderEmpty(_rootFolderPath))
                  .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetAvailableSpace(_rootFolderPath))
                  .Returns(0L);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetTotalSize(_rootFolderPath))
                  .Returns(0L);
        }

        private void GivenSubfolders(params string[] subfolders)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectories(_rootFolderPath))
                  .Returns(subfolders);
        }

        private void GivenMangaPaths(Dictionary<int, string> paths)
        {
            Mocker.GetMock<IMangaRepository>()
                  .Setup(r => r.AllMangaPaths())
                  .Returns(paths);
        }

        // Pins Option B substrate (Phase 25.1 RESEARCH §5 / PATTERNS finding #6).
        // GIVEN two candidate subfolders under one Root Folder AND one of them is
        // already a registered Manga.Path, WHEN AllWithUnmappedFolders runs, THEN
        // only the un-registered subfolder surfaces as an UnmappedFolder. Regression
        // would surface as Title A filtering through (filter source flipped back to
        // a stale Series-shape collection) — pin closes Threat T-25.1-01 + T-25.1-02
        // per Plan 25.1-01 STRIDE register.
        [Test]
        public void unmapped_folders_filter_against_manga_path()
        {
            GivenSubfolders(_titleAPath, _titleBPath);
            GivenMangaPaths(new Dictionary<int, string> { { 1, _titleAPath } });

            var result = Subject.AllWithUnmappedFolders();

            result.Should().HaveCount(1);
            result[0].UnmappedFolders.Should().HaveCount(1);
            result[0].UnmappedFolders[0].Path.Should().Be(_titleBPath);
        }

        // Defensive companion — confirms the filter is total: every registered
        // Manga.Path is excluded from the UnmappedFolders projection. Guards
        // against a regression where the filter degrades to a partial-match (e.g.,
        // case-insensitive compare flipped to ordinal, or normalization stripped).
        [Test]
        public void unmapped_folders_empty_when_all_subfolders_mapped()
        {
            GivenSubfolders(_titleAPath);
            GivenMangaPaths(new Dictionary<int, string> { { 1, _titleAPath } });

            var result = Subject.AllWithUnmappedFolders();

            result.Should().HaveCount(1);
            result[0].UnmappedFolders.Should().BeEmpty();
        }
    }
}
