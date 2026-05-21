// Phase 27.1 Plan 27.1-02 Task 3 — Companion typing for ImportListOptions.css.
// Empty `styles` export — the .css module ships zero selectors per the
// comment block in ImportListOptions.css. Mangarr's webpack has no
// typed-css-modules auto-gen step (RESEARCH Anti-Patterns), so this stub is
// hand-written and kept in lockstep with the .css file's selector inventory.

declare const styles: Readonly<Record<string, string>>;

export default styles;
