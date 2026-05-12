// Sonarr divergence: Phase 17.3 Plan 17.3-04 (D-07) — file renamed from
// SeriesTagInput.tsx to MangaTagInput.tsx; in-file symbols renamed
// (SeriesTag -> MangaTag, SeriesTagInputProps -> MangaTagInputProps,
// useSeriesTags -> useMangaTags). The hook is defined inline in this file
// so it is renamed in place per the plan's decision rule.
import React, { useCallback, useEffect, useMemo } from 'react';
import { Tag, useAddTag, useSortedTagList } from 'Tags/useTags';
import { InputChanged } from 'typings/inputs';
import { useFormInputGroup } from '../FormInputGroupContext';
import TagInput, { TagBase, TagInputProps } from './TagInput';

interface MangaTag extends TagBase {
  id: number;
  name: string;
}

export interface MangaTagInputProps<V>
  extends Omit<
    TagInputProps<MangaTag>,
    'tags' | 'tagList' | 'onTagAdd' | 'onTagDelete' | 'onChange'
  > {
  name: string;
  value: V;
  onChange: (change: InputChanged<V>) => void;
}

function useMangaTags(tags: number[]) {
  const sortedTags = useSortedTagList();
  const filteredTagList = sortedTags.filter((tag) => !tags.includes(tag.id));

  return {
    tags: tags.reduce((acc: MangaTag[], tag) => {
      const matchingTag = sortedTags.find((t) => t.id === tag);

      if (matchingTag) {
        acc.push({
          id: tag,
          name: matchingTag.label,
        });
      }

      return acc;
    }, []),

    tagList: filteredTagList.map(({ id, label: name }) => {
      return {
        id,
        name,
      };
    }),

    allTags: sortedTags,
  };
}

export default function MangaTagInput<V extends number | number[]>({
  name,
  value,
  onChange,
  ...otherProps
}: MangaTagInputProps<V>) {
  const formInputActions = useFormInputGroup();
  const isArray = Array.isArray(value);

  const arrayValue = useMemo(() => {
    if (isArray) {
      return value as number[];
    }

    return value === 0 ? [] : [value as number];
  }, [isArray, value]);

  const { tags, tagList, allTags } = useMangaTags(arrayValue);

  const handleTagCreated = useCallback(
    (tag: Tag) => {
      if (isArray) {
        onChange({ name, value: [...value, tag.id] as V });
      } else {
        onChange({
          name,
          value: tag.id as V,
        });
      }
    },
    [name, value, isArray, onChange]
  );

  const { addTag, addTagError } = useAddTag(handleTagCreated);

  const handleTagAdd = useCallback(
    (newTag: MangaTag) => {
      if (newTag.id) {
        if (isArray) {
          onChange({ name, value: [...value, newTag.id] as V });
        } else {
          onChange({ name, value: newTag.id as V });
        }

        return;
      }

      const existingTag = allTags.some((t) => t.label === newTag.name);

      if (!existingTag) {
        addTag({
          label: newTag.name,
        });
      }
    },
    [name, value, isArray, allTags, onChange, addTag]
  );

  const handleTagDelete = useCallback(
    ({ index }: { index: number }) => {
      if (isArray) {
        const newValue = value.slice();
        newValue.splice(index, 1);

        onChange({ name, value: newValue as V });
      } else {
        onChange({ name, value: 0 as V });
      }
    },
    [name, value, isArray, onChange]
  );

  useEffect(() => {
    formInputActions?.setClientErrors(addTagError?.errors ?? []);
    formInputActions?.setClientWarnings(addTagError?.warnings ?? []);
  }, [addTagError, formInputActions]);

  return (
    <TagInput
      {...otherProps}
      name={name}
      tags={tags}
      tagList={tagList}
      onTagAdd={handleTagAdd}
      onTagDelete={handleTagDelete}
    />
  );
}
