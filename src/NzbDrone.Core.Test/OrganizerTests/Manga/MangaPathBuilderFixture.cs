using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    // Phase 5 plan 05-06 — replaces the Wave-0 placeholder fixture. D-15 flat folder layout asserted:
    // <root>/<Manga Title>/<Chapter NNN>.cbz (two levels deep — root → manga folder → chapter file).
    [TestFixture]
    public class MangaPathBuilderFixture : CoreTest<MangaPathBuilder>
    {
        [SetUp]
        public void Setup()
        {
            // Default: GetMangaFolder returns the manga title (D-15 flat layout).
            Mocker.GetMock<IBuildMangaFileNames>()
                  .Setup(s => s.GetMangaFolder(It.IsAny<MangaModel>(), It.IsAny<NamingConfig>()))
                  .Returns<MangaModel, NamingConfig>((m, _) => m.Title);
        }

        [Test]
        public void BuildPath_combines_root_with_MangaFolderFormat()
        {
            var manga = new MangaModel
            {
                Title = "Solo Leveling",
                RootFolderPath = @"/manga".AsOsAgnostic()
            };

            var path = Subject.BuildPath(manga, useExistingRelativeFolder: false);

            path.Should().Be(Path.Combine(@"/manga".AsOsAgnostic(), "Solo Leveling"));
        }

        [Test]
        public void BuildPath_throws_when_RootFolderPath_is_null_or_whitespace()
        {
            var manga = new MangaModel
            {
                Title = "Solo Leveling",
                RootFolderPath = string.Empty
            };

            Action act = () => Subject.BuildPath(manga, useExistingRelativeFolder: false);

            act.Should().Throw<ArgumentException>()
                .WithMessage("Root folder was not provided*");
        }
    }
}
