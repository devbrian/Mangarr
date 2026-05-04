// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesTitleLink.tsx (verbatim port).
//
// Manga sibling preserves: title-as-link rendering shape.
// Manga sibling diverges from SeriesTitleLink:
//   * Routes to `/manga/${titleSlug}` (Plan 07-04 D-09 additive route).
//
// Phase 8 cleanup: collapse with SeriesTitleLink when Tv/ deletes.
import React from 'react';
import Link, { LinkProps } from 'Components/Link/Link';

export interface MangaTitleLinkProps extends LinkProps {
  // Optional because the manga shape (Plan 07-03 Manga.ts) marks titleSlug
  // optional until the Phase 2 backend gap-fill emits it on every record.
  titleSlug?: string;
  title: string;
}

export default function MangaTitleLink({
  titleSlug,
  title,
  ...linkProps
}: MangaTitleLinkProps) {
  const link = `/manga/${titleSlug ?? ''}`;

  return (
    <Link to={link} {...linkProps}>
      {title}
    </Link>
  );
}
