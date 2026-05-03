using System;
using System.IO;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Folder
{
    /// <summary>
    /// Phase 4 D-14 — folder-mode <see cref="ArchiveOutputContext"/>. Sidecars land as
    /// sibling files inside the staging directory (e.g. "ComicInfo.xml" beside "0001.jpg").
    /// CBZ analog: <c>CbzArchiveOutputContext</c> (which streams sidecars into ZipArchive entries).
    /// The CBZ-vs-Folder split is the entire reason <see cref="ArchiveOutputContext"/> exists —
    /// metadata writers (e.g. plan 04-07's ComicInfoMetadataWriter) compose against the abstract
    /// base, never knowing whether they're inside a ZIP or beside images on disk.
    /// </summary>
    public sealed class FolderArchiveOutputContext : ArchiveOutputContext
    {
        private readonly string _outputDir;
        private readonly IDiskProvider _diskProvider;

        public FolderArchiveOutputContext(string outputDir, IDiskProvider diskProvider)
        {
            _outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            _diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        }

        public override Stream OpenSidecar(string filename)
        {
            // Filename comes from internal callers only (v1: literal "ComicInfo.xml" via plan 04-07).
            // T-04-14: see threat model in 04-05-PLAN.md — no sanitisation here in v1; future
            // v2 plug-in writers with attacker-influenced filenames must validate themselves.
            var path = Path.Combine(_outputDir, filename);
            return _diskProvider.OpenWriteStream(path);
        }
    }
}
