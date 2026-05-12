// Sonarr divergence: Phase 17.3 Plan 17.3-04 (D-07) — file renamed from
// SeriesTagList.tsx to MangaTagList.tsx; component + props interface renamed.
import React from 'react';
import { useTagList } from 'Tags/useTags';
import TagList from './TagList';

interface MangaTagListProps {
  tags: number[];
}

function MangaTagList({ tags }: MangaTagListProps) {
  const tagList = useTagList();

  return <TagList tags={tags} tagList={tagList} />;
}

export default MangaTagList;
