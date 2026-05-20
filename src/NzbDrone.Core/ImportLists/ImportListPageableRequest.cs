using System.Collections;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListPageableRequest.cs.
    public class ImportListPageableRequest : IEnumerable<ImportListRequest>
    {
        private readonly IEnumerable<ImportListRequest> _enumerable;

        public ImportListPageableRequest(IEnumerable<ImportListRequest> enumerable)
        {
            _enumerable = enumerable;
        }

        public IEnumerator<ImportListRequest> GetEnumerator()
        {
            return _enumerable.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _enumerable.GetEnumerator();
        }
    }
}
