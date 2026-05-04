// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesPoster.tsx (verbatim port).
//
// Manga sibling preserves: 250 / 500 px renderer choice + base64 placeholder.
// Manga sibling diverges from SeriesPoster:
//   * Reads from `manga.images[].coverType === 'poster'` per Lock #3
//     (cover URLs are rewritten by MangaController.MapResource via
//     _coverMapper.ConvertToLocalUrls — verified Sonarr.Api.V5/Manga/
//     MangaController.cs:151).
//
// Phase 8 cleanup: collapse with SeriesPoster when Tv/ deletes.
import React from 'react';
import MangaImage, { MangaImageProps } from './MangaImage';

const posterPlaceholder =
  'data:image/png;base64,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==';

interface MangaPosterProps
  extends Omit<MangaImageProps, 'coverType' | 'placeholder'> {
  size?: 250 | 500;
}

function MangaPoster({ size = 250, ...otherProps }: MangaPosterProps) {
  return (
    <MangaImage
      {...otherProps}
      size={size}
      coverType="poster"
      placeholder={posterPlaceholder}
    />
  );
}

export default MangaPoster;
