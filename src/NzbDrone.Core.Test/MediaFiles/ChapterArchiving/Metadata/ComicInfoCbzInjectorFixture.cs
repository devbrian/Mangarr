using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 38 Plan 38-02 Task 1 — ComicInfoCbzInjector fixture (CINFO-01).
    ///
    /// Writes a REAL temp .cbz under the test's temp dir (golden-fixture convention — NOT under
    /// _tests/), opens it with <see cref="ZipArchiveMode.Update"/> via the injector, and asserts:
    ///   (a) a ComicInfo.xml entry exists with the recovered ScanlationGroup + TranslatedLanguage
    ///       + &lt;PageCount&gt;N&lt;/PageCount&gt; matching the real image count;
    ///   (b) injecting when ComicInfo.xml already exists yields EXACTLY ONE entry (upsert, not dup);
    ///   (c) PageCount counts image entries EXCLUDING the ComicInfo.xml entry;
    ///   (d) a CBZ that cannot be opened re-throws after exactly one retry (persisted failure is fatal).
    /// </summary>
    [TestFixture]
    public class ComicInfoCbzInjectorFixture : CoreTest<ComicInfoCbzInjector>
    {
        private string _tempDir;
        private NzbDrone.Core.Manga.Manga _manga;
        private Chapter _chapter;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ComicInfoCbzInjector_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _manga = new NzbDrone.Core.Manga.Manga
            {
                Id = 7,
                Title = "Vagabond",
                Overview = "A samurai's journey",
                ContentRating = "safe",
            };

            _chapter = new Chapter
            {
                Id = 42,
                MangaId = 7,
                Title = "Chapter 132",
                ChapterNumber = 132m,
            };
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private string MakePageOnlyCbz(int pageCount, bool includeExistingComicInfo = false)
        {
            var path = Path.Combine(_tempDir, "ch" + Guid.NewGuid().ToString("N") + ".cbz");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                for (var i = 0; i < pageCount; i++)
                {
                    var entry = zip.CreateEntry($"{i:D4}.jpg", CompressionLevel.NoCompression);
                    using var s = entry.Open();
                    var bytes = new byte[] { 0xFF, 0xD8, 0xFF, (byte)i };  // fake JPEG-ish header
                    s.Write(bytes, 0, bytes.Length);
                }

                if (includeExistingComicInfo)
                {
                    var entry = zip.CreateEntry("ComicInfo.xml");
                    using var s = entry.Open();
                    var stale = new XDocument(new XElement("ComicInfo", new XElement("Series", "STALE")));
                    stale.Save(s);
                }
            }

            return path;
        }

        private ChapterFile ChapterFileAt(string path, string group = "TestGroup", string lang = "en")
        {
            return new ChapterFile
            {
                Id = 99,
                MangaId = _manga.Id,
                ChapterId = _chapter.Id,
                Path = path,
                ScanlationGroup = group,
                TranslatedLanguage = lang,
            };
        }

        [Test]
        public void should_inject_comicinfo_with_real_pagecount_group_and_language()
        {
            var cbz = MakePageOnlyCbz(5);

            Subject.Inject(ChapterFileAt(cbz, group: "TestGroup", lang: "en"), _manga, _chapter);

            using var zip = ZipFile.OpenRead(cbz);
            var entry = zip.GetEntry("ComicInfo.xml");
            entry.Should().NotBeNull("the injector must upsert a ComicInfo.xml entry");

            using var s = entry.Open();
            var doc = XDocument.Load(s);
            doc.Root!.Element("PageCount").Value.Should().Be("5");
            doc.Root.Element("Translator").Value.Should().Be("TestGroup");
            doc.Root.Element("ScanInformation").Value.Should().Be("TestGroup");
            doc.Root.Element("LanguageISO").Value.Should().Be("en");
            doc.Root.Element("Series").Value.Should().Be("Vagabond");
        }

        [Test]
        public void should_upsert_single_entry_when_comicinfo_already_exists()
        {
            var cbz = MakePageOnlyCbz(3, includeExistingComicInfo: true);

            Subject.Inject(ChapterFileAt(cbz), _manga, _chapter);

            using var zip = ZipFile.OpenRead(cbz);
            zip.Entries.Count(e => e.Name == "ComicInfo.xml")
                .Should().Be(1, "delete-then-create upsert must not duplicate the entry");

            // The stale content must be replaced by the freshly-built document.
            var entry = zip.GetEntry("ComicInfo.xml");
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            doc.Root!.Element("Series").Value.Should().Be("Vagabond");
        }

        [Test]
        public void should_exclude_comicinfo_xml_from_pagecount()
        {
            // 5 images + a pre-existing ComicInfo.xml → PageCount must be 5, never 6.
            var cbz = MakePageOnlyCbz(5, includeExistingComicInfo: true);

            Subject.Inject(ChapterFileAt(cbz), _manga, _chapter);

            using var zip = ZipFile.OpenRead(cbz);
            var entry = zip.GetEntry("ComicInfo.xml");
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            doc.Root!.Element("PageCount").Value.Should().Be("5");
        }

        [Test]
        public void should_rethrow_after_retry_when_archive_cannot_be_opened()
        {
            // A path that does not exist (and cannot be created as a valid zip) forces ZipFile.Open
            // to throw on BOTH the initial attempt and the single retry → the second failure is fatal
            // and must propagate to the caller.
            var bogus = Path.Combine(_tempDir, "does-not-exist", "missing.cbz");

            Action act = () => Subject.Inject(ChapterFileAt(bogus), _manga, _chapter);

            act.Should().Throw<Exception>("a persisted injection failure must re-throw, not be swallowed");

            // D-B — the first failure logs a single Warn before the retry; declare it expected so the
            // LoggingTest base does not fail the test on an unexpected Warn.
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
