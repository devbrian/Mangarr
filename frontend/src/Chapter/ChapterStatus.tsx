// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeStatus.tsx (Series-shape
// status icon — uses queue + episode file + airDate; manga sibling derives
// from queue + history + blocklist + chapterFileId per RESEARCH Lock #4).
//
// 6-state badge set per UI-SPEC §Chapter status badge set + RESEARCH Lock #4.
// Status precedence (when multiple states could apply):
//   failed > blocklisted > have-file > queued > wanted > unmonitored
//
// Manga sibling preserves: small icon-button rendering pattern; Helpers/Props
// icons + kinds.
// Manga sibling diverges from EpisodeStatus:
//   * No airDate / hasAired branch (manga has no airing concept).
//   * No EpisodeFile detail panel — `chapterFileId` presence alone toggles
//     have-file. Files-tab UI (Plan 07-08+) renders chapter-file detail.
//   * Reads three React Query caches concurrently (queue / history /
//     blocklist) via the URL-shaped cache keys from Plan 07-02 SignalR
//     contract; SignalR-pushed updates invalidate the keys → status icon
//     re-renders within 1 second of backend events.
//
// Phase 8 cleanup: collapse with EpisodeStatus when Tv/ deletes.
import React from 'react';
import Icon from 'Components/Icon';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Chapter from './Chapter';
import ChapterHistory from 'typings/ChapterHistory';
import MangaBlocklist from 'typings/MangaBlocklist';
import MangaQueueItem from 'typings/MangaQueueItem';

export interface ChapterStatusProps {
  chapter: Chapter;
}

function ChapterStatus({ chapter }: ChapterStatusProps) {
  // Plan 07-02 SignalR contract: each of these cache keys is invalidated by
  // its matching backend resource handler in `Components/SignalRListener.tsx`,
  // so the status icon below reflects backend events within ~1 s. The keys
  // are URL-shaped strings (NOT EpisodeEntity-style enums).
  const { data: queue } = useApiQuery<MangaQueueItem[]>({
    path: '/manga/queue',
    queryOptions: { staleTime: 30 * 1000 },
  });

  // /api/v5/manga/blocklist returns the paged `Ok<PagingResource<MangaBlocklistResource>>`
  // shape (page / pageSize / totalRecords / records[]) — NOT a flat array. Reading it as
  // `MangaBlocklist[]` and calling .some() on the wrapper object throws TypeError and
  // crashes the entire ChapterStatus component (F-NEW-1 from quick-260507-tff-rerun).
  // Type as the paged shape and dereference .records.
  const { data: blocklist } = useApiQuery<{ records?: MangaBlocklist[] }>({
    path: '/manga/blocklist',
    queryOptions: { staleTime: 30 * 1000 },
  });

  // WR-05 fix: explicitly request descending-by-date so `history?.[0]` is
  // guaranteed to be the most recent event. The /manga/history endpoint is
  // paged and does not contractually default to descending sort order — the
  // previous code would surface stale "download failed" indicators forever
  // if the backend returned ascending order.
  //
  // WR-04 (per-row fan-out) is intentionally NOT fixed here: the backend
  // history endpoint is PAGED and supports `chapterId` as a server-side
  // filter. Fetching whole-manga history client-side and filtering by
  // chapterId would only inspect the first page (typically 20-50 rows) and
  // could miss the most recent event for older chapters. Lifting the fetch
  // to a parent component (per the reviewer's suggestion) is a structural
  // refactor deferred to a follow-up plan.
  // /api/v5/manga/history returns the paged `Ok<PagingResource<ChapterHistoryResource>>`
  // shape — same wrapper as blocklist above. Read .records[0] for the most recent
  // event after the descending-by-date sort. The previous flat-array typing happened
  // to gracefully degrade to "no failure detected" because `[].records` is undefined
  // and the optional chain returns undefined, but it silently masked failed downloads.
  const { data: history } = useApiQuery<{ records?: ChapterHistory[] }>({
    path: '/manga/history',
    queryParams: {
      chapterId: chapter.id,
      sortKey: 'date',
      sortDirection: 'descending',
    },
    queryOptions: { staleTime: 30 * 1000 },
  });

  const isQueued =
    !!queue &&
    queue.some(
      (q) =>
        q.chapterId === chapter.id ||
        (q.chapterIds?.includes(chapter.id) ?? false)
    );

  const isBlocklisted =
    !!blocklist?.records &&
    blocklist.records.some((b) => b.chapterIds?.includes(chapter.id) ?? false);

  // The most recent history event for this chapter — used to detect a failed
  // last attempt. The retry-budget gate (Phase 6 D-13) is not yet exposed on
  // the wire; v1 uses last-event === 'downloadFailed' as the signal. When
  // the budget signal lands, AND the budget into the condition.
  const lastEvent = history?.records?.[0]?.eventType;
  const isFailed = lastEvent === 'downloadFailed';

  const hasFile = chapter.chapterFileId != null;

  // Precedence: failed > blocklisted > have-file > queued > wanted > unmonitored
  // WR-06 fix: every status title flows through translate() so the badges
  // localize. Keys not yet present in en.json render as the key string
  // (translate() falls back to key) — Plan 07 follow-up adds them to the
  // en.json catalog. Reuses existing 'Imported' and 'Wanted' keys.
  if (isFailed) {
    return (
      <Icon
        name={icons.DANGER}
        kind={kinds.DANGER}
        title={translate('LastDownloadAttemptFailed')}
      />
    );
  }

  if (isBlocklisted) {
    return (
      <Icon
        name={icons.BLOCKLIST}
        kind={kinds.DANGER}
        title={translate('BestReleaseForChapterBlocklisted')}
      />
    );
  }

  if (hasFile) {
    return (
      <Icon
        name={icons.FILE}
        kind={kinds.SUCCESS}
        title={translate('Imported')}
      />
    );
  }

  if (isQueued) {
    return (
      <Icon
        name={icons.SPINNER}
        kind={kinds.INFO}
        isSpinning={true}
        title={translate('ChapterInQueueDownloading')}
      />
    );
  }

  if (chapter.monitored) {
    return (
      <Icon
        name={icons.MONITORED}
        kind={kinds.WARNING}
        title={translate('Wanted')}
      />
    );
  }

  return (
    <Icon
      name={icons.UNMONITORED}
      kind={kinds.DISABLED}
      title={translate('ChapterIsNotMonitored')}
    />
  );
}

export default ChapterStatus;
