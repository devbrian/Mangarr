// Quick task 260623-kar — Discovery named-saved-filter presets store.
// ADDITIVE layer over discoveryOptionsStore (the live working filters keep
// persisting under 'discovery_options' exactly as today). Presets live in their
// OWN localStorage key 'discovery_filter_presets' so the existing store shape is
// untouched (CONTEXT.md "Base filters approach — Named saved presets", LOCKED).
//
// This is the bespoke-Discovery-drawer divergence (DIVERGENCE.md Phase 42), NOT a
// re-adoption of Sonarr's Components/Filter saved-filter FilterBuilder machinery.
//
// A preset snapshots DiscoveryFilterState — which already EXCLUDES `topX` (that
// lives only on DiscoveryOptions = DiscoveryFilterState & { topX }), so the
// toolbar add-count is naturally left out of every preset per the LOCKED decision.
import { DiscoveryFilterState } from 'Discovery/DiscoveryModels';
import { createPersist } from 'Helpers/createPersist';

export interface DiscoveryFilterPreset {
  id: string;
  name: string;
  filters: DiscoveryFilterState;
}

export interface DiscoveryPresetsState {
  presets: DiscoveryFilterPreset[];
  addPreset: (name: string, filters: DiscoveryFilterState) => void;
  deletePreset: (id: string) => void;
}

// Only `presets` round-trips through localStorage; the actions are re-created from
// this initializer on every rehydrate (standard zustand-persist actions-in-store
// pattern — JSON.stringify silently drops the function-valued props on persist, and
// the default merge restores them from the initializer when rehydrating).
export const useDiscoveryPresetsStore = createPersist<DiscoveryPresetsState>(
  'discovery_filter_presets',
  (set) => ({
    presets: [],
    addPreset: (name, filters) =>
      set((state) => ({
        presets: [
          ...state.presets,
          {
            id: crypto.randomUUID(),
            name,
            // Deep-clone so the stored preset is decoupled from the live-store
            // object refs (the tristate maps / tag array are mutated in place
            // by the drawer handlers).
            filters: structuredClone(filters),
          },
        ],
      })),
    deletePreset: (id) =>
      set((state) => ({
        presets: state.presets.filter((preset) => preset.id !== id),
      })),
  })
);
