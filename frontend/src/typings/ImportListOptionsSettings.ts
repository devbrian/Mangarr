export type ListSyncLevel =
  | 'disabled'
  | 'logOnly'
  | 'keepAndUnmonitor'
  | 'keepAndTag';

export default interface ImportListOptionsSettings {
  // Optional — present on GET responses (RestResource serializes Id=1 for KV
  // config controllers per Plan 27.1-01 backend). The FE never authors `id`
  // on save; the Redux thunk roundtrips it via `pendingChanges`.
  id?: number;
  listSyncLevel: ListSyncLevel;
  listSyncTag: number;
}
