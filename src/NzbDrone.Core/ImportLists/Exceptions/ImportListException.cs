using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.ImportLists.Exceptions
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/Exceptions/ImportListException.cs.
    public class ImportListException : NzbDroneException
    {
        private readonly ImportListResponse _importListResponse;

        public ImportListException(ImportListResponse response, string message, params object[] args)
            : base(message, args)
        {
            _importListResponse = response;
        }

        public ImportListException(ImportListResponse response, string message)
            : base(message)
        {
            _importListResponse = response;
        }

        public ImportListResponse Response => _importListResponse;
    }
}
