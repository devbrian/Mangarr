// Sonarr divergence: NEW manga sibling per Phase 7 D-10 + Lock #1 — see DIVERGENCE.md.
// Role-match analog: thin wrapper over frontend/src/Activity/Queue/Queue.tsx (Plan 07-09 task 1
// extended Queue with the mediaType prop).
//
// Manga sibling preserves: Queue page rendering verbatim — no UI fork. The wrapper exists only
// to provide a stable component reference for the AppRoutes `/manga/activity/queue` Route entry
// and to lock the mediaType discriminator at the call-site (per D-10 + Lock #1 separate-route
// pattern; React Query key auto-namespaces to ['/manga/queue'] via the path prop in useQueue).
//
// Manga sibling diverges from Queue:
//   * Default mediaType='manga' instead of 'series' (forced by always passing the prop)
//
// Phase 8 cleanup: when /manga/activity/queue is promoted (or /activity/queue is dropped), this
// wrapper merges into Queue.tsx (default mediaType becomes 'manga') and is deleted.
import React from 'react';
import Queue from './Queue';

function MangaQueue() {
  return <Queue mediaType="manga" />;
}

export default MangaQueue;
