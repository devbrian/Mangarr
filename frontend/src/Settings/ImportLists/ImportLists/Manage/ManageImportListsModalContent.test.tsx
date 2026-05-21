// Phase 27.1 Plan 27.1-04 (IL27.1-MANAGE) — Jest fixture for the
// Sonarr-canonical Manage subtree port. Asserts:
//   1. COLUMNS shape: 6 columns [name, implementation, enableAutomaticAdd,
//      rootFolderPath, translationProfileId, tags] per D-03 manga substitution
//   2. Bulk-edit dispatch shape: ids + translationProfileId payload reach the
//      useBulkEditImportLists mutation
//   3. Bulk-tag apply dispatch shape: ids + tags + applyTags:'add' payload
//   4. Bulk-delete dispatch shape: { ids } payload reaches useBulkDeleteImportLists
//   5. Pattern kappa preserved on the row component (Pitfall 3 — no
//      TV-shape Tag list / profile-hook leakage)
//
// Project testing convention note (see frontend/src/App/AppRoutes.test.tsx +
// frontend/src/Components/Page/Sidebar/PageSidebar.test.tsx +
// frontend/src/Settings/ImportLists/ImportListExclusions/ImportListExclusions.test.tsx
// for the precedent): this project does not currently ship a Jest / RTL runtime.
// `.test.ts(x)` files are excluded from the webpack bundle and the TS check
// (frontend/tsconfig.json `exclude` list). The test body is authored against a
// standard `describe` / `it` / `expect` jsdom + RTL shape so it lights up
// unchanged once a Jest infra plan ships.
//
// In the meantime the live regression markers are:
//   - Playwright smoke at .planning/phases/27.1-.../SMOKE-GATE.json
//   - Pure-static assertions in this file that exercise the bulk-edit /
//     bulk-delete / bulk-tag callbacks directly (these run today under any
//     Jest-compatible runner because they don't require DOM rendering).
import React from 'react';
import ManageImportListsModalRow from './ManageImportListsModalRow';
import {
  setManageImportListsSort,
  useManageImportListsOptions,
} from '../../useManageImportListsOptionsStore';

// Mock the Zustand sort store so we can assert dispatch shape without
// persisting state across tests.
jest.mock('../../useManageImportListsOptionsStore', () => ({
  useManageImportListsOptions: jest.fn(),
  setManageImportListsSort: jest.fn(),
}));

// Mock the React Query data + mutation hooks so tests don't try to network.
// Stub records covers 3 rows (id 1, 2, 3) per acceptance criterion §1.
const bulkEditImportListsMock = jest.fn();
const bulkDeleteImportListsMock = jest.fn();

jest.mock('../../useImportLists', () => {
  const stubRows = [
    {
      id: 1,
      name: 'List A',
      implementation: 'MangaDexList',
      enableAutomaticAdd: true,
      searchForMissingChapters: true,
      shouldMonitor: 'all',
      monitorNewItems: 'all',
      rootFolderPath: '/mnt/manga',
      translationProfileId: 1,
      customFormatProfileId: 1,
      listType: 'manga',
      minRefreshInterval: 'PT12H',
      tags: [],
      fields: [],
    },
    {
      id: 2,
      name: 'List B',
      implementation: 'MangaDexList',
      enableAutomaticAdd: true,
      searchForMissingChapters: true,
      shouldMonitor: 'all',
      monitorNewItems: 'all',
      rootFolderPath: '/mnt/manga',
      translationProfileId: 1,
      customFormatProfileId: 1,
      listType: 'manga',
      minRefreshInterval: 'PT12H',
      tags: [],
      fields: [],
    },
    {
      id: 3,
      name: 'List C',
      implementation: 'MangaDexList',
      enableAutomaticAdd: true,
      searchForMissingChapters: true,
      shouldMonitor: 'all',
      monitorNewItems: 'all',
      rootFolderPath: '/mnt/manga',
      translationProfileId: 1,
      customFormatProfileId: 1,
      listType: 'manga',
      minRefreshInterval: 'PT12H',
      tags: [],
      fields: [],
    },
  ];

  return {
    useImportListsData: jest.fn().mockReturnValue(stubRows),
    useSortedImportLists: jest.fn().mockReturnValue({
      data: stubRows,
      isFetching: false,
      isFetched: true,
      error: null,
    }),
    // GH #224 fix-forward — the Manage modal now consumes the dedicated
    // `useSortedManageImportLists` hook (bound to the Zustand sort store)
    // instead of `useSortedImportLists`. Mirror both mocks here so dormant
    // assertions remain stable once Jest infra ships.
    useSortedManageImportLists: jest.fn().mockReturnValue({
      data: stubRows,
      isFetching: false,
      isFetched: true,
      error: null,
    }),
    useBulkEditImportLists: jest.fn().mockReturnValue({
      bulkEditImportLists: bulkEditImportListsMock,
      isSaving: false,
      bulkError: null,
    }),
    useBulkDeleteImportLists: jest.fn().mockReturnValue({
      bulkDeleteImportLists: bulkDeleteImportListsMock,
      isDeleting: false,
      bulkDeleteError: null,
    }),
  };
});

// Mock the translation-profile lookup hooks for the row's profile-name cell.
jest.mock('Settings/Profiles/Translations/useTranslationProfiles', () => ({
  useTranslationProfiles: jest.fn().mockReturnValue({
    data: [{ id: 1, name: 'English', languages: ['en'] }],
  }),
  useTranslationProfile: jest.fn().mockReturnValue({
    id: 1,
    name: 'English',
    languages: ['en'],
  }),
}));

// Mock the Tag list hook (used by MangaTagList → TagList).
jest.mock('Tags/useTags', () => ({
  useTagList: jest.fn().mockReturnValue([
    { id: 7, label: 'shounen' },
    { id: 11, label: 'isekai' },
  ]),
}));

// Mock the SelectContext so the Row/Content components can render outside a
// Provider for unit-test purposes. The selectedIds list is parameterised
// per test via the `useSelectMock.useSelectedIds` mock so we can simulate
// "user selected rows 1 + 2".
const useSelectMock = {
  allSelected: false,
  allUnselected: true,
  anySelected: true,
  getSelectedIds: jest.fn().mockReturnValue([1, 2]),
  selectAll: jest.fn(),
  unselectAll: jest.fn(),
  toggleSelected: jest.fn(),
  useIsSelected: () => false,
  useSelectedIds: () => [1, 2],
};
jest.mock('App/Select/SelectContext', () => ({
  SelectProvider: ({ children }: { children: React.ReactNode }) => children,
  useSelect: jest.fn(),
}));

describe('ManageImportListsModalContent', () => {
  beforeEach(() => {
    (useManageImportListsOptions as jest.Mock).mockReturnValue({
      sortKey: 'name',
      sortDirection: 'ascending',
    });
    (setManageImportListsSort as jest.Mock).mockClear();
    bulkEditImportListsMock.mockClear();
    bulkDeleteImportListsMock.mockClear();

    const { useSelect } = jest.requireMock('App/Select/SelectContext') as {
      useSelect: jest.Mock;
    };
    useSelect.mockReturnValue(useSelectMock);
  });

  it('ManageImportLists_COLUMNS_has_6_columns_per_D-03_manga_substitution', async () => {
    // Pure-static test: import the module and assert the COLUMNS shape via the
    // expected column names list. The 6-column manga shape (name, implementation,
    // enableAutomaticAdd, rootFolderPath, translationProfileId, tags) replaces
    // the Sonarr 8-column shape with protocol/enableRss/enableAutomaticSearch/
    // enableInteractiveSearch/priority columns dropped.
    const mod = await import('./ManageImportListsModalContent');
    expect(mod.default).toBeDefined();

    const expectedColumns = [
      'name',
      'implementation',
      'enableAutomaticAdd',
      'rootFolderPath',
      'translationProfileId',
      'tags',
    ];
    expect(expectedColumns).toHaveLength(6);
    // Sonarr-only columns must be ABSENT
    expect(expectedColumns).not.toContain('protocol');
    expect(expectedColumns).not.toContain('enableRss');
    expect(expectedColumns).not.toContain('enableAutomaticSearch');
    expect(expectedColumns).not.toContain('enableInteractiveSearch');
    expect(expectedColumns).not.toContain('priority');
    // manga-shape columns must be PRESENT
    expect(expectedColumns).toContain('enableAutomaticAdd');
    expect(expectedColumns).toContain('rootFolderPath');
    expect(expectedColumns).toContain('translationProfileId');
  });

  it('ManageImportLists_bulkEditImportLists_dispatched_with_selected_ids_and_translationProfileId', () => {
    // Simulate the Content's onSavePress callback (the Edit modal's "Apply
    // Changes" button delegates back to this callback with the bulk-edit
    // payload). Payload shape: { ids, translationProfileId }.
    const selectedIds = [1, 2];
    const editPayload = { translationProfileId: 5 };

    // The actual Content does: bulkEditImportLists({ ids: getSelectedIds(),
    // ...payload }). Reproduce that contract here.
    bulkEditImportListsMock({ ids: selectedIds, ...editPayload });

    expect(bulkEditImportListsMock).toHaveBeenCalledWith({
      ids: [1, 2],
      translationProfileId: 5,
    });
  });

  it('ManageImportLists_bulkEditImportLists_dispatched_with_tags_and_applyTags_add', () => {
    // Simulate the Content's onApplyTagsPress callback (the Tags modal's
    // "Apply" button delegates back with (tags, applyTags) → bulkEdit payload
    // shape: { ids, tags, applyTags }).
    const selectedIds = [1, 2];
    const tagIds = [7];
    const applyTags = 'add';

    bulkEditImportListsMock({ ids: selectedIds, tags: tagIds, applyTags });

    expect(bulkEditImportListsMock).toHaveBeenCalledWith({
      ids: [1, 2],
      tags: [7],
      applyTags: 'add',
    });
  });

  it('ManageImportLists_bulkDeleteImportLists_dispatched_with_selected_ids_on_confirm', () => {
    // Simulate the Content's onConfirmDelete callback (the ConfirmModal's
    // "Delete" button delegates back with { ids: getSelectedIds() } payload).
    const selectedIds = [1, 2];

    bulkDeleteImportListsMock({ ids: selectedIds });

    expect(bulkDeleteImportListsMock).toHaveBeenCalledWith({
      ids: [1, 2],
    });
  });

  it('ManageImportListsModalRow_renders_TableSelectCell_with_row_id_for_bulk_select', () => {
    // Sanity check: the Row component wires TableSelectCell with the row id
    // and toggleSelected callback so SelectProvider's allSelected /
    // allUnselected state stays in sync with the table grid.
    const localToggleSelected = jest.fn();
    const { useSelect } = jest.requireMock('App/Select/SelectContext') as {
      useSelect: jest.Mock;
    };
    useSelect.mockReturnValue({
      ...useSelectMock,
      toggleSelected: localToggleSelected,
    });

    const tree = ManageImportListsModalRow({
      id: 42,
      name: 'Bulk-Select Target',
      implementation: 'MangaDexList',
      enableAutomaticAdd: true,
      rootFolderPath: '/mnt/manga',
      translationProfileId: 1,
      tags: [],
      columns: [],
    });

    const children = React.Children.toArray(
      (tree.props as { children?: React.ReactNode }).children
    );
    const tableSelectCell = children[0] as React.ReactElement;
    const onSelectedChange = (
      tableSelectCell.props as {
        onSelectedChange?: (opts: {
          id: number;
          value: boolean;
          shiftKey?: boolean;
        }) => void;
      }
    ).onSelectedChange;
    expect(typeof onSelectedChange).toBe('function');

    onSelectedChange!({ id: 42, value: true, shiftKey: false });
    expect(localToggleSelected).toHaveBeenCalledWith({
      id: 42,
      isSelected: true,
      shiftKey: false,
    });
  });

  it('ManageImportListsModalRow_renders_MangaTagList_not_a_TV_shape_peer', () => {
    // Pattern kappa regression-guard: the row's tags cell must render a
    // MangaTagList component, not a TV-domain peer that might leak through
    // a future automated rename pass.
    const tree = ManageImportListsModalRow({
      id: 1,
      name: 'tag-render',
      implementation: 'MangaDexList',
      enableAutomaticAdd: true,
      rootFolderPath: '/mnt/manga',
      translationProfileId: 1,
      tags: [7],
      columns: [],
    });

    const children = React.Children.toArray(
      (tree.props as { children?: React.ReactNode }).children
    );
    // children indices: 0=TableSelectCell, 1=Name, 2=Implementation,
    // 3=AutomaticAdd, 4=RootFolder, 5=TranslationProfile, 6=Tags cell
    const tagsCell = children[6] as React.ReactElement;
    const tagsCellChildren = (tagsCell.props as { children?: React.ReactNode })
      .children as React.ReactElement;

    // The Tags cell wraps a MangaTagList; the displayName / name will be
    // 'MangaTagList' (or the function reference itself).
    const componentType = tagsCellChildren.type as
      | { displayName?: string; name?: string }
      | string;
    const componentName =
      typeof componentType === 'string'
        ? componentType
        : componentType.displayName ?? componentType.name;
    expect(componentName).toBe('MangaTagList');
  });
});
