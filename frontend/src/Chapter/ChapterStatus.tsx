// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeStatus.tsx (Series-shape
// status icon — uses queue + episode file + airDate; manga sibling derives
// from queue + history + blocklist + chapterFileId per RESEARCH Lock #4).
//
// 6-state badge set per UI-SPEC §Chapter status badge set + RESEARCH Lock #4.
// Status precedence (when multiple states could apply):
//   failed > blocklisted > have-file > queued > wanted > unmonitored
// NOTE: BOTH `failed` and `blocklisted` are `!hasFile`-guarded (see isFailed +
// isBlocklisted below) so a present ChapterFile always wins over a stale
// historical downloadFailed event OR a lingering blocklist row — Sonarr
// EpisodeStatus parity (debug: chapter-status-stale-failed + auto-retry-one-
// release-exhaust follow-up: a chapter that the auto-retry recovered via a
// next-best release still carries the blocklist row for the release that failed).
//
// Manga sibling preserves: small icon-button rendering pattern; Helpers/Props
// icons + kinds.
// Manga sibling diverges from EpisodeStatus:
//   * No airDate / hasAired branch (manga has no airing concept).
//   * No EpisodeFile detail panel — `chapterFileId` presence alone toggles
//     have-file. Files-tab UI (Plan 07-08+) renders chapter-file detail.
//   * Reads queue + blocklist via the URL-shaped React Query cache keys from
//     Plan 07-02 SignalR contract; SignalR-pushed updates invalidate the keys
//     -> status icon re-renders within 1 second of backend events.
//   * Reads per-chapter most-recent history event from MangaChapterHistoryContext
//     (lifted parent-fetched whole-manga history bucketed by chapterId)
//     instead of firing its own per-row useApiQuery({ path: '/manga/history',
//     queryParams: { chapterId } }) — issue #51 fix. The prior per-row pattern
//     fanned out to N HTTP requests when the Chapters tab opened (one per
//     row); the parent-provider pattern mirrors Sonarr's QueueDetailsProvider
//     -> useQueueItemForEpisode parent-read shape (canonical mirror).
//
// Phase 8 cleanup: collapse with EpisodeStatus when Tv/ deletes.
import React from 'react';
import Icon from 'Components/Icon';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { useLastChapterHistoryEvent } from 'Manga/Details/MangaChapterHistoryContext';
import MangaBlocklist from 'typings/MangaBlocklist';
import MangaQueueItem from 'typings/MangaQueueItem';
import translate from 'Utilities/String/translate';
import Chapter from './Chapter';

export interface ChapterStatusProps {
  chapter: Chapter;
}

function ChapterStatus({ chapter }: ChapterStatusProps) {
  // Plan 07-02 SignalR contract: each of these cache keys is invalidated by
  // its matching backend resource handler in `Components/SignalRListener.tsx`,
  // so the status icon below reflects backend events within ~1 s. The keys
  // are URL-shaped strings (NOT EpisodeEntity-style enums).
  //
  // /api/v5/manga/queue returns the paged `Ok<PagingResource<MangaQueueResource>>`
  // envelope (page / pageSize / totalRecords / records[]) post-PR-#74
  // (`fix(manga-queue): union pending releases + page envelope + frontend null-guards`),
  // NOT a flat array. Reading it as `MangaQueueItem[]` and calling .some() on the
  // wrapper object throws `TypeError: queue.some is not a function` and crashes
  // the entire ChapterStatus component — which in turn breaks every page that
  // mounts ChapterStatus (Wanted/Missing, Wanted/CutoffUnmet, Manga Details
  // Chapters tab). Type as the paged shape and dereference .records — same
  // treatment as the blocklist read below (F-NEW-1 mirror from
  // quick-260507-tff-rerun). useApiQuery is a raw passthrough (does not
  // auto-unwrap PagingResource envelopes).
  const { data: queue } = useApiQuery<{ records?: MangaQueueItem[] }>({
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

  // Issue #51 fix: read the most-recent history event for this chapter from
  // the MangaDetailsProvider-level context. The provider runs ONE
  // /api/v5/manga/history?mangaIds=<id> fetch on tab-mount and buckets
  // results by chapterId; we get O(1) lookup here instead of an HTTP round-
  // trip per row.
  //
  // WR-05 sort-discipline preserved: provider's usePagedApiQuery passes
  // sortKey='date' + sortDirection='descending', so the bucketed array is in
  // most-recent-first order without an additional client-side sort.
  //
  // WR-04 (per-row fan-out) IS NOW FIXED via the lift to
  // MangaDetailsProvider — single mangaIds-filtered fetch instead of N
  // chapterId-filtered fetches.
  const lastEvent = useLastChapterHistoryEvent(chapter.id);

  const hasFile = chapter.chapterFileId != null;

  // A present ChapterFile always wins over a stale historical failure. Sonarr's
  // EpisodeStatus never renders a persistent history-derived "failed" badge over
  // a chapter that already has a file — an active failure surfaces via the live
  // queue, not a stale History row. RESEARCH Lock #4 specified `failed` only
  // among the no-file states (its `retryBudgetExhausted` qualifier was dropped in
  // the original impl, and the `!hasFile` qualifier was missing entirely), so a
  // re-listed/failed gateway job for an already-imported chapter was painting the
  // row red even though the CBZ is on disk (debug: chapter-status-stale-failed).
  // Guarding `isFailed` with `!hasFile` keeps the 6-state precedence honest:
  // `failed` is reachable only when the chapter genuinely has no file.
  const isFailed = !hasFile && lastEvent?.eventType === 'downloadFailed';

  const isQueued =
    !!queue?.records &&
    queue.records.some(
      (q) =>
        q.chapterId === chapter.id ||
        (q.chapterIds?.includes(chapter.id) ?? false)
    );

  // `!hasFile`-guarded for the SAME reason as isFailed above: the "Best release
  // for this chapter is blocklisted" badge is an actionable WANTED-state warning
  // (the chapter can't be auto-grabbed because its best release is blocklisted).
  // Once the chapter has a file — e.g. the auto-retry grabbed a next-best release
  // after the failed one was blocklisted — the blocklist row legitimately persists
  // as history, but it must NOT paint the row red over an imported CBZ. Sonarr
  // EpisodeStatus never renders a blocklist warning over a downloaded episode.
  const isBlocklisted =
    !hasFile &&
    !!blocklist?.records &&
    blocklist.records.some((b) => b.chapterIds?.includes(chapter.id) ?? false);

  // Precedence: failed > blocklisted > have-file > queued > wanted > unmonitored
  // (`failed` is now `!hasFile`-guarded above, so a present file wins over a stale
  // historical failure — see the isFailed comment.)
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
    // Sonarr-canonical: monitored && !hasFile renders as Missing.
    // hasFile is the SOLE file-presence axis (chapter.chapterFileId != null) —
    // mirrors Episode.Monitored && EpisodeFileId == 0 (Sonarr Tv/Episode.cs).
    // Phase 16 D-04 alias-flip on the (now-removed) per-translation collection
    // reverted in Phase 16.1 per SPEC REVERT-05.
    // Pill literal is `'Missing'` for max Sonarr parity
    // (per CONTEXT.md additional_context Pitfall #5 + PATTERNS.md Pitfall 8).
    // 6-state precedence preserved: failed > blocklisted > have-file > queued >
    // wanted/missing > unmonitored. NO 7th state.
    return (
      <Icon
        name={icons.MONITORED}
        kind={kinds.WARNING}
        title={translate('Missing')}
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
