// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesTags.tsx (verbatim
// shape — manga shares the Tag entity).
//
// Manga sibling preserves: useTagList lookup → Label list render.
// Manga sibling diverges from SeriesTags: identifier renames only.
//
// Phase 8 cleanup: collapse with SeriesTags when Tv/ deletes.
import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';
import { useSingleManga } from 'Manga/useManga';
import { useTagList } from 'Tags/useTags';
import sortByProp from 'Utilities/Array/sortByProp';

interface MangaTagsProps {
  mangaId: number;
}

function MangaTags({ mangaId }: MangaTagsProps) {
  const manga = useSingleManga(mangaId);
  const tagList = useTagList();

  if (!manga) {
    return null;
  }

  const tags = manga.tags
    .map((tagId) => tagList.find((tag) => tag.id === tagId))
    .filter((tag) => !!tag)
    .sort(sortByProp('label'))
    .map((tag) => tag.label);

  return (
    <div>
      {tags.map((tag) => {
        return (
          <Label key={tag} kind={kinds.INFO} size={sizes.LARGE}>
            {tag}
          </Label>
        );
      })}
    </div>
  );
}

export default MangaTags;
