import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface QueueStatus {
  totalCount: number;
  count: number;
  unknownCount: number;
  errors: boolean;
  warnings: boolean;
  unknownErrors: boolean;
  unknownWarnings: boolean;
}

// Repointed (2026-05-10) from the deleted TV `/queue/status` route onto the canonical
// Mangarr `/manga/queue/status` route (Phase 13 Plan 13-09 `MangaQueueStatusController`).
// The TV `QueueStatusController` was removed during the Phase 5/13 cutover; only
// `src/Mangarr.Api.V5/Manga/Queue/MangaQueueStatusController.cs` remains.
//
// The `MangaQueueStatusResource` counter DTO is field-for-field identical to the
// retired `QueueStatusResource` (TotalCount / Count / UnknownCount / Errors / Warnings /
// UnknownErrors / UnknownWarnings — see `MangaQueueStatusResource.cs:21-30`), so the
// `QueueStatus` interface above applies unchanged.
//
// React Query key now derives from path `'/manga/queue/status'`, matching the SignalR
// invalidation handler at `Components/SignalRListener.tsx:366-380` which writes pushed
// status payloads into `['/manga/queue/status']` via `setQueryData`. Pre-fix the cache
// key was `['/queue/status']` and the SignalR push was dropped on the floor.
//
// Phase 8/15 cleanup: when `Tv/` deletes per D-13-16, the manga prefix collapses and
// the path becomes `/queue/status` again under the unified namespace.
export default function useQueueStatus() {
  const { data } = useApiQuery<QueueStatus>({
    path: '/manga/queue/status',
  });

  if (!data) {
    return {
      count: 0,
      errors: false,
      warnings: false,
    };
  }

  const { errors, warnings, unknownErrors, unknownWarnings, totalCount } = data;

  return {
    count: totalCount,
    errors: errors || unknownErrors,
    warnings: warnings || unknownWarnings,
  };
}
