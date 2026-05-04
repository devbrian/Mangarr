# Settings/Profiles/Translations/

## Purpose

TranslationProfile editor sub-tree — wires the Phase 5 `TranslationProfile` entity (`/api/v5/translationprofile`) into the Settings → Translation Profiles page (Phase 7 D-05 topology rework).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Settings\Profiles\Translations\`

## Sonarr Inheritance

This sub-tree is the manga sibling of `frontend/src/Settings/Profiles/Quality/`. Every file ports the role-match analog file from `Quality/` with manga-domain divergence at the form-fields layer (language-rank list + `isDefault` + `fallback` enum replace quality items + `cutoff` + `upgradeAllowed`). All files carry the Pattern S2 sibling-divergence header.

## Files

| File | Role | Quality/ Analog |
|------|------|-----------------|
| `TranslationProfiles.tsx` | List page (FieldSet + map of profile cards + add-new card + EditModal) | `QualityProfiles.tsx` |
| `TranslationProfiles.css` + `.css.d.ts` | List-page styling | `QualityProfiles.css` + `.css.d.ts` |
| `TranslationProfile.tsx` | Single-card display (name + isDefault badge + allowed-language chips + delete confirm) | `QualityProfile.tsx` |
| `TranslationProfile.css` + `.css.d.ts` | Single-card styling | `QualityProfile.css` + `.css.d.ts` |
| `TranslationProfileName.tsx` | Name resolver hook + component for column rendering elsewhere | `QualityProfileName.tsx` |
| `EditTranslationProfileModal.tsx` | Modal scaffold wrapper | `EditQualityProfileModal.tsx` |
| `EditTranslationProfileModalContent.tsx` | Form: name + isDefault + ranked language list + fallback radio + Save/Delete | `EditQualityProfileModalContent.tsx` |
| `CLAUDE.md` | This documentation | — |

## API Wiring

- **GET `/api/v5/translationprofile`** — list (used by `TranslationProfiles.tsx`)
- **POST `/api/v5/translationprofile`** — create (used by modal Save)
- **PUT `/api/v5/translationprofile/{id}`** — update (used by modal Save)
- **DELETE `/api/v5/translationprofile/{id}`** — delete (used by modal Delete + card Delete)

React Query cache key: `['/translationprofile']` (singular `path`-shaped — matches the `useApiQuery` `path` argument; SignalR auto-invalidation NOT wired in v1 since Phase 5 controllers do not extend `RestControllerWithSignalR<T>` for profiles).

## Data Shape (Phase 5 D-01..D-04)

```typescript
interface TranslationProfile {
  id: number;
  name: string;
  isDefault: boolean;
  languages: { language: string /* BCP-47 */; rank: number; allowed: boolean }[];
  fallback: 'allowed-low-rank' | 'rejected';
}
```

## i18n Keys Referenced (deferred to Plan 11 en.json additions)

The `translate()` calls fall back to the key string at runtime if the key is missing — no crash. Plan 07-11 lands en.json additions:

- `TranslationProfiles` (page legend + nav label)
- `TranslationProfilesSettingsSummary` (nav row summary)
- `TranslationProfilesLoadError`
- `AddTranslationProfile` (add CTA)
- `EditTranslationProfile` (modal title)
- `SaveTranslationProfile` (save button — UI-SPEC §Page-level CTAs)
- `DeleteTranslationProfile` (delete button)
- `DeleteTranslationProfileMessageText` (confirm message — UI-SPEC §Destructive confirmation)
- `DeleteProfile` (confirm-modal confirmLabel)
- `TranslationProfileInUseHelpText` (in-use error — UI-SPEC §Error states)
- `Languages` / `Fallback` / `DefaultProfile` / `AddLanguage` / `MoveUp` / `MoveDown` / `Remove`

## Phase 8 Cleanup

This sub-tree is manga-canonical and stays. The legacy `Settings/Profiles/Quality/` sub-tree is deleted in Phase 8 (Pitfall 8 — Phase 7 keeps it on disk + unlinked from the left-nav for additive-principle preservation).

## Cross-References

- [../Profiles.tsx](../Profiles.tsx) — page repurposed in Phase 7 Plan 07-07 to render `<TranslationProfiles />` only
- [../Quality/](../Quality/) — Sonarr role-match analog (kept on disk; Phase 8 deletes)
- [../../Settings.tsx](../../Settings.tsx) — left-nav Profiles row renamed to "Translation Profiles" in Phase 7 D-05
- [../../CLAUDE.md](../../CLAUDE.md) — Settings sub-tree overview + D-05 topology rework
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-07-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-07-PLAN.md) — plan body
- [../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — TranslationProfile entity decisions
