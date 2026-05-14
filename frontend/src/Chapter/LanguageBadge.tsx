// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeLanguages.tsx (closest
// precedent — generic Label list with single + multi-language renderings).
//
// Manga sibling preserves: small Label-based badge component shape.
// Manga sibling diverges from EpisodeLanguages:
//   * Pill-shaped per UI-SPEC §Translation language badge — extraSmallFontSize
//     11 px (inherited from Sonarr fonts.js); uppercase BCP-47 code (e.g. EN,
//     ES, JA) instead of full language name.
//   * Background flips to the manga accent (themeBlue — Plan 11 rebrands cyan
//     to manga pink #f06292 per UI-SPEC §Color) when the chapter's language
//     matches the user's #1-ranked language on the default Translation
//     Profile (Phase 5 D-01). The position-0 entry of `profile.languages` IS
//     the highest-rank language (TranslationProfileResource: ordered list).
//   * No multi-language Popover — chapters carry a single BCP-47 code at the
//     wire layer (`Chapter.translatedLanguage`); an empty / undefined value
//     renders nothing.
//
// Phase 8 cleanup: this stays — manga-specific. Phase 11 swaps themeBlue
// from Sonarr cyan to manga pink and the accent flip lands automatically.
import React from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import styles from './LanguageBadge.css';

interface TranslationProfileResource {
  id: number;
  name?: string;
  languages: string[];
  allowLanguagesNotInProfile?: boolean;
}

export interface LanguageBadgeProps {
  language?: string;
  className?: string;
}

function LanguageBadge({ language, className }: LanguageBadgeProps) {
  const { data: translationProfiles } = useApiQuery<
    TranslationProfileResource[]
  >({
    path: '/translationprofile',
    queryOptions: {
      staleTime: Infinity,
    },
  });

  if (!language) {
    return null;
  }

  // The position-0 entry of `profile.languages` is the user's top-ranked
  // language for that profile. The default profile is the first one in the
  // list (or fall back to position-0 across the array if the backend hasn't
  // marked a default in v1 — see Phase 5 D-11).
  const topRanked =
    translationProfiles && translationProfiles.length > 0
      ? translationProfiles[0]?.languages?.[0]
      : undefined;

  const isAccent =
    !!topRanked && topRanked.toLowerCase() === language.toLowerCase();

  return (
    <span
      className={[isAccent ? styles.accent : styles.badge, className]
        .filter(Boolean)
        .join(' ')}
      title={language.toUpperCase()}
    >
      {language.toUpperCase()}
    </span>
  );
}

export default LanguageBadge;
