using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Cbz;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 plan 04-04 Task 3 — composition seam contract test.
    /// Defends the <see cref="ArchiveOutputContext"/> + <see cref="CbzArchiveOutputContext"/> +
    /// IMetadataWriter composition against future refactors that might (a) make
    /// <c>ArchiveOutputContext</c> non-abstract, (b) drop the inheritance from
    /// <c>CbzArchiveOutputContext</c>, or (c) alter the <c>OpenSidecar</c> signature in a way
    /// that breaks IMetadataWriter consumers.
    ///
    /// When plan 04-05 lands <c>FolderArchiveOutputContext</c>, extend this fixture with the
    /// symmetric Folder-side test. For now CBZ is the only concrete subclass.
    /// </summary>
    [TestFixture]
    public class ArchiveOutputContextContractFixture
    {
        [Test]
        public void CbzArchiveOutputContext_is_an_ArchiveOutputContext()
        {
            // Inheritance contract — CBZ subclass MUST be an ArchiveOutputContext (D-14 plugin seam).
            typeof(CbzArchiveOutputContext).Should().BeAssignableTo<ArchiveOutputContext>();
        }

        [Test]
        public void ArchiveOutputContext_OpenSidecar_is_abstract()
        {
            // The abstract method is the seam — concrete subclasses MUST override.
            var method = typeof(ArchiveOutputContext).GetMethod(nameof(ArchiveOutputContext.OpenSidecar));
            method.Should().NotBeNull();
            method!.IsAbstract.Should().BeTrue();
            method.ReturnType.Should().Be(typeof(Stream));
        }

        [Test]
        public void CbzArchiveOutputContext_OpenSidecar_creates_a_zip_entry()
        {
            // End-to-end: CBZ ctx.OpenSidecar('foo.xml') produces a writable entry inside the open ZipArchive.
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var ctx = new CbzArchiveOutputContext(archive);
                using var stream = ctx.OpenSidecar("ComicInfo.xml");
                using var writer = new StreamWriter(stream);
                writer.Write("<ComicInfo/>");
            }

            ms.Position = 0;
            using var read = new ZipArchive(ms, ZipArchiveMode.Read);
            read.Entries.Should().Contain(e => e.Name == "ComicInfo.xml");
        }

        [Test]
        public void IMetadataWriter_can_be_implemented_end_to_end_against_CbzArchiveOutputContext()
        {
            // The cross-component composition: any IMetadataWriter impl receives an
            // ArchiveOutputContext and must compose against CbzArchiveOutputContext without casts.
            // Stub writer + CBZ ctx => sidecar lands in the ZIP. Proves the seam compiles +
            // composes for plan 04-07's ComicInfoMetadataWriter (Wave 2 dependency).
            var writer = new StubMetadataWriter();
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                ArchiveOutputContext ctx = new CbzArchiveOutputContext(archive);   // upcast — compiler check
                writer.WriteSync(new ChapterArchiveRequest { OutputFilename = "x", PageCount = 0 }, ctx);
            }

            ms.Position = 0;
            using var read = new ZipArchive(ms, ZipArchiveMode.Read);
            read.Entries.Select(e => e.Name).Should().Contain("stub.txt");
        }

        private sealed class StubMetadataWriter
        {
            public void WriteSync(ChapterArchiveRequest req, ArchiveOutputContext ctx)
            {
                using var s = ctx.OpenSidecar("stub.txt");
                using var w = new StreamWriter(s);
                w.Write("ok");
            }
        }
    }
}
