using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 4 D-14 — pluggable reader-metadata writer.
    /// v1 implementations: <c>ComicInfoMetadataWriter</c> (FormatKey="comicinfo") in plan 04-07.
    /// v2 may add Mihon-format / Calibre-OPF / etc. — zero edits to archiver/downloader/import.
    ///
    /// Sibling to <see cref="IChapterArchiver"/>: archivers iterate IEnumerable&lt;IMetadataWriter&gt;
    /// (DryIoc auto-discovery) inside their archive scope; each writer decides if it
    /// <see cref="AppliesTo"/> the request and streams via <see cref="ArchiveOutputContext.OpenSidecar"/>.
    /// </summary>
    public interface IMetadataWriter
    {
        /// <summary>Stable identifier matched against <c>Config.MetadataFormats</c> entries.</summary>
        string FormatKey { get; }

        /// <summary>
        /// Whether this writer should run for this request. Default impls check
        /// <c>configService.MetadataFormats.Contains(FormatKey)</c>.
        /// </summary>
        bool AppliesTo(ChapterArchiveRequest request);

        /// <summary>
        /// Streams the sidecar bytes (e.g. ComicInfo.xml) via the format-agnostic
        /// <see cref="ArchiveOutputContext.OpenSidecar"/> seam.
        /// </summary>
        Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct);
    }
}
