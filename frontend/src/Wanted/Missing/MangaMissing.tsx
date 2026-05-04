// Sonarr divergence: NEW manga sibling per Phase 7 D-10 + Lock #1 — see DIVERGENCE.md.
// Role-match analog: thin wrapper over frontend/src/Wanted/Missing/Missing.tsx (Plan 07-10
// task 1 extended Missing with the mediaType prop).
//
// Manga sibling preserves: Missing page rendering verbatim — no UI fork. The wrapper exists only
// to provide a stable component reference for the AppRoutes `/manga/wanted/missing` Route entry
// and to lock the mediaType discriminator at the call-site (per D-10 + Lock #1 separate-route
// pattern; React Query key auto-namespaces to ['/manga/wanted/missing'] via the path prop in
// useMissing).
//
// Manga sibling diverges from Missing:
//   * Default mediaType='manga' instead of 'series' (forced by always passing the prop)
//
// Phase 8 cleanup: when /manga/wanted/missing is promoted (or /wanted/missing is dropped), this
// wrapper merges into Missing.tsx (default mediaType becomes 'manga') and is deleted.
import React from 'react';
import Missing from './Missing';

function MangaMissing() {
  return <Missing mediaType="manga" />;
}

export default MangaMissing;
