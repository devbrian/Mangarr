using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Metadata.Stax;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Metadata
{
    /// <summary>
    /// quick-260701-e71 — provider-level coverage for <see cref="StaxMetadata"/>'s
    /// on-disk series-level write. Exercises the D-02 self-heal (create / skip-on-match /
    /// overwrite-on-diff / overwrite-on-unparseable) + D-03 skip (null MangaBakaId /
    /// empty Path) semantics against a mocked <see cref="IDiskProvider"/> — no real IO
    /// (the SUT only does Path.Combine + string equality).
    /// </summary>
    [TestFixture]
    public class StaxMetadataFixture : CoreTest<StaxMetadata>
    {
        // Cross-platform-safe literal — the SUT does Path.Combine + string equality only.
        private const string MangaPath = @"C:\manga\Solo Leveling";

        private Manga.Manga GivenManga(int? mangaBakaId, string path = MangaPath)
        {
            return new Manga.Manga
            {
                Title = "Solo Leveling",
                Path = path,
                MangaBakaId = mangaBakaId,
            };
        }

        private string ExistingStaxJson(int mangaBakaId)
        {
            // Build the existing-file JSON with the SAME serializer the SUT uses so the
            // round-trip compare is exact (camelCase mangabakaId).
            return Json.ToJson(new { mangabakaId = mangaBakaId });
        }

        [Test]
        public void applies_to_returns_false()
        {
            Subject.AppliesTo(new ChapterArchiveRequest()).Should().BeFalse();
        }

        [Test]
        public void creates_stax_json_when_missing()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(false);

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((_, contents) => written = contents);

            Subject.WriteMangaMetadata(GivenManga(42));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            Json.TryDeserialize<StaxAssertionPayload>(written, out var parsed).Should().BeTrue();
            parsed.MangaBakaId.Should().Be(42);
        }

        [Test]
        public void skips_write_when_existing_content_matches()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Returns(ExistingStaxJson(42));

            Subject.WriteMangaMetadata(GivenManga(42));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void overwrites_when_mangabaka_id_differs()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Returns(ExistingStaxJson(7));

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((_, contents) => written = contents);

            Subject.WriteMangaMetadata(GivenManga(42));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            Json.TryDeserialize<StaxAssertionPayload>(written, out var parsed).Should().BeTrue();
            parsed.MangaBakaId.Should().Be(42);
        }

        [Test]
        public void overwrites_when_existing_unparseable()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Returns("not json");

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((_, contents) => written = contents);

            Subject.WriteMangaMetadata(GivenManga(42));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            Json.TryDeserialize<StaxAssertionPayload>(written, out var parsed).Should().BeTrue();
            parsed.MangaBakaId.Should().Be(42);
        }

        [Test]
        public void skips_entirely_when_mangabaka_id_null()
        {
            Subject.WriteMangaMetadata(GivenManga(null));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.FileExists(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.ReadAllText(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void skips_when_path_empty()
        {
            Subject.WriteMangaMetadata(GivenManga(42, path: string.Empty));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.FileExists(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.ReadAllText(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        // Mirror of the SUT's private StaxPayload shape so the written camelCase
        // `mangabakaId` round-trips back to an int for assertion.
        private sealed class StaxAssertionPayload
        {
            public int MangaBakaId { get; set; }
        }
    }
}
