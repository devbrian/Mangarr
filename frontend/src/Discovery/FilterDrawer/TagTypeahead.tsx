// Phase 42 Plan 42-06 — Discovery tag typeahead (NEW-in-Mangarr; FilterModal
// divergence — see DIVERGENCE.md). Solves the 2,692-tag problem (sketch 001 +
// spike 004): client-side Fuse.js typeahead over the cached slim tag list, NO
// query-as-you-type backend round-trips.
//
// spike-004 contract (LOCKED):
//   * Bind the filter value to the integer `id` — NOT name, NOT namePath.
//     `tag=<name>` returns a silently-wrong count; `tag_not=<name>` 400s
//     ("expected number"). onAdd therefore emits { id:number, name:string }.
//   * Rank suggestions by `seriesCount` desc.
//   * Hide the 199 `seriesCount === 0` category nodes.
//   * 14 duplicate names exist ("Mental Illness", "Chibi", …) — show `namePath`
//     to disambiguate when a name is non-unique.
//   * `tag_mode` AND/OR (default `and`) — surfaced here as a segmented toggle,
//     value+onChange owned by the parent (FilterDrawer -> discoveryOptionsStore).
import classNames from 'classnames';
import Fuse from 'fuse.js';
import React, { useCallback, useMemo, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import { DiscoveryTag } from 'Discovery/DiscoveryModels';
import { useDiscoveryTags } from 'Discovery/useDiscovery';
import { icons } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './TagTypeahead.css';

const SUGGESTION_LIMIT = 20;

const FUSE_OPTIONS = {
  shouldSort: false,
  ignoreLocation: true,
  threshold: 0.3,
  minMatchCharLength: 1,
  keys: [
    { name: 'name', weight: 0.7 },
    { name: 'namePath', weight: 0.3 },
  ],
};

export interface TagTypeaheadProps {
  selectedIds: number[];
  tagMode: 'and' | 'or';
  onAdd: (tag: { id: number; name: string }) => void;
  onTagModeChange: (mode: 'and' | 'or') => void;
}

function bySeriesCountDesc(a: DiscoveryTag, b: DiscoveryTag) {
  return b.seriesCount - a.seriesCount;
}

interface SuggestionProps {
  tag: DiscoveryTag;
  showNamePath: boolean;
  onSelect: (tag: DiscoveryTag) => void;
}

function Suggestion({ tag, showNamePath, onSelect }: SuggestionProps) {
  const handleClick = useCallback(() => {
    onSelect(tag);
  }, [tag, onSelect]);

  return (
    <li>
      <button type="button" className={styles.suggestion} onClick={handleClick}>
        <span className={styles.suggestionMain}>
          <Icon className={styles.suggestionIcon} name={icons.ADD} size={11} />
          {tag.name}
        </span>

        {showNamePath ? (
          <span className={styles.namePath}>{tag.namePath}</span>
        ) : null}

        <span className={styles.seriesCount}>{tag.seriesCount}</span>
      </button>
    </li>
  );
}

function TagTypeahead({
  selectedIds,
  tagMode,
  onAdd,
  onTagModeChange,
}: TagTypeaheadProps) {
  const { data: tags } = useDiscoveryTags();
  const [term, setTerm] = useState('');

  // Hide the seriesCount === 0 category nodes (spike-004 Q4); these are
  // non-filterable hierarchy parents that would only add noise.
  const filterableTags = useMemo(
    () => tags.filter((tag) => tag.seriesCount > 0),
    [tags]
  );

  // The 14 duplicate names that MUST show namePath to disambiguate.
  const duplicateNames = useMemo(() => {
    const counts = new Map<string, number>();
    for (const tag of filterableTags) {
      counts.set(tag.name, (counts.get(tag.name) ?? 0) + 1);
    }
    return new Set(
      Array.from(counts.entries())
        .filter(([, count]) => count > 1)
        .map(([name]) => name)
    );
  }, [filterableTags]);

  const fuse = useMemo(
    () => new Fuse(filterableTags, FUSE_OPTIONS),
    [filterableTags]
  );

  const selected = useMemo(() => new Set(selectedIds), [selectedIds]);

  const suggestions = useMemo(() => {
    const trimmed = term.trim();

    const pool = trimmed
      ? fuse.search(trimmed).map((result) => result.item)
      : [...filterableTags];

    return pool
      .filter((tag) => !selected.has(tag.id))
      .sort(bySeriesCountDesc)
      .slice(0, SUGGESTION_LIMIT);
  }, [term, fuse, filterableTags, selected]);

  const handleTermChange = useCallback(({ value }: InputChanged<string>) => {
    setTerm(value);
  }, []);

  const handleAdd = useCallback(
    (tag: DiscoveryTag) => {
      onAdd({ id: tag.id, name: tag.name });
      setTerm('');
    },
    [onAdd]
  );

  const handleAndPress = useCallback(() => {
    onTagModeChange('and');
  }, [onTagModeChange]);

  const handleOrPress = useCallback(() => {
    onTagModeChange('or');
  }, [onTagModeChange]);

  return (
    <div className={styles.container}>
      <div className={styles.modeRow}>
        <div className={styles.segmented} role="group">
          <button
            type="button"
            className={classNames(
              styles.segment,
              tagMode === 'and' && styles.segmentActive
            )}
            data-testid="discovery-tagmode-and"
            onClick={handleAndPress}
          >
            {translate('DiscoveryTagModeAnd')}
          </button>
          <button
            type="button"
            className={classNames(
              styles.segment,
              tagMode === 'or' && styles.segmentActive
            )}
            data-testid="discovery-tagmode-or"
            onClick={handleOrPress}
          >
            {translate('DiscoveryTagModeOr')}
          </button>
        </div>
      </div>

      <TextInput
        name="tagSearch"
        value={term}
        placeholder={translate('DiscoveryTagSearchPlaceholder')}
        data-testid="discovery-tag-search"
        onChange={handleTermChange}
      />

      {suggestions.length > 0 ? (
        <ul
          className={styles.suggestions}
          data-testid="discovery-tag-suggestions"
        >
          {suggestions.map((tag) => (
            <Suggestion
              key={tag.id}
              tag={tag}
              showNamePath={duplicateNames.has(tag.name)}
              onSelect={handleAdd}
            />
          ))}
        </ul>
      ) : null}
    </div>
  );
}

export default TagTypeahead;
