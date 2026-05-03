using System.IO;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 D-14 — abstraction so metadata writers stream their sidecars without knowing
    /// whether they're inside a ZIP archive or beside images on disk. CBZ archiver passes
    /// <c>CbzArchiveOutputContext</c>; folder archiver passes <c>FolderArchiveOutputContext</c>.
    /// </summary>
    public abstract class ArchiveOutputContext
    {
        /// <summary>
        /// Opens a writable stream for a sidecar file (e.g. "ComicInfo.xml"). Caller is
        /// responsible for disposing. CBZ ctx writes into the open <see cref="System.IO.Compression.ZipArchive"/>
        /// entry; folder ctx writes to a sibling file under the staging directory.
        /// </summary>
        public abstract Stream OpenSidecar(string filename);
    }
}
