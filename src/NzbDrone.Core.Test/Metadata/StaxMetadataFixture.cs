using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Metadata.Stax;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

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
        public void ensures_manga_folder_exists_before_writing_on_add()
        {
            // Regression: on ADD the manga folder does not exist yet (nothing downloaded), so
            // WriteAllText threw DirectoryNotFoundException and stax.json only appeared on a
            // later refresh once the folder existed. EnsureFolder must run before the write.
            //
            // CodeRabbit #407: record call ORDER — the fix's whole premise is EnsureFolder
            // BEFORE WriteAllText, and Times.Once alone would pass even if a regression
            // reversed them (write-then-create would still throw on a missing folder).
            var calls = new List<string>();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(false);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.EnsureFolder(It.IsAny<string>()))
                  .Callback(() => calls.Add("EnsureFolder"));
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() => calls.Add("WriteAllText"));

            Subject.WriteMangaMetadata(GivenManga(42));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.EnsureFolder(MangaPath), Times.Once());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            calls.Should().Equal("EnsureFolder", "WriteAllText");
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
        public void swallows_disk_error_on_self_heal_read_path()
        {
            // CodeRabbit #406: the exist-check + ReadAllText are inside the SUT's try/catch, so a
            // disk error on the READ path (permission denial, TOCTOU delete, AV lock) must NOT
            // propagate out of WriteMangaMetadata — WR-07 batch tolerance.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Throws(new UnauthorizedAccessException("simulated read denial"));

            Action act = () => Subject.WriteMangaMetadata(GivenManga(42));

            act.Should().NotThrow();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void skips_entirely_when_mangabaka_id_null()
        {
            Subject.WriteMangaMetadata(GivenManga(null));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.EnsureFolder(It.IsAny<string>()), Times.Never());
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
                  .Verify(d => d.EnsureFolder(It.IsAny<string>()), Times.Never());
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
