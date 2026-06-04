// eslint-disable filenames/match-exported

// Sonarr divergence: manga search worker. Role-match analog: the TV fuse.worker
// that backed SeriesSearchInput. Diverges: matches the manga metadata IDs
// (mangaDexId / aniListId / malId) and operates over SuggestedManga, not
// SuggestedSeries.
import Fuse from 'fuse.js';
import { SuggestedManga } from './MangaSearchInput';

const fuseOptions = {
  shouldSort: true,
  includeMatches: true,
  ignoreLocation: true,
  threshold: 0.3,
  maxPatternLength: 32,
  minMatchCharLength: 1,
  keys: [
    'title',
    'alternateTitles.title',
    'mangaDexId',
    'aniListId',
    'malId',
    'tags.label',
  ],
};

function getSuggestions(manga: SuggestedManga[], value: string) {
  const limit = 10;
  let suggestions = [];

  if (value.length === 1) {
    for (let i = 0; i < manga.length; i++) {
      const m = manga[i];
      if (m.firstCharacter === value.toLowerCase()) {
        suggestions.push({
          item: manga[i],
          indices: [[0, 0]],
          matches: [
            {
              value: m.title,
              key: 'title',
            },
          ],
          refIndex: 0,
        });
        if (suggestions.length >= limit) {
          break;
        }
      }
    }
  } else {
    const fuse = new Fuse(manga, fuseOptions);
    suggestions = fuse.search(value, { limit });
  }

  return suggestions;
}

onmessage = function (e) {
  if (!e) {
    return;
  }

  const { manga, value } = e.data;

  const suggestions = getSuggestions(manga, value);

  const results = {
    value,
    suggestions,
  };

  self.postMessage(results);
};
