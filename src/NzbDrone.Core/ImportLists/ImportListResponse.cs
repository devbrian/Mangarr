using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListResponse.cs.
    public class ImportListResponse
    {
        private readonly ImportListRequest _importListRequest;
        private readonly HttpResponse _httpResponse;

        public ImportListResponse(ImportListRequest importListRequest, HttpResponse httpResponse)
        {
            _importListRequest = importListRequest;
            _httpResponse = httpResponse;
        }

        public ImportListRequest Request => _importListRequest;

        public HttpRequest HttpRequest => _httpResponse.Request;

        public HttpResponse HttpResponse => _httpResponse;

        public string Content => _httpResponse.Content;
    }
}
