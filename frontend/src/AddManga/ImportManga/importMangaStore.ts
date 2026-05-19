// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md once Plan 25.1-03 lands.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/importSeriesStore.ts
// (RR v6 syntax in upstream; this Mangarr port is RR-version-agnostic — the
// store is presentational state, not routing).
//
// Manga sibling preserves: per-session ephemeral zustand store, 7-action
// surface (updateImportMangaItem / removeImportMangaItemByPath /
// clearImportManga / startProcessing / stopProcessing / addToLookupQueue /
// removeFromLookupQueue), lookupQueue + isProcessing fields, selector hook
// shape (useImportMangaItem / useImportMangaItems /
// useIsCurrentLookupQueueItem / useLookupQueueHasItems).
// Manga sibling diverges from importSeriesStore:
//   - translationProfileId + customFormatProfileId replace qualityProfileId
//     (Phase 5 D-04 — manga has no Quality model)
//   - seriesType DROPPED (no manga peer per Phase 8 audit)
//   - seasonFolder DROPPED (PROJECT.md DOMAIN-02 — no Season peer)
//   - MangaMonitor (5-value enum per Phase 6 D-03) replaces SeriesMonitor
//     (11-value upstream)
//
// CRITICAL convention: uses plain `import { create } from 'zustand'` (PATTERNS
// finding #3 + RESEARCH §2.6) — this store is per-session ephemeral, cleared
// on unmount of the per-folder scan view via clearImportManga(). It MUST NOT
// use the persisted-options factory from Helpers/Hooks/useOptionsStore — that
// factory is localStorage-backed, wrong for a per-session lookup queue / row
// state. (For reference: addMangaOptionsStore.ts IS persistence-backed and
// lives in the same parent dir; do not confuse the two.)
//
// Phase 8 cleanup: collapse with importSeriesStore when AddSeries/ deletes.
import { create } from 'zustand';
import { AddMangaResult } from 'AddManga/AddManga';
import { MangaMonitor } from 'Manga/Manga';

export interface ImportMangaItem {
  // id is the UnmappedFolder.name; load-bearing because per-row lookup keys
  // and lookupQueue head comparisons reference it as a stable string handle.
  id: string;
  path: string;
  name: string;
  relativePath: string;
  monitor: MangaMonitor;
  translationProfileId: number;
  customFormatProfileId: number;
  // Populated by the per-row useLookupManga useEffect once the chained
  // /api/v5/manga/lookup?term=<folderName> resolves with a non-empty array
  // (D-04 — top match auto-selects per D-03; user can override).
  selectedManga?: AddMangaResult;
  hasSearched: boolean;
}

interface ImportMangaState {
  items: Record<string, ImportMangaItem>;
  lookupQueue: string[];
  isProcessing: boolean;
}

interface ImportMangaActions {
  updateImportMangaItem: (id: string, patch: Partial<ImportMangaItem>) => void;
  removeImportMangaItemByPath: (path: string) => void;
  clearImportManga: () => void;
  startProcessing: () => void;
  stopProcessing: () => void;
  addToLookupQueue: (id: string) => void;
  removeFromLookupQueue: (id: string) => void;
}

const useImportMangaStore = create<ImportMangaState & ImportMangaActions>(
  (set) => ({
    items: {},
    lookupQueue: [],
    isProcessing: false,

    // updateImportMangaItem is a silent no-op when the row id is missing
    // from state — this is intentional and load-bearing for L-LOOKUP-RACE
    // (late-arriving lookup resolutions after clearImportManga must not
    // re-introduce stale rows). The Partial<ImportMangaItem> is spread on
    // top of the existing item; if the item is undefined (already cleared),
    // we skip the write entirely.
    updateImportMangaItem: (id, patch) =>
      set((s) => {
        const existing = s.items[id];
        if (!existing) {
          return s;
        }
        return {
          items: {
            ...s.items,
            [id]: { ...existing, ...patch },
          },
        };
      }),

    removeImportMangaItemByPath: (path) =>
      set((s) => {
        const next: Record<string, ImportMangaItem> = {};
        for (const k of Object.keys(s.items)) {
          if (s.items[k].path !== path) {
            next[k] = s.items[k];
          }
        }
        return { items: next };
      }),

    clearImportManga: () =>
      set({ items: {}, lookupQueue: [], isProcessing: false }),

    startProcessing: () => set({ isProcessing: true }),

    stopProcessing: () => set({ isProcessing: false }),

    addToLookupQueue: (id) =>
      set((s) => ({ lookupQueue: [...s.lookupQueue, id] })),

    removeFromLookupQueue: (id) =>
      set((s) => ({ lookupQueue: s.lookupQueue.filter((x) => x !== id) })),
  })
);

// Hook-based selectors (use these from components — they subscribe to
// re-renders on the relevant slice via zustand's shallow-equality default).
export const useImportMangaItem = (id: string): ImportMangaItem | undefined =>
  useImportMangaStore((s) => s.items[id]);

export const useImportMangaItems = (): ImportMangaItem[] =>
  useImportMangaStore((s) => Object.values(s.items));

export const useIsCurrentLookupQueueItem = (id: string): boolean =>
  useImportMangaStore((s) => s.lookupQueue[0] === id);

export const useLookupQueueHasItems = (): boolean =>
  useImportMangaStore((s) => s.lookupQueue.length > 0);

export const useIsImportMangaProcessing = (): boolean =>
  useImportMangaStore((s) => s.isProcessing);

// Action exports — destructure from getState() so callers do NOT subscribe
// to re-renders for every action invocation (PATTERNS finding — avoid the
// hook-via-selector re-render anti-pattern). Pattern is verbatim from
// frontend/src/InteractiveImport/Interactive/InteractiveImportContent.tsx
// line 196 and from how Sonarr's importSeriesStore exports actions.
export const {
  updateImportMangaItem,
  removeImportMangaItemByPath,
  clearImportManga,
  startProcessing,
  stopProcessing,
  addToLookupQueue,
  removeFromLookupQueue,
} = useImportMangaStore.getState();

// Seed helper — bulk-insert items on initial scan (each row's defaults are
// supplied by the caller, sourced from useAddMangaOption per D-05'). Called
// once per /add/import/:rootFolderId mount in ImportManga.tsx; subsequent
// renders re-use the state.
export const seedImportMangaItems = (items: ImportMangaItem[]) => {
  const nextItems: Record<string, ImportMangaItem> = {};
  for (const item of items) {
    nextItems[item.id] = item;
  }
  useImportMangaStore.setState({
    items: nextItems,
    lookupQueue: items.map((i) => i.id),
    isProcessing: false,
  });
};

export default useImportMangaStore;
