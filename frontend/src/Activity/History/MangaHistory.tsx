// Sonarr divergence: NEW manga sibling per Phase 7 D-10 + Lock #1 — see DIVERGENCE.md.
// Role-match analog: thin wrapper over frontend/src/Activity/History/History.tsx (Plan 07-09 task
// 1 extended History with the mediaType prop).
//
// Manga sibling preserves: History page rendering verbatim — no UI fork. The wrapper exists only
// to provide a stable component reference for the AppRoutes `/manga/activity/history` Route entry
// and to lock the mediaType discriminator at the call-site (per D-10 + Lock #1 separate-route
// pattern; React Query key auto-namespaces to ['/manga/history'] via the path prop in useHistory).
//
// Manga sibling diverges from History:
//   * Default mediaType='manga' instead of 'series' (forced by always passing the prop)
//
// Phase 8 cleanup: when /manga/activity/history is promoted (or /activity/history is dropped),
// this wrapper merges into History.tsx (default mediaType becomes 'manga') and is deleted.
import React from 'react';
import History from './History';

function MangaHistory() {
  return <History mediaType="manga" />;
}

export default MangaHistory;
