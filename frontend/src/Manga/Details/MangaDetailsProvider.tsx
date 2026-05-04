// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetailsProvider.tsx
// (Series wraps QueueDetailsProvider + EpisodeFileContext.Provider; manga
// sibling defers ChapterFile context to Plans 07-08+ — Files tab).
//
// Manga sibling preserves: provider-pass-through shape.
// Manga sibling diverges from SeriesDetailsProvider:
//   * No ChapterFile context — Plan 07-05 ships the Chapters tab; the
//     ChapterFile subresource lands with the Files tab in a future plan.
//   * No QueueDetails wrap — manga queue details (per-chapter download
//     progress) is wired via the URL-shaped React Query cache key
//     `['/manga/queue']` directly inside ChapterStatus. The provider exists
//     as a passthrough today so consumers (MangaDetails) can swap
//     dependencies later without a render-tree refactor.
//
// Phase 8 cleanup: collapse with SeriesDetailsProvider when Tv/ deletes.
import React, { PropsWithChildren } from 'react';

interface MangaDetailsProviderProps {
  mangaId: number;
}

function MangaDetailsProvider({
  children,
}: PropsWithChildren<MangaDetailsProviderProps>) {
  // v1: passthrough. The mangaId prop is reserved for future provider
  // wiring (queue details, chapter-file context) without forcing a
  // render-tree refactor on consumers.
  return <>{children}</>;
}

export default MangaDetailsProvider;
