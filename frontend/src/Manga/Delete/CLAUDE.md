# Manga/Delete/

## Purpose

Single-manga Delete confirmation modal — wired into the Delete toolbar button on `MangaDetails.tsx`. Previously deferred under Phase 15 Plan 15-12 as a `{isDeleteModalOpen ? null : null}` stub; shipped here as the second of the two deferred per-manga modal sub-trees (the first was `Manga/Edit/`, shipped in PR #27 — `fix(manga-edit-button-no-op)`).


## Key Files

| File | Purpose |
|------|---------|
| `DeleteMangaModal.tsx` | Thin Modal-wrapping component that mounts `DeleteMangaModalContent` when `isOpen`. Mirrors `Manga/Edit/EditMangaModal.tsx` exactly. |
| `DeleteMangaModalContent.tsx` | Form body: ModalHeader (`DeleteSelectedSeries - {title}`), ModalBody with three FormGroups — Path readout (with chapter-file count + size-on-disk when present), `addImportListExclusion` (CHECK, zustand-persisted via `useMangaDeleteOptions`), `deleteFiles` (CHECK, local state, kind=danger). Confirmation copy block toggles between `DeleteMangaFolderCountConfirmation` and `DeleteMangaFolderCountWithFilesConfirmation`. ModalFooter with Cancel + SpinnerErrorButton (kind=danger, label=Delete) wired to `useDeleteManga(mangaId, { deleteFiles, addImportListExclusion })`. Auto-closes on success via `usePrevious(isDeleting)` + useEffect (mirrors `EditMangaModalContent.tsx:127-132`). |
| `DeleteMangaModalContent.css` + `.css.d.ts` | CSS modules — adapted from `Manga/Index/Select/Delete/DeleteMangaModalContent.css` (path / pathContainer / statistics / message / deleteFilesMessage classes) plus modalFooter / modalFooterButtons composes from EditMangaModalContent.css. |

## Patterns / Conventions

- **Hook signature gotcha (`useDeleteManga`)** — Unlike `useSaveManga` / `useToggleMangaMonitored`, `useDeleteManga(mangaId, options)` takes the options at hook-construction time and bakes them into `queryParams`. The modal handles this by constructing the hook with `{ deleteFiles, addImportListExclusion }` derived from form state, so each render's hook instance has the current queryParams. The mutate call (`deleteManga()`) takes no args; the queryParams come from the hook closure. This is how the bulk modal at `Manga/Index/Select/Delete/` works for `useBulkDeleteManga` too.
- **No explicit post-delete navigation** — On successful delete, `useDeleteManga.onSuccess` filters the manga out of the `['/manga']` React Query cache. `MangaDetailsPage.tsx:34-44` watches `mangaIndex === -1 && previousIndex !== -1` and `history.push`es to `/manga` automatically. The modal just calls `onModalClose()` to dismiss the dialog; navigation runs organically.
- **Persistence model mirrors the bulk delete modal** — `addImportListExclusion` persists across modal opens (zustand `useMangaDeleteOptions()`); `deleteFiles` resets to false on each open (local `useState`). This keeps single-manga and bulk-select flows on the same UX contract for which knobs stick.
- **i18n keys reuse Mangarr's existing strings** — `DeleteSelectedSeries`, `DeleteMangaFolder`, `DeleteMangaFolderHelpText`, `DeleteMangaFolderCountConfirmation`, `DeleteMangaFolderCountWithFilesConfirmation`, `AddListExclusion`, `AddListExclusionSeriesHelpText`, `Path`, `Chapters`, `SizeOnDisk`, `Cancel`, `Delete`. No new keys added — Phase 11 i18n pass will rename the `Series` references later.
- **Sibling-divergence comments (Pattern S2)** — both `.tsx` files carry the `// Sonarr divergence: ...` header naming the role-match analog. The role-match analog `Series/Delete/DeleteSeriesModal.tsx` was deleted in Phase 17.3 Plan 17.3-13 atomic stub-dir delete; the structural reference is `Manga/Edit/EditMangaModal.tsx` (the proven sibling shipped in PR #27).

## Manga Adaptation Notes

This directory IS the manga canonical for single-manga Delete. The Series
sibling at `frontend/src/Series/Delete/DeleteSeriesModal.tsx` was a Phase 15
Plan 15-12 `() => null` stub that was deleted in Phase 17.3 Plan 17.3-13
atomic stub-dir delete (D-09/D-10).

## Cross-References

- [../Edit/](../Edit/) — sibling per-manga modal shipped in PR #27 (`fix(manga-edit-button-no-op)`) — closest structural reference.
- [../Index/Select/Delete/](../Index/Select/Delete/) — bulk Delete modal sibling (multi-select form-shape source).
- [../useManga.ts](../useManga.ts) — `useDeleteManga` hook (~line 509).
- [../Details/MangaDetails.tsx](../Details/MangaDetails.tsx) — call-site (Delete toolbar button + modal mount).
- [../Details/MangaDetailsPage.tsx](../Details/MangaDetailsPage.tsx) — owner of the redirect-on-vanish effect that handles post-delete navigation.
- [manga-delete-button-no-op.md](../../../../.planning/debug/resolved/manga-delete-button-no-op.md) — debug session that produced this fix (moved to `debug/resolved/`).
