using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.Parser.Model;

// Sonarr divergence: Phase 15 Plan 15-11 cascade absorption — Series/Episode replaced with manga peer Manga/Chapter; Series.Certification -> Manga.ContentRating; Episode.EpisodeNumber/AirDateUtc -> Chapter.ChapterNumber/ReleaseDate.

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 4 plan 04-07 Task 1 — pure-XDocument builder fixture.
    /// Defends ARCHIVE-04 dual-write contract (Pitfall 3) and InvariantCulture formatting
    /// (Pitfall 7) under multiple [SetCulture] runs. Includes a DeepEquals against the
    /// golden ComicInfo.xml fixture for canonical-input drift detection.
    /// </summary>
    [TestFixture]
    public class ComicInfoXmlBuilderFixture
    {
        private static ChapterArchiveRequest MakeRequest(
            int chapterNumber = 132,
            string contentRating = "safe",
            string scanlator = "TestGroup",
            string lang = "en",
            DateTime? airDateUtc = null)
        {
            return new ChapterArchiveRequest
            {
                Manga = new Manga.Manga
                {
                    Title = "Vagabond",
                    Overview = "A samurai's journey",
                    Genres = new List<string> { "Action", "Drama" },

                    // Phase 2 hasn't added a dedicated ContentRating field; v1 reuses the
                    // existing Sonarr `Certification` string field as the AgeRating source.
                    // Phase 5 metadata-source work may rename / move this — see SUMMARY.
                    ContentRating = contentRating,
                },
                Chapter = new Chapter
                {
                    Title = "Chapter 132",
                    ChapterNumber = chapterNumber,
                    ReleaseDate = airDateUtc ?? new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                },
                Release = new ReleaseInfo
                {
                    TranslatedLanguage = lang,
                    ScanlationGroup = scanlator,
                },
                PageCount = 5,
                OutputFilename = "Chapter 132",
            };
        }

        [Test]
        public void Includes_all_v20_mandatory_fields()
        {
            var doc = ComicInfoXmlBuilder.Build(MakeRequest());
            var root = doc.Root;

            root.Should().NotBeNull();
            root!.Element("Title").Should().NotBeNull();
            root.Element("Series").Should().NotBeNull();
            root.Element("Number").Should().NotBeNull();
            root.Element("Genre").Should().NotBeNull();
            root.Element("PageCount").Value.Should().Be("5");
            root.Element("LanguageISO").Should().NotBeNull();
            root.Element("AgeRating").Should().NotBeNull();
            root.Element("Manga").Value.Should().Be("YesAndRightToLeft");
        }

        [Test]
        public void Dual_writes_ScanInformation_AND_Translator()
        {
            // Pitfall 3 — Kavita's <Translator> parse is ambiguous; v2.0 <ScanInformation> fallback
            // covers the gap while Komga 1.10+ reads the preferred v2.1 <Translator> field.
            var doc = ComicInfoXmlBuilder.Build(MakeRequest(scanlator: "TestGroup"));

            doc.Root!.Element("ScanInformation").Value.Should().Be("TestGroup");
            doc.Root!.Element("Translator").Value.Should().Be("TestGroup");
        }

        [Test]
        [SetCulture("de-DE")]
        public void Number_uses_InvariantCulture_under_de_DE_locale()
        {
            // Pitfall 7 — must NOT format with comma decimal under de-DE.
            var doc = ComicInfoXmlBuilder.Build(MakeRequest(chapterNumber: 132));

            doc.Root!.Element("Number").Value.Should().Be("132");
        }

        [Test]
        [SetCulture("ja-JP")]
        public void Number_uses_InvariantCulture_under_ja_JP_locale()
        {
            var doc = ComicInfoXmlBuilder.Build(MakeRequest(chapterNumber: 132));

            doc.Root!.Element("Number").Value.Should().Be("132");
        }

        [Test]
        [SetCulture("en-US")]
        public void Number_uses_InvariantCulture_under_en_US_locale()
        {
            var doc = ComicInfoXmlBuilder.Build(MakeRequest(chapterNumber: 132));

            doc.Root!.Element("Number").Value.Should().Be("132");
        }

        [TestCase("safe", "Everyone")]
        [TestCase("suggestive", "Teen")]
        [TestCase("erotica", "Adults Only 18+")]
        [TestCase("pornographic", "X18+")]
        [TestCase("unknown-rating", "Unknown")]
        [TestCase(null, "Unknown")]
        public void AgeRating_mapping_table(string mangaDexRating, string expected)
        {
            AgeRatingMapper.Map(mangaDexRating).Should().Be(expected);
        }

        [Test]
        public void Null_ScanlationGroup_omits_both_ScanInformation_and_Translator()
        {
            var doc = ComicInfoXmlBuilder.Build(MakeRequest(scanlator: null));

            doc.Root!.Element("ScanInformation").Should().BeNull();
            doc.Root!.Element("Translator").Should().BeNull();
        }

        [Test]
        public void Null_Manga_Overview_omits_Summary()
        {
            var req = MakeRequest();
            req.Manga.Overview = null;
            var doc = ComicInfoXmlBuilder.Build(req);

            doc.Root!.Element("Summary").Should().BeNull();
        }

        [Test]
        public void No_xmlns_namespace_declared()
        {
            // anansi-project XSDs declare no targetNamespace.
            var doc = ComicInfoXmlBuilder.Build(MakeRequest());

            doc.Root!.Name.NamespaceName.Should().BeEmpty();
        }

        [Test]
        public void Web_element_omitted_in_v1_no_MangaDex_id_field_yet()
        {
            // WARNING #7 — Series.cs has MalIds + AniListIds but no MangaDex ID; v1 omits <Web>.
            var doc = ComicInfoXmlBuilder.Build(MakeRequest());

            doc.Root!.Element("Web").Should().BeNull();
        }

        [Test]
        public void Empty_Genres_omits_Genre_and_Tags()
        {
            var req = MakeRequest();
            req.Manga.Genres = new List<string>();
            var doc = ComicInfoXmlBuilder.Build(req);

            doc.Root!.Element("Genre").Should().BeNull();
            doc.Root!.Element("Tags").Should().BeNull();
        }

        [Test]
        public void DeepEquals_golden_xml()
        {
            // Golden expected XML for canonical input — review golden if intentionally changing schema.
            var doc = ComicInfoXmlBuilder.Build(MakeRequest());

            var goldenPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Files",
                "Phase04",
                "golden-cbz",
                "ComicInfo.xml");
            File.Exists(goldenPath).Should().BeTrue($"golden fixture must exist at {goldenPath}");

            var golden = XDocument.Load(goldenPath);
            XNode.DeepEquals(doc.Root, golden.Root)
                .Should()
                .BeTrue("output XML should match golden — review golden if intentionally changing schema");
        }
    }
}
