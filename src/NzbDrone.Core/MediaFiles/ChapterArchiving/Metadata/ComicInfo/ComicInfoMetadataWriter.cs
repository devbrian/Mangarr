using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo
{
    /// <summary>
    /// Phase 4 ARCHIVE-04 — IMetadataWriter implementation emitting ComicInfo.xml
    /// dual-write (v2.0 + v2.1 fields per Pitfall 3 — Kavita's v2.1 Translator parse
    /// status is ambiguous; the v2.0 ScanInformation fallback covers the gap).
    ///
    /// User toggles via <c>Config.MetadataFormats</c> (default <c>["comicinfo"]</c>;
    /// can be <c>[]</c> to disable; can stack <c>["comicinfo","mihon"]</c> in v2 once
    /// additional writers ship — D-14 plugin contract).
    ///
    /// Composes with any <see cref="ArchiveOutputContext"/> implementation via the
    /// format-agnostic <see cref="ArchiveOutputContext.OpenSidecar"/> seam. The concrete
    /// CBZ / folder output contexts were retired with the in-process archiver set in
    /// Phase 39 Plan 02 (RETIRE-01).
    /// </summary>
    public class ComicInfoMetadataWriter : IMetadataWriter
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ComicInfoMetadataWriter(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public string FormatKey => "comicinfo";

        public bool AppliesTo(ChapterArchiveRequest request)
        {
            var formats = _configService.MetadataFormats;
            return formats != null
                && formats.Any(f => string.Equals(f, FormatKey, StringComparison.OrdinalIgnoreCase));
        }

        public async Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct)
        {
            var doc = ComicInfoXmlBuilder.Build(request);
            using var stream = ctx.OpenSidecar("ComicInfo.xml");
            await doc.SaveAsync(stream, SaveOptions.None, ct).ConfigureAwait(false);
            _logger.Debug("Wrote ComicInfo.xml for chapter {0}", request?.OutputFilename);
        }
    }
}
