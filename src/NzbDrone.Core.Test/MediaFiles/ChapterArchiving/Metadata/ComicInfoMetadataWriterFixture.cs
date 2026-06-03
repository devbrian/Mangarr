using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.Parser.Model;

// Sonarr divergence: Phase 15 Plan 15-11 cascade absorption — Series/Episode replaced with manga peer Manga/Chapter; Series.Certification -> Manga.ContentRating; Episode.EpisodeNumber/AirDateUtc -> Chapter.ChapterNumber/ReleaseDate.
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 4 plan 04-07 Task 2 — IMetadataWriter composition fixture for
    /// <see cref="ComicInfoMetadataWriter"/>. Verifies AppliesTo respects
    /// <c>Config.MetadataFormats</c> (D-14 disable / case-insensitive) and that
    /// WriteAsync streams ComicInfo.xml correctly via the
    /// <see cref="ArchiveOutputContext.OpenSidecar"/> seam.
    ///
    /// Phase 39 Plan 02 (RETIRE-01): the concrete <c>CbzArchiveOutputContext</c> /
    /// <c>FolderArchiveOutputContext</c> were deleted along with the orphaned in-process
    /// archiver set. This fixture now exercises the abstract <see cref="ArchiveOutputContext"/>
    /// seam against a minimal in-fixture ZIP-backed double, which is all the surviving
    /// <see cref="ComicInfoMetadataWriter"/> contract requires (it only calls
    /// <see cref="ArchiveOutputContext.OpenSidecar"/>).
    /// </summary>
    [TestFixture]
    public class ComicInfoMetadataWriterFixture : CoreTest<ComicInfoMetadataWriter>
    {
        private ChapterArchiveRequest _req;

        [SetUp]
        public void SetUp()
        {
            _req = new ChapterArchiveRequest
            {
                Manga = new Manga.Manga
                {
                    Title = "Vagabond",
                    Genres = new List<string>(),
                    ContentRating = "safe",
                },
                Chapter = new Chapter
                {
                    Title = "Ch 132",
                    ChapterNumber = 132,
                },
                Release = new ReleaseInfo
                {
                    TranslatedLanguage = "en",
                    ScanlationGroup = "G",
                },
                PageCount = 5,
                OutputFilename = "Chapter 132",
            };
        }

        [Test]
        public void FormatKey_is_comicinfo()
        {
            Subject.FormatKey.Should().Be("comicinfo");
        }

        [Test]
        public void AppliesTo_true_when_comicinfo_in_MetadataFormats()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.MetadataFormats)
                  .Returns(new List<string> { "comicinfo" });

            Subject.AppliesTo(_req).Should().BeTrue();
        }

        [Test]
        public void AppliesTo_false_when_MetadataFormats_empty()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.MetadataFormats)
                  .Returns(new List<string>());

            Subject.AppliesTo(_req).Should().BeFalse();
        }

        [Test]
        public void AppliesTo_false_when_MetadataFormats_null()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.MetadataFormats)
                  .Returns((List<string>)null);

            Subject.AppliesTo(_req).Should().BeFalse();
        }

        [Test]
        public void AppliesTo_case_insensitive()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.MetadataFormats)
                  .Returns(new List<string> { "ComicInfo" });

            Subject.AppliesTo(_req).Should().BeTrue();
        }

        [Test]
        public async Task WriteAsync_streams_via_OpenSidecar_seam()
        {
            // Compose against a real ZipArchive in-memory through the abstract
            // ArchiveOutputContext.OpenSidecar seam just to verify the writer round-trips.
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var ctx = new ZipBackedOutputContext(archive);
                await Subject.WriteAsync(_req, ctx, CancellationToken.None);
            }

            ms.Position = 0;
            using var read = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = read.GetEntry("ComicInfo.xml");
            entry.Should().NotBeNull("ctx.OpenSidecar should produce a ComicInfo.xml ZIP entry");

            using var entryStream = entry!.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            var xml = reader.ReadToEnd();

            xml.Should().Contain("<ComicInfo>");
            xml.Should().Contain("<Translator>G</Translator>");
            xml.Should().Contain("<ScanInformation>G</ScanInformation>");
        }

        // Minimal ArchiveOutputContext double — Phase 39 Plan 02 replaced the deleted
        // CbzArchiveOutputContext (it opened a ZIP entry exactly like this). The surviving
        // ComicInfoMetadataWriter only depends on the abstract OpenSidecar contract.
        private sealed class ZipBackedOutputContext : ArchiveOutputContext
        {
            private readonly ZipArchive _archive;

            public ZipBackedOutputContext(ZipArchive archive)
            {
                _archive = archive;
            }

            public override Stream OpenSidecar(string filename)
            {
                return _archive.CreateEntry(filename).Open();
            }
        }
    }
}
