using System.Collections.Generic;

namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>SubmitRequest</c> schema
    /// (<c>POST /downloads</c>). Required on the wire: <c>releaseHandle</c>, <c>sourceKey</c>.
    ///
    /// <para>
    /// <see cref="ReleaseHandle"/> is the opaque R6 token the Phase-37 GatewayIndexer minted on
    /// <c>Release.downloadHandle</c> (mapped onto <c>ReleaseInfo.DownloadUrl</c> at
    /// <c>GatewayParser.cs:80</c>) — it is submitted BACK to the gateway here, NEVER dereferenced
    /// by Mangarr (Pitfall 5 / SSRF). <see cref="OutputFormat"/> is the D-D hard-default
    /// <c>"cbz"</c>; it is NOT a user-exposed settings field.
    /// </para>
    /// </summary>
    public class GatewaySubmitRequest
    {
        // Required (OpenAPI required: [releaseHandle, sourceKey]).
        public string ReleaseHandle { get; set; }
        public string SourceKey { get; set; }

        // Optional gateway-internal manifest URL hint (nullable on the wire).
        public string DownloadUrl { get; set; }

        public string Title { get; set; }
        public int MangaId { get; set; }
        public string MangaTitle { get; set; }
        public List<int> ChapterIds { get; set; }
        public List<decimal> ChapterNumbers { get; set; }

        // D-D hard-default: the client ALWAYS sets this to "cbz" — never user-configurable.
        public string OutputFormat { get; set; }
    }
}
