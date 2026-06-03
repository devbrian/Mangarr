using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata
{
    /// <summary>
    /// Phase 4 D-14 — pluggable reader-metadata writer.
    /// v1 implementations: <c>ComicInfoMetadataWriter</c> (FormatKey="comicinfo") in plan 04-07.
    /// v2 may add Mihon-format / Calibre-OPF / etc. — zero edits to archiver/downloader/import.
    ///
    /// Originally a sibling to the (now-retired) in-process chapter archiver: archivers iterated
    /// IEnumerable&lt;IMetadataWriter&gt; inside their archive scope. The archiver set was deleted in
    /// Phase 39 Plan 02 (RETIRE-01); the surviving writer is reached via the Phase 30 <c>IMetadata</c>
    /// ThingiProvider substrate. Each writer decides if it <see cref="AppliesTo"/> the request and
    /// streams via <see cref="ArchiveOutputContext.OpenSidecar"/>.
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
