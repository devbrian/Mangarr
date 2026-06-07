using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using NzbDrone.Core.MediaCover;

namespace Mangarr.Http.Frontend.Mappers
{
    public class MediaCoverProxyMapper : IMapHttpRequestsToDisk
    {
        // The hash is the lookup key; the filename is used only for mime-type guessing
        // (falls back to application/octet-stream below). Accept ANY filename after the
        // hash — MangaBaka (Phase 41 default primary) serves covers as `.webp` and as
        // extension-less CDN path segments, neither of which matched the original
        // `.jpg|.png|.gif`-only pattern, so every such cover 404'd. MangaDex always used
        // `.jpg/.png`, which is why this latent limitation only surfaced with MangaBaka.
        private readonly Regex _regex = new(@"/MediaCoverProxy/(?<hash>\w+)/(?<filename>.+)");

        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IContentTypeProvider _mimeTypeProvider;

        public MediaCoverProxyMapper(IMediaCoverProxy mediaCoverProxy)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _mimeTypeProvider = new FileExtensionContentTypeProvider();
        }

        public string Map(string resourceUrl)
        {
            return null;
        }

        public bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/MediaCoverProxy/", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task<IActionResult> GetResponse(HttpContext context, string resourceUrl)
        {
            var match = _regex.Match(resourceUrl);

            if (!match.Success)
            {
                return new StatusCodeResult((int)HttpStatusCode.NotFound);
            }

            var hash = match.Groups["hash"].Value;
            var filename = match.Groups["filename"].Value;

            var imageData = await _mediaCoverProxy.GetImage(hash);

            if (!_mimeTypeProvider.TryGetContentType(filename, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return new FileContentResult(imageData, contentType);
        }
    }
}
