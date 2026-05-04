// Phase 7 Plan 07-04: generalized to operate on any item type that exposes a
// `sortTitle` string. Previously typed as `Series[]`; widened so the manga
// sibling pages can call it without a cast (no behaviour change for TV).
const STARTS_WITH_NUMBER_REGEX = /^\d/;

export default function getIndexOfFirstCharacter<T extends { sortTitle: string }>(
  items: T[],
  character: string
) {
  return items.findIndex((item) => {
    const firstCharacter = item.sortTitle.charAt(0);

    if (character === '#') {
      return STARTS_WITH_NUMBER_REGEX.test(firstCharacter);
    }

    return firstCharacter === character;
  });
}
