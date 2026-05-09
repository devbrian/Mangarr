using System.Net;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover
{
    public interface ICoverExistsSpecification
    {
        bool AlreadyExists(string url, string path);
    }

    public class CoverAlreadyExistsSpecification : ICoverExistsSpecification
    {
        // Image CDNs commonly only allow GET on asset URLs and reject HEAD requests:
        //   - MangaDex `uploads.mangadex.org` returns 405 on HEAD for /covers/...
        //   - Some Cloudflare-fronted hosts return 403 on HEAD due to WAF rules
        //   - Some CDNs return 404 on HEAD when the path is GET-only
        // The HEAD here is a cheap cache-validation hint (compare Content-Length to
        // on-disk size). When the CDN rejects HEAD we cannot validate, so we treat the
        // local file as stale and let the caller redownload via GET — the original
        // behavior the existing "no Content-Length header" fallback already produces.
        // We suppress these status codes so HttpClient does not throw (HttpClient.cs:118),
        // turning a hard failure into a soft cache-miss.
        private static readonly HttpStatusCode[] SuppressedHeadStatusCodes =
        {
            HttpStatusCode.MethodNotAllowed,
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound
        };

        private readonly IDiskProvider _diskProvider;
        private readonly IHttpClient _httpClient;

        public CoverAlreadyExistsSpecification(IDiskProvider diskProvider, IHttpClient httpClient)
        {
            _diskProvider = diskProvider;
            _httpClient = httpClient;
        }

        public bool AlreadyExists(string url, string path)
        {
            if (!_diskProvider.FileExists(path))
            {
                return false;
            }

            var request = new HttpRequest(url)
            {
                SuppressHttpErrorStatusCodes = SuppressedHeadStatusCodes,
                LogHttpError = false
            };

            var response = _httpClient.Head(request);

            // CDN rejected HEAD (or asset is gone) — we cannot verify the local file
            // matches the remote, so report "not already exists" and let the caller
            // redownload via GET.
            if (response.HasHttpError)
            {
                return false;
            }

            var fileSize = _diskProvider.GetFileSize(path);
            return fileSize == response.Headers.ContentLength;
        }
    }
}
