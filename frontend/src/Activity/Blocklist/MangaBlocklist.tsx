// Sonarr divergence: NEW manga sibling per Phase 7 D-10 + Lock #1 — see DIVERGENCE.md.
// Role-match analog: thin wrapper over frontend/src/Activity/Blocklist/Blocklist.tsx (Plan 07-09
// task 1 extended Blocklist with the mediaType prop).
//
// Manga sibling preserves: Blocklist page rendering verbatim — no UI fork. The wrapper exists
// only to provide a stable component reference for the AppRoutes `/manga/activity/blocklist`
// Route entry and to lock the mediaType discriminator at the call-site (per D-10 + Lock #1
// separate-route pattern; React Query key auto-namespaces to ['/manga/blocklist'] via the path
// prop in useBlocklist).
//
// Manga sibling diverges from Blocklist:
//   * Default mediaType='manga' instead of 'series' (forced by always passing the prop)
//
// Phase 8 cleanup: when /manga/activity/blocklist is promoted (or /activity/blocklist is dropped),
// this wrapper merges into Blocklist.tsx (default mediaType becomes 'manga') and is deleted.
import React from 'react';
import Blocklist from './Blocklist';

function MangaBlocklist() {
  return <Blocklist mediaType="manga" />;
}

export default MangaBlocklist;
