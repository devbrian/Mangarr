# Settings/Profiles/CustomFormatProfile/

## Purpose

CustomFormatProfile editor sub-tree — wires the Phase 5 D-07 `CustomFormatProfile` entity (`/api/v5/customformatprofile`) into the Settings → Custom Format Profiles top-level page (Phase 7 D-05 topology rework — NEW route `/settings/customformatprofiles`).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Settings\Profiles\CustomFormatProfile\`

## Mangarr Inheritance

This sub-tree is the manga sibling of the QualityProfile editor. Every file ports the role-match analog file from `Settings/Profiles/Quality/` with manga-domain divergence at the form-fields layer (formatItems list + min/max score range + upgradeAllowed Toggle replace quality items + cutoff). All files carry the Pattern S2 sibling-divergence header.

**Distinction from CustomFormat editor:** Phase 5 D-07 separates `CustomFormat` (a definition/spec — managed via `Settings/CustomFormats/`) from `CustomFormatProfile` (an ordered + scored set referenced by Manga — managed here). The Custom Format definitions feed into this profile editor's `formatItems` dropdown.

## Files

| File | Role | Quality/ Analog |
|------|------|-----------------|
| `CustomFormatProfileSettings.tsx` | Top-level page (route `/settings/customformatprofiles`); wraps PageContent + SettingsToolbar + DndProvider + the list-page UI | `Quality.tsx` + `QualityProfiles.tsx` |
| `CustomFormatProfileSettings.css` + `.css.d.ts` | List-page styling | `QualityProfiles.css` + `.css.d.ts` |
| `CustomFormatProfile.tsx` | Single-card display (name + isDefault badge + score range + allowed-format count + upgradeAllowed badge + delete confirm) | `QualityProfile.tsx` |
| `CustomFormatProfile.css` + `.css.d.ts` | Single-card styling | `QualityProfile.css` + `.css.d.ts` |
| `EditCustomFormatProfileModal.tsx` | Modal scaffold wrapper | `EditQualityProfileModal.tsx` |
| `EditCustomFormatProfileModalContent.tsx` | Form: name + isDefault + upgradeAllowed + min/max score + formatItems list (customFormatId + score) + Save/Delete | `EditQualityProfileModalContent.tsx` |
| `CLAUDE.md` | This documentation | — |

## API Wiring

- **GET `/api/v5/customformatprofile`** — list (used by `CustomFormatProfileSettings.tsx`)
- **POST `/api/v5/customformatprofile`** — create (used by modal Save)
- **PUT `/api/v5/customformatprofile/{id}`** — update (used by modal Save)
- **DELETE `/api/v5/customformatprofile/{id}`** — delete (used by modal Delete + card Delete)
- **GET `/api/v5/customformat?mediaType=manga`** — feed the formatItems dropdown (Phase 5 D-10 filter)

React Query cache keys: `['/customformatprofile']` and `['/customformat?mediaType=manga']` (singular `path`-shaped).

## Data Shape (Phase 5 D-07)

```typescript
interface CustomFormatProfile {
  id: number;
  name: string;
  isDefault: boolean;
  formatItems: { customFormatId: number; score: number }[];
  minFormatScore: number;
  maxFormatScore: number;
  upgradeAllowed: boolean;
}
```

## Routing

This sub-tree's `CustomFormatProfileSettings.tsx` is registered in `frontend/src/App/AppRoutes.tsx` as:

```tsx
<Route path="/settings/customformatprofiles" component={CustomFormatProfileSettings} />
```

The route is additive per D-09 — no existing route is modified.

## i18n Keys Referenced (deferred to Plan 11 en.json additions)

- `CustomFormatProfiles` (page title + nav label + legend)
- `CustomFormatProfilesSettingsSummary` (nav row summary)
- `CustomFormatProfilesLoadError`
- `AddCustomFormatProfile` (add CTA)
- `EditCustomFormatProfile` (modal title)
- `SaveCustomFormatProfile` (save button — UI-SPEC §Page-level CTAs)
- `DeleteCustomFormatProfile` (delete button)
- `DeleteCustomFormatProfileMessageText` (confirm message — UI-SPEC §Destructive confirmation)
- `DeleteProfile` (confirm-modal confirmLabel — shared with Translations/)
- `CustomFormatProfileInUseHelpText` (in-use error — UI-SPEC §Error states)
- `MinFormatScoreLabel` / `MaxFormatScoreLabel` / `AllowedFormatsCount`
- `MaximumCustomFormatScore` / `MaximumCustomFormatScoreHelpText`
- `FormatItems` / `AddFormatItem` / `NoMangaCustomFormatsAvailable`
- `Default` / `UpgradesAllowed` (existing Mangarr keys reused)

## Phase 8 Cleanup

This sub-tree is manga-canonical and stays. The legacy `Settings/Profiles/Quality/` sub-tree is deleted in Phase 8.

## Cross-References

- [../Translations/CLAUDE.md](../Translations/CLAUDE.md) — sibling sub-tree (TranslationProfile editor)
- [../Quality/](../Quality/) — Mangarr role-match analog (kept on disk; Phase 8 deletes)
- [../../Settings.tsx](../../Settings.tsx) — left-nav CustomFormatProfiles row added in Phase 7 D-05
- [../../../App/AppRoutes.tsx](../../../App/AppRoutes.tsx) — `/settings/customformatprofiles` route registered here
- [../../CLAUDE.md](../../CLAUDE.md) — Settings sub-tree overview + D-05 topology rework
- [07-07-PLAN.md](../../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-07-PLAN.md) — plan body
- [05-CONTEXT.md](../../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-07 CustomFormatProfile entity decisions; D-10 mediaType=manga filter
