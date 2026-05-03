using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer.Manga;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    [TestFixture]
    public class MangaNamingPresetsFixture
    {
        [Test]
        public void All_returns_four_presets_in_locked_order()
        {
            MangaNamingPresets.All.Should().HaveCount(4);
            MangaNamingPresets.All[0].Name.Should().Be("Komga");
            MangaNamingPresets.All[1].Name.Should().Be("Kavita");
            MangaNamingPresets.All[2].Name.Should().Be("ComicRack");
            MangaNamingPresets.All[3].Name.Should().Be("Custom");
        }

        [Test]
        public void Default_is_Komga()
        {
            MangaNamingPresets.Default.Name.Should().Be("Komga");
        }

        [Test]
        public void Komga_template_is_locked_per_RESEARCH_Pattern_5()
        {
            MangaNamingPresets.All[0].StandardChapterFormat.Should().Be("{Manga.Title} - Chapter {Chapter.Number:000}");
            MangaNamingPresets.All[0].MangaFolderFormat.Should().Be("{Manga.Title}");
        }

        [Test]
        public void Kavita_template_is_locked_per_RESEARCH_Pattern_5()
        {
            MangaNamingPresets.All[1].StandardChapterFormat.Should().Be("{Manga.Title} Ch.{Chapter.Number:0000}");
        }

        [Test]
        public void ComicRack_template_is_locked_per_RESEARCH_Pattern_5()
        {
            MangaNamingPresets.All[2].StandardChapterFormat.Should().Be("{Manga.Title} #{Chapter.Number:000}");
        }

        [Test]
        public void All_presets_use_flat_MangaFolderFormat_per_D15()
        {
            foreach (var preset in MangaNamingPresets.All)
            {
                preset.MangaFolderFormat.Should().Be("{Manga.Title}",
                    $"D-15 flat folder layout — preset {preset.Name} should not use volume nesting");
            }
        }
    }
}
