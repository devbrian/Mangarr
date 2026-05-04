// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesGenres.tsx (verbatim port).
import React from 'react';
import Label from 'Components/Label';
import Tooltip from 'Components/Tooltip/Tooltip';
import { kinds, sizes, tooltipPositions } from 'Helpers/Props';

interface MangaGenresProps {
  className?: string;
  genres: string[];
}

function MangaGenres({ className, genres }: MangaGenresProps) {
  const [firstGenre, ...otherGenres] = genres;

  if (otherGenres.length) {
    return (
      <Tooltip
        anchor={<span className={className}>{firstGenre}</span>}
        tooltip={
          <div>
            {otherGenres.map((tag) => {
              return (
                <Label key={tag} kind={kinds.INFO} size={sizes.LARGE}>
                  {tag}
                </Label>
              );
            })}
          </div>
        }
        kind={kinds.INVERSE}
        position={tooltipPositions.TOP}
      />
    );
  }

  return <span className={className}>{firstGenre}</span>;
}

export default MangaGenres;
