// Phase 42 Plan 42-06 — Discovery filter drawer (NEW-in-Mangarr; the deliberate
// DIVERGENCE from Sonarr's Components/Filter modal+builder — see DIVERGENCE.md).
// That builder targets locally-held collections with saved named filters;
// Discovery builds a query against MangaBaka's REMOTE attribute search where
// include/exclude pairs (genre/genre_not) and a ~2,692-row Fuse.js typeahead
// have no saved-filter-builder analog.
//
// Reuses the sub-pieces (EnhancedSelectInput, NumberInput, CheckInput, Fuse.js)
// but NOT the Sonarr filter-modal container (42-PATTERNS §Filter-modal divergence).
//
// Sketch 001 (winner C, LOCKED): right-slide panel holding tristate chips for
// Type/Genres/Status/ContentRating, a tag typeahead with AND/OR mode, year +
// score range inputs, a sort dropdown, and an Include-adult-content toggle.
// Every selection writes through setDiscoveryOption -> discoveryOptionsStore so
// the toolbar Search button (42-05) submits a complete filter payload.
import classNames from 'classnames';
import React, { useCallback } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import EnhancedSelectInput from 'Components/Form/Select/EnhancedSelectInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import {
  DiscoveryTagSelection,
  TristateMap,
  TristateMode,
} from 'Discovery/DiscoveryModels';
import {
  setDiscoveryOption,
  useDiscoveryOptions,
} from 'Discovery/discoveryOptionsStore';
import { useDiscoveryGenres } from 'Discovery/useDiscovery';
import { icons } from 'Helpers/Props';
import { CheckInputChanged, EnhancedSelectInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import TagTypeahead from './TagTypeahead';
import TristateChip, { TristateChipState } from './TristateChip';
import styles from './FilterDrawer.css';

interface FilterOption {
  value: string;
  label: string;
}

// Hardcoded enums (note §Enums — no endpoint). Type/format, status, and content
// rating are fixed sets; demographics surface as GENRE values, not a dimension.
const TYPE_OPTIONS: FilterOption[] = [
  { value: 'manga', label: 'Manga' },
  { value: 'novel', label: 'Novel' },
  { value: 'manhwa', label: 'Manhwa' },
  { value: 'manhua', label: 'Manhua' },
  { value: 'oel', label: 'OEL' },
  { value: 'other', label: 'Other' },
];

const STATUS_OPTIONS: FilterOption[] = [
  { value: 'releasing', label: 'Releasing' },
  { value: 'completed', label: 'Completed' },
  { value: 'hiatus', label: 'Hiatus' },
  { value: 'cancelled', label: 'Cancelled' },
  { value: 'upcoming', label: 'Upcoming' },
  { value: 'unknown', label: 'Unknown' },
];

const CONTENT_RATING_OPTIONS: FilterOption[] = [
  { value: 'safe', label: 'Safe' },
  { value: 'suggestive', label: 'Suggestive' },
  { value: 'erotica', label: 'Erotica' },
  { value: 'pornographic', label: 'Pornographic' },
];

const SORT_OPTIONS = [
  { key: 'score_desc', value: 'Score (High to Low)' },
  { key: 'score_asc', value: 'Score (Low to High)' },
  // MangaBaka popularity is a RANK (1 = most popular), so "most popular first"
  // is popularity_asc and "least popular first" is popularity_desc — inverse of
  // the value-based sorts (score/chapters/year). Labels map to the right token.
  { key: 'popularity_asc', value: 'Popularity (High to Low)' },
  { key: 'popularity_desc', value: 'Popularity (Low to High)' },
  { key: 'name_asc', value: 'Name (A to Z)' },
  { key: 'name_desc', value: 'Name (Z to A)' },
  { key: 'published_year_desc', value: 'Year (Newest)' },
  { key: 'published_year_asc', value: 'Year (Oldest)' },
  { key: 'chapters_desc', value: 'Chapters (Most)' },
  { key: 'latest', value: 'Latest Updated' },
];

function cycleNext(current: TristateChipState): TristateMode | undefined {
  if (current === 'neutral') {
    return 'include';
  }

  if (current === 'include') {
    return 'exclude';
  }

  return undefined;
}

interface TristateOptionProps {
  option: FilterOption;
  state: TristateChipState;
  onCycle: (value: string, current: TristateChipState) => void;
}

function TristateOption({ option, state, onCycle }: TristateOptionProps) {
  const handleCycle = useCallback(
    (current: TristateChipState) => {
      onCycle(option.value, current);
    },
    [option.value, onCycle]
  );

  return (
    <TristateChip
      label={option.label}
      state={state}
      data-testid={`discovery-chip-${option.value}`}
      onCycle={handleCycle}
    />
  );
}

interface TristateSectionProps {
  label: string;
  options: FilterOption[];
  map: TristateMap;
  onChange: (map: TristateMap) => void;
}

function TristateSection({
  label,
  options,
  map,
  onChange,
}: TristateSectionProps) {
  const handleCycle = useCallback(
    (value: string, current: TristateChipState) => {
      const next = { ...map };
      const mode = cycleNext(current);

      if (mode) {
        next[value] = mode;
      } else {
        delete next[value];
      }

      onChange(next);
    },
    [map, onChange]
  );

  return (
    <div className={styles.section}>
      <div className={styles.sectionLabel}>{label}</div>
      <div className={styles.chips}>
        {options.map((option) => (
          <TristateOption
            key={option.value}
            option={option}
            state={map[option.value] ?? 'neutral'}
            onCycle={handleCycle}
          />
        ))}
      </div>
    </div>
  );
}

interface TagChipProps {
  tag: DiscoveryTagSelection;
  onCycle: (tag: DiscoveryTagSelection) => void;
}

function TagChip({ tag, onCycle }: TagChipProps) {
  const handleCycle = useCallback(() => {
    onCycle(tag);
  }, [tag, onCycle]);

  return (
    <TristateChip
      label={tag.name}
      state={tag.mode}
      data-testid={`discovery-tag-chip-${tag.id}`}
      onCycle={handleCycle}
    />
  );
}

export interface FilterDrawerProps {
  isOpen: boolean;
  onClose: () => void;
}

function FilterDrawer({ isOpen, onClose }: FilterDrawerProps) {
  const options = useDiscoveryOptions();
  const { data: genres } = useDiscoveryGenres();

  const handleTypeChange = useCallback((map: TristateMap) => {
    setDiscoveryOption('type', map);
  }, []);

  const handleGenreChange = useCallback((map: TristateMap) => {
    setDiscoveryOption('genre', map);
  }, []);

  const handleStatusChange = useCallback((map: TristateMap) => {
    setDiscoveryOption('status', map);
  }, []);

  const handleContentRatingChange = useCallback((map: TristateMap) => {
    setDiscoveryOption('contentRating', map);
  }, []);

  const handleTagAdd = useCallback(
    (tag: { id: number; name: string }) => {
      const next: DiscoveryTagSelection[] = [
        ...options.tags,
        { id: tag.id, name: tag.name, mode: 'include' },
      ];
      setDiscoveryOption('tags', next);
    },
    [options.tags]
  );

  // Selected-tag lifecycle: include -> exclude -> off (removes the chip).
  const handleTagCycle = useCallback(
    (tag: DiscoveryTagSelection) => {
      if (tag.mode === 'include') {
        setDiscoveryOption(
          'tags',
          options.tags.map((t) =>
            t.id === tag.id ? { ...t, mode: 'exclude' as const } : t
          )
        );
      } else {
        setDiscoveryOption(
          'tags',
          options.tags.filter((t) => t.id !== tag.id)
        );
      }
    },
    [options.tags]
  );

  const handleTagModeChange = useCallback((mode: 'and' | 'or') => {
    setDiscoveryOption('tagMode', mode);
  }, []);

  const handleYearLowerChange = useCallback(({ value }: NumberInputChanged) => {
    setDiscoveryOption('yearLower', value ?? undefined);
  }, []);

  const handleYearUpperChange = useCallback(({ value }: NumberInputChanged) => {
    setDiscoveryOption('yearUpper', value ?? undefined);
  }, []);

  const handleRatingLowerChange = useCallback(
    ({ value }: NumberInputChanged) => {
      setDiscoveryOption('ratingLower', value ?? undefined);
    },
    []
  );

  const handleRatingUpperChange = useCallback(
    ({ value }: NumberInputChanged) => {
      setDiscoveryOption('ratingUpper', value ?? undefined);
    },
    []
  );

  const handleSortChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string>) => {
      setDiscoveryOption('sortBy', value);
    },
    []
  );

  const handleAdultChange = useCallback(({ value }: CheckInputChanged) => {
    setDiscoveryOption('includeAdult', value);
  }, []);

  if (!isOpen) {
    return null;
  }

  const genreOptions: FilterOption[] = genres.map((genre) => ({
    value: genre.value,
    label: genre.label,
  }));

  return (
    <div className={styles.overlay}>
      <button
        type="button"
        className={styles.backdrop}
        aria-label={translate('Close')}
        onClick={onClose}
      />

      <div
        className={classNames(styles.drawer)}
        data-testid="discovery-filter-drawer"
      >
        <div className={styles.header}>
          <span className={styles.title}>{translate('DiscoveryFilters')}</span>

          <Link
            className={styles.closeButton}
            aria-label={translate('Close')}
            onPress={onClose}
          >
            <Icon name={icons.CLOSE} size={18} />
          </Link>
        </div>

        <div className={styles.body}>
          <TristateSection
            label={translate('DiscoveryType')}
            options={TYPE_OPTIONS}
            map={options.type}
            onChange={handleTypeChange}
          />

          <TristateSection
            label={translate('DiscoveryGenres')}
            options={genreOptions}
            map={options.genre}
            onChange={handleGenreChange}
          />

          <TristateSection
            label={translate('DiscoveryStatus')}
            options={STATUS_OPTIONS}
            map={options.status}
            onChange={handleStatusChange}
          />

          <TristateSection
            label={translate('DiscoveryContentRating')}
            options={CONTENT_RATING_OPTIONS}
            map={options.contentRating}
            onChange={handleContentRatingChange}
          />

          <div className={styles.section}>
            <div className={styles.sectionLabel}>
              {translate('DiscoveryTags')}
            </div>

            <TagTypeahead
              selectedIds={options.tags.map((tag) => tag.id)}
              tagMode={options.tagMode}
              onAdd={handleTagAdd}
              onTagModeChange={handleTagModeChange}
            />

            {options.tags.length > 0 ? (
              <div className={styles.chips}>
                {options.tags.map((tag) => (
                  <TagChip key={tag.id} tag={tag} onCycle={handleTagCycle} />
                ))}
              </div>
            ) : null}
          </div>

          <div className={styles.section}>
            <div className={styles.sectionLabel}>
              {translate('DiscoveryYear')}
            </div>

            <div className={styles.rangeRow}>
              <NumberInput
                className={styles.rangeInput}
                name="yearLower"
                value={options.yearLower ?? null}
                min={1679}
                max={2262}
                placeholder={translate('DiscoveryRangeFrom')}
                onChange={handleYearLowerChange}
              />

              <span className={styles.rangeSeparator}>–</span>

              <NumberInput
                className={styles.rangeInput}
                name="yearUpper"
                value={options.yearUpper ?? null}
                min={1679}
                max={2262}
                placeholder={translate('DiscoveryRangeTo')}
                onChange={handleYearUpperChange}
              />
            </div>
          </div>

          <div className={styles.section}>
            <div className={styles.sectionLabel}>
              {translate('DiscoveryScore')}
            </div>

            <div className={styles.rangeRow}>
              <NumberInput
                className={styles.rangeInput}
                name="ratingLower"
                value={options.ratingLower ?? null}
                min={0}
                max={100}
                placeholder={translate('DiscoveryRangeFrom')}
                onChange={handleRatingLowerChange}
              />

              <span className={styles.rangeSeparator}>–</span>

              <NumberInput
                className={styles.rangeInput}
                name="ratingUpper"
                value={options.ratingUpper ?? null}
                min={0}
                max={100}
                placeholder={translate('DiscoveryRangeTo')}
                onChange={handleRatingUpperChange}
              />
            </div>
          </div>

          <div className={styles.section}>
            <div className={styles.sectionLabel}>
              {translate('DiscoverySortBy')}
            </div>

            <EnhancedSelectInput
              name="sortBy"
              value={options.sortBy}
              values={SORT_OPTIONS}
              onChange={handleSortChange}
            />
          </div>

          <div className={styles.section}>
            <label className={styles.adultRow}>
              <span className={styles.sectionLabel}>
                {translate('IncludeAdultContent')}
              </span>

              <CheckInput
                name="includeAdult"
                value={options.includeAdult}
                data-testid="discovery-include-adult"
                onChange={handleAdultChange}
              />
            </label>
          </div>
        </div>
      </div>
    </div>
  );
}

export default FilterDrawer;
