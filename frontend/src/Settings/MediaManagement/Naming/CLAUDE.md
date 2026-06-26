# Settings/MediaManagement/Naming/

## Purpose

Manga Naming FieldSet + token-picker modal for the Settings → Media Management page. The TV `Naming.tsx` / `NamingModal.tsx` FieldSet was deleted in the Phase 8 cutover; only the manga naming UI remains.

## Files

| File | Role | Backend |
|------|------|---------|
| `MangaNaming.tsx` | Manga Naming FieldSet | `/api/v5/config/manganaming` |
| `MangaNamingModal.tsx` | Manga token-picker modal | n/a (client) |
| `MangaNaming.css` + `.css.d.ts` | Manga naming styles | n/a |
| `MangaNamingModal.css` + `.css.d.ts` | Manga modal styles | n/a |
| `useMangaNamingSettings.ts` | Manga settings + presets hooks | `/config/manganaming` + `/config/manganaming/presets/manga` |
| `NamingOption.tsx` + `.css` + `.css.d.ts` | Shared token-card primitive (used by the manga modal) | n/a |
| `TokenCase.ts` / `TokenSeparator.ts` | Shared TS unions | n/a |
| `useNamingSettings.ts` | Orphan TV hook left by the Phase 8 cutover (no live importer) | n/a |

## Backend endpoints (Phase 5 Plan 05-06)

- `GET /api/v5/config/manganaming` — returns the manga-shape subset of `NamingConfig` (StandardChapterFormat / MangaFolderFormat / RenameChapters / ReplaceIllegalCharacters / ColonReplacementFormat).
- `PUT /api/v5/config/manganaming` — server-side `ApplyMangaFields` preserves the TV-shape fields on the singleton row.
- `GET /api/v5/config/manganaming/presets/manga` — returns 4 reader-compat presets (Komga / Kavita / ComicRack / Custom; Phase 5 D-13..D-16). Selecting a preset patches `standardChapterFormat` + `mangaFolderFormat`.

## Preset Selector Behavior (UX-fix 2026-06-06)

The "Naming Preset" dropdown in `MangaNaming.tsx` is a quick-fill for the format text fields, not a separate custom-name input. After a 2026-06-06 usability fix (GH user report — "selecting Custom does nothing / no box"):

- **No "Select preset" placeholder.** On load the selector *derives* its value by matching the saved `standardChapterFormat` + `mangaFolderFormat` against the known presets — so a fresh DB (Komga defaults per D-16) shows **Komga**, not a blank. Derivation excludes "Custom" from matching because its backend template is byte-identical to Komga (`MangaNamingPresets.cs:40-44`).
- **Editing a format field flips the selector to "Custom"** (`handleInputChange` → `setSelectedPreset('Custom')`). "Custom" is a *manual-edit marker*; selecting it from the dropdown is a no-op on the fields (it never clobbers the user's edits — `handlePresetChange` early-returns on `'Custom'`).
- **`StandardChapterFormat` is always visible** — previously gated behind the `RenameChapters` toggle (defaults `false`), which hid the chapter-naming box and made it look like there was no way to enter a custom name. It now always renders so the preset templates are discoverable. The template is still only *applied* on import when `RenameChapters` is on (backend-owned); when the toggle is off, a help-text hint (`StandardChapterFormatRenameDisabledHelpText`) renders under the box so the visible-but-inert field isn't misleading.
- State model: `selectedPreset: string | null` (null = use derived); `effectivePreset = selectedPreset ?? derivedPreset` feeds the SELECT `value`.

## Live Preview

`MangaNaming.tsx` includes a client-side token substitution preview rendered as a help-text line under the `StandardChapterFormat` and `MangaFolderFormat` inputs. Sample tokens include `{Manga.Title} → "Berserk"`, `{Chapter.Number} → "132"`, `{Chapter.Title} → "The Eclipse"`. A future plan may swap this for a backend `POST /preview` endpoint if richer preview is needed.

## Cross-References

- [../CLAUDE.md (Settings)](../../CLAUDE.md)
- [../../../typings/CLAUDE.md](../../../typings/CLAUDE.md)
- [src/Mangarr.Api.V5/CLAUDE.md](../../../../../src/Mangarr.Api.V5/CLAUDE.md)
- Phase 5 Plan 05-06 backend: [src/Mangarr.Api.V5/Config/MangaNamingConfigController.cs](../../../../../src/Mangarr.Api.V5/Config/MangaNamingConfigController.cs)
- Phase 5 D-13..D-16 preset locks: [.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md](../../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md)
