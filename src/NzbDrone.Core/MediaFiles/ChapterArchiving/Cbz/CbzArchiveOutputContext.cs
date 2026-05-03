using System.IO;
using System.IO.Compression;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Cbz
{
    /// <summary>
    /// Phase 4 D-14 — CBZ-specific <see cref="ArchiveOutputContext"/>. Sidecars stream
    /// into ZipArchive entries with <see cref="CompressionLevel.NoCompression"/> (D-17 —
    /// Stored, not Deflated; .NET 9+ produces true Stored entries per dotnet/docs#40299).
    /// </summary>
    public sealed class CbzArchiveOutputContext : ArchiveOutputContext
    {
        private readonly ZipArchive _archive;

        public CbzArchiveOutputContext(ZipArchive archive)
        {
            _archive = archive;
        }

        public override Stream OpenSidecar(string filename)
        {
            var entry = _archive.CreateEntry(filename, CompressionLevel.NoCompression);
            return entry.Open();
        }
    }
}
