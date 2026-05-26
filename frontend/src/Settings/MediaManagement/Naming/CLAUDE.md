# Settings/MediaManagement/Naming/

## Purpose

Naming format pickers for the Settings → Media Management page. Both the **TV `Naming` FieldSet** (Episode Naming) and the **NEW manga `MangaNaming` FieldSet** (Manga Naming, Phase 7 D-05) live here side-by-side.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Settings\MediaManagement\Naming\`

## Files

| File | Role | Backend |
|------|------|---------|
| `Naming.tsx` | TV Episode Naming FieldSet | `/api/v3/config/naming` (V3 inherited) |
| `NamingModal.tsx` | TV token-picker modal | n/a (client) |
| `Naming.css` + `.css.d.ts` | TV naming styles | n/a |
| `NamingModal.css` + `.css.d.ts` | TV modal styles | n/a |
| `NamingOption.tsx` + `.css` + `.css.d.ts` | Shared token-card primitive — used by both TV and manga modals | n/a |
| `TokenCase.ts` / `TokenSeparator.ts` | Shared TS unions | n/a |
| `useNamingSettings.ts` | TV settings + examples hooks | `/settings/naming` + `/settings/naming/examples` |
| **`MangaNaming.tsx`** | **NEW (Phase 7 D-05)** — Manga Naming FieldSet | `/api/v5/config/manganaming` (Phase 5 Plan 05-06) |
| **`MangaNamingModal.tsx`** | **NEW (Phase 7 D-05)** — manga token-picker modal | n/a (client) |
| **`MangaNaming.css` + `.css.d.ts`** | **NEW (Phase 7 D-05)** — manga naming styles | n/a |
| **`MangaNamingModal.css` + `.css.d.ts`** | **NEW (Phase 7 D-05)** — manga modal styles | n/a |
| **`useMangaNamingSettings.ts`** | **NEW (Phase 7 D-05)** — manga settings + presets hooks | `/config/manganaming` + `/config/manganaming/presets/manga` |

## Phase 7 D-05 Manga Naming Section

Per `.planning/phases/07-api-v5-frontend-manga-shell/07-CONTEXT.md` D-05 (executed in Plan 07-08):

The `MediaManagement.tsx` page now renders the existing `<Naming />` (TV Episode Naming) FieldSet AND a new `<MangaNaming />` (Manga Naming) FieldSet stacked. The page-level `SettingsToolbar` Save button dispatches BOTH child saves via `saveSettings.current.naming()` and `saveSettings.current.mangaNaming()`. Pending-change + isSaving aggregation merges all three sources (page-level + TV naming + manga naming).

**Backend endpoints wired (Phase 5 Plan 05-06):**
- `GET /api/v5/config/manganaming` — returns the manga-shape subset of `NamingConfig` (StandardChapterFormat / MangaFolderFormat / RenameChapters / ReplaceIllegalCharacters / ColonReplacementFormat).
- `PUT /api/v5/config/manganaming` — saves the manga fields. Server-side `ApplyMangaFields` preserves all TV-shape fields on the singleton row (so the TV `<Naming />` save and manga `<MangaNaming />` save do NOT clobber each other).
- `GET /api/v5/config/manganaming/presets/manga` — returns 4 reader-compat presets (Komga / Kavita / ComicRack / Custom; Phase 5 D-13..D-16). Selecting a preset patches `standardChapterFormat` + `mangaFolderFormat`.

**Per Pattern S2 (sibling-divergence header):** every NEW file under this directory carries an inline header naming the role-match analog (e.g., `MangaNaming.tsx` ↔ `Naming.tsx`) and the Phase 8 cleanup target.

**Phase 8 cleanup:** `Naming.tsx` + `NamingModal.tsx` + `useNamingSettings.ts` + `Naming.css` + `Naming.css.d.ts` + `NamingModal.css` + `NamingModal.css.d.ts` are deleted in the Phase 8 cutover. The `NamingOption.tsx` + `TokenCase.ts` + `TokenSeparator.ts` shared primitives stay (used by `MangaNamingModal.tsx`).

## Live Preview

`MangaNaming.tsx` includes a client-side token substitution preview rendered as a help-text line under the `StandardChapterFormat` and `MangaFolderFormat` inputs. Sample tokens include `{Manga.Title} → "Berserk"`, `{Chapter.Number} → "132"`, `{Chapter.Title} → "The Eclipse"`. A future plan may swap this for a backend `POST /preview` endpoint if richer preview is needed.

## Cross-References

- [../CLAUDE.md (Settings)](../../CLAUDE.md)
- [../../../typings/CLAUDE.md](../../../typings/CLAUDE.md)
- [src/Mangarr.Api.V5/CLAUDE.md](../../../../../src/Mangarr.Api.V5/CLAUDE.md)
- Phase 5 Plan 05-06 backend: [src/Mangarr.Api.V5/Config/MangaNamingConfigController.cs](../../../../../src/Mangarr.Api.V5/Config/MangaNamingConfigController.cs)
- Phase 5 D-13..D-16 preset locks: [.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md](../../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md)
