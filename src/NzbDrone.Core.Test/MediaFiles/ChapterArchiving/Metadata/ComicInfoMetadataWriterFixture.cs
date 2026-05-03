using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Cbz;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 4 plan 04-07 Task 2 — IMetadataWriter composition fixture for
    /// <see cref="ComicInfoMetadataWriter"/>. Verifies AppliesTo respects
    /// <c>Config.MetadataFormats</c> (D-14 disable / case-insensitive) and that
    /// WriteAsync streams ComicInfo.xml correctly via the
    /// <see cref="ArchiveOutputContext.OpenSidecar"/> seam against a real
    /// <see cref="CbzArchiveOutputContext"/>.
    ///
    /// FolderArchiveOutputContext composition test is deferred — plan 04-05
    /// has not landed in this worktree's base yet (Wave 2 race).
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
                Manga = new Series
                {
                    Title = "Vagabond",
                    Genres = new List<string>(),
                    Certification = "safe",
                },
                Chapter = new Episode
                {
                    Title = "Ch 132",
                    EpisodeNumber = 132,
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
        public async Task WriteAsync_streams_via_CbzArchiveOutputContext()
        {
            // Compose against a real ZipArchive in-memory just to verify the
            // OpenSidecar abstraction round-trips.
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var ctx = new CbzArchiveOutputContext(archive);
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

        [Test]
        [Ignore("FolderArchiveOutputContext lands in plan 04-05 (Wave 2); enable once base catches up.")]
        public Task WriteAsync_streams_via_FolderArchiveOutputContext()
        {
            // Placeholder — extend with the symmetric folder-side test when 04-05 lands.
            return Task.CompletedTask;
        }
    }
}
