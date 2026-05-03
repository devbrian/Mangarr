using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 ARCHIVE-03 — Wave 0 fixture for <see cref="ChapterArchiverFactory"/>.
    /// Asserts the factory's case-insensitive FormatKey lookup, the cbz fallback +
    /// Warn-log on unknown keys, and the canonical DryIoc IEnumerable&lt;IChapterArchiver&gt;
    /// injection idiom (mirrors <c>ImportDecisionMaker</c>'s
    /// IEnumerable&lt;IImportDecisionEngineSpecification&gt; pattern).
    /// </summary>
    [TestFixture]
    public class ChapterArchiverFactoryFixture : CoreTest<ChapterArchiverFactory>
    {
        private Mock<IChapterArchiver> _cbz;
        private Mock<IChapterArchiver> _folder;

        [SetUp]
        public void SetUp()
        {
            _cbz = new Mock<IChapterArchiver>();
            _cbz.SetupGet(a => a.FormatKey).Returns("cbz");

            _folder = new Mock<IChapterArchiver>();
            _folder.SetupGet(a => a.FormatKey).Returns("folder");

            Mocker.SetConstant<IEnumerable<IChapterArchiver>>(new[] { _cbz.Object, _folder.Object });
        }

        [Test]
        public void Resolve_cbz_returns_cbz_archiver()
        {
            Subject.Resolve("cbz").FormatKey.Should().Be("cbz");
        }

        [Test]
        public void Resolve_folder_returns_folder_archiver()
        {
            Subject.Resolve("folder").FormatKey.Should().Be("folder");
        }

        [TestCase("CBZ")]
        [TestCase("CbZ")]
        [TestCase("Cbz")]
        public void Resolve_case_insensitive_to_cbz(string formatKey)
        {
            Subject.Resolve(formatKey).FormatKey.Should().Be("cbz");
        }

        [TestCase("FOLDER")]
        [TestCase("Folder")]
        [TestCase("FoLdEr")]
        public void Resolve_case_insensitive_to_folder(string formatKey)
        {
            Subject.Resolve(formatKey).FormatKey.Should().Be("folder");
        }

        [Test]
        public void Resolve_unknown_falls_back_to_cbz()
        {
            var resolved = Subject.Resolve("epub-future-format");

            resolved.FormatKey.Should().Be("cbz");

            // T-04-18 mitigation — Warn-level log on unknown FormatKey.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Resolve_null_falls_back_to_cbz()
        {
            var resolved = Subject.Resolve(null);

            resolved.FormatKey.Should().Be("cbz");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Resolve_empty_falls_back_to_cbz()
        {
            var resolved = Subject.Resolve(string.Empty);

            resolved.FormatKey.Should().Be("cbz");
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
