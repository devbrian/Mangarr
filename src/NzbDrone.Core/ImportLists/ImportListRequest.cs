using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListRequest.cs.
    public class ImportListRequest
    {
        public HttpRequest HttpRequest { get; private set; }

        public ImportListRequest(string url, HttpAccept httpAccept)
        {
            HttpRequest = new HttpRequest(url, httpAccept);
        }

        public ImportListRequest(HttpRequest httpRequest)
        {
            HttpRequest = httpRequest;
        }

        public HttpUri Url => HttpRequest.Url;
    }
}
