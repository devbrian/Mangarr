using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    /// <summary>
    /// Phase 30 Plan 30-04 D-02 — ThingiProvider contract for chapter-archive metadata
    /// writers (manga-shape per R-14).
    ///
    /// Sonarr divergence (R-14): Sonarr's <c>IMetadata</c> is Series/Episode/Season-keyed
    /// and writes standalone on-disk sidecars (Kodi NFO, Roksbox, Wdtv). Mangarr's only
    /// v1.2 provider (<c>ComicInfoMetadata</c>) writes ComicInfo.xml INTO the CBZ via
    /// <see cref="ArchiveOutputContext.OpenSidecar"/> — fundamentally different lifecycle.
    /// On-disk sidecar writers (Kodi NFO, Komga series.json, Kavita-flavored) are v1.3+
    /// scope and may reintroduce the Sonarr-canonical method shape at that time. Recorded
    /// in DIVERGENCE.md per Phase 30 Plan 30-04.
    ///
    /// D-02 Option 4b — <c>ComicInfoMetadata</c> wraps the existing
    /// <see cref="MediaFiles.ChapterArchiving.Metadata.ComicInfo.ComicInfoMetadataWriter"/>
    /// via delegation, preserving Phase 4 golden-fixture XML tests verbatim.
    /// </summary>
    public interface IMetadata : IProvider
    {
        /// <summary>Stable identifier (e.g. <c>"comicinfo"</c>); preserved verbatim from the
        /// Phase 4 <c>IMetadataWriter.FormatKey</c> contract for Option 4b delegation.</summary>
        string FormatKey { get; }

        /// <summary>
        /// Whether this provider should run for this request. Post Phase 30 D-02 flip,
        /// implementations check <c>Definition?.Enable ?? false</c> instead of reading
        /// <c>Config.MetadataFormats</c>; the ThingiProvider Enable toggle is the
        /// canonical signal.
        /// </summary>
        bool AppliesTo(ChapterArchiveRequest request);

        /// <summary>
        /// Streams the sidecar bytes (e.g. <c>ComicInfo.xml</c>) via the format-agnostic
        /// <see cref="ArchiveOutputContext.OpenSidecar"/> seam.
        /// </summary>
        Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct);
    }
}
