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

  const { data: blocklist } = useApiQuery<MangaBlocklist[]>({
    path: '/manga/blocklist',
    queryOptions: { staleTime: 30 * 1000 },
  });

  const { data: history } = useApiQuery<ChapterHistory[]>({
    path: '/manga/history',
    queryParams: { chapterId: chapter.id },
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
    !!blocklist &&
    blocklist.some((b) => b.chapterIds?.includes(chapter.id) ?? false);

  // The most recent history event for this chapter — used to detect a failed
  // last attempt. The retry-budget gate (Phase 6 D-13) is not yet exposed on
  // the wire; v1 uses last-event === 'downloadFailed' as the signal. When
  // the budget signal lands, AND the budget into the condition.
  const lastEvent = history?.[0]?.eventType;
  const isFailed = lastEvent === 'downloadFailed';

  const hasFile = chapter.chapterFileId != null;

  // Precedence: failed > blocklisted > have-file > queued > wanted > unmonitored
  if (isFailed) {
    return (
      <Icon
        name={icons.DANGER}
        kind={kinds.DANGER}
        title="Last download attempt failed"
      />
    );
  }

  if (isBlocklisted) {
    return (
      <Icon
        name={icons.BLOCKLIST}
        kind={kinds.DANGER}
        title="Best release for this chapter is blocklisted"
      />
    );
  }

  if (hasFile) {
    return (
      <Icon name={icons.FILE} kind={kinds.SUCCESS} title="Imported" />
    );
  }

  if (isQueued) {
    return (
      <Icon
        name={icons.SPINNER}
        kind={kinds.INFO}
        isSpinning={true}
        title="In queue / downloading"
      />
    );
  }

  if (chapter.monitored) {
    return (
      <Icon name={icons.MONITORED} kind={kinds.WARNING} title="Wanted" />
    );
  }

  return (
    <Icon
      name={icons.UNMONITORED}
      kind={kinds.DISABLED}
      title="Not monitored"
    />
  );
}

export default ChapterStatus;
