// Sonarr divergence: NEW manga sibling per Phase 7 D-10 + Lock #1 (Plan 12-08 sub-wave C extension) — see DIVERGENCE.md.
// Role-match analog: thin wrapper over frontend/src/Wanted/CutoffUnmet/CutoffUnmet.tsx (Plan 12-08
// task 2 extended CutoffUnmet with the mediaType prop).
//
// Manga sibling preserves: CutoffUnmet page rendering verbatim — no UI fork. The wrapper exists only
// to provide a stable component reference for the AppRoutes `/manga/wanted/cutoffunmet` Route entry
// and to lock the mediaType discriminator at the call-site (per D-10 + Lock #1 separate-route
// pattern; React Query key auto-namespaces to ['/manga/wanted/cutoff'] via the path prop in
// useCutoffUnmet).
//
// Manga sibling diverges from CutoffUnmet:
//   * Default mediaType='manga' instead of 'series' (forced by always passing the prop)
//
// Phase 8 cleanup: when /manga/wanted/cutoffunmet is promoted (or /wanted/cutoffunmet is dropped), this
// wrapper merges into CutoffUnmet.tsx (default mediaType becomes 'manga') and is deleted.
import React from 'react';
import CutoffUnmet from './CutoffUnmet';

function MangaCutoffUnmet() {
  return <CutoffUnmet mediaType="manga" />;
}

export default MangaCutoffUnmet;
