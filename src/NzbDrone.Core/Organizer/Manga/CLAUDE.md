# Organizer/Manga

## Purpose
Manga naming engine — token resolution, path builder, reader-compat presets, V5 controller. Sibling to TV `Organizer/` until Phase 8 collapse.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Organizer\Manga`

## Key Files
| File | Purpose |
|------|---------|
| `MangaNamingPresets.cs` | Static list of 4 reader-compat presets (Komga default, Kavita, ComicRack, Custom) |
| `MangaFileNameBuilder.cs` | Token resolution: {Manga.Title} / {Chapter.Number:000} / {Language} / etc. |
| `MangaPathBuilder.cs` | Flat folder layout per D-15: `<root>/<Manga Title>/<Chapter NNN>.cbz` |
| `IBuildMangaFileNames.cs` | Manga sibling to `IBuildFileNames` (TV) — operates on `Chapter` / `Manga` / `ReleaseInfo` (not `EpisodeFile`) |

## Patterns / Conventions
- Reuses Mangarr's `FileNameBuilder.TitleRegex` (lines 48-49) for token+padding regex semantics — DO NOT REINVENT
- Padding uses C# canonical `IFormattable.ToString("000.0", CultureInfo.InvariantCulture)` per Pitfall 7 — culture-invariant
- Default seeded preset = Komga per D-16
- All preset templates locked per 05-RESEARCH.md Pattern 5 (web-verified against Komga / Kavita / ComicRack docs)
- Folder layout = flat (D-15): `<root>/<Manga Title>/<chapter file>` — no volume nesting in v1

## Manga Adaptation Notes
This is a NEW manga-side directory. Mangarr's `FileNameBuilder` handles `{Series Title}` / `{episode:00}` etc.; manga sibling handles `{Manga.Title}` / `{Chapter.Number:000}` / `{Manga.MangaDexId}` etc. Token set per D-14:

- Required (READER-02): `{Manga.Title}`, `{Manga.MalId}`, `{Chapter.Number}`, `{Chapter.Title}`, `{ScanlationGroup}`, `{Language}`
- Plus per user direction 2026-05-03: `{Manga.MangaDexId}` (Guid), `{Manga.AniListId}` (int), padding control on chapter number, `{Source}`
- NOT in v1: `{Chapter.AbsoluteNumber}`, `{Chapter.Volume}`, `{Chapter.Type}`

Note: `Manga.MangaDexId` is `Guid?` (singular), `Manga.MalId` is `int?` (singular), `Manga.AniListId` is `int?` (singular) per Phase 2 D-09. NOT collections.

## Phase 8 Collapse
When `Tv/` deletes in Phase 8, this directory collapses into the canonical `Organizer/` namespace. The TV-shaped FileNameBuilder + SeriesPathBuilder delete in the same cutover.

## Cross-References
- [Phase 5 CONTEXT](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-13, D-14, D-15, D-16
- [Phase 5 RESEARCH](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-RESEARCH.md) — Pattern 5 (locked preset templates), Pitfall 7 (padding)
- [Phase 5 PATTERNS-MAP](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-PATTERNS.md) — pattern S10 ([SetCulture("de-DE")] regression)
- Sonarr pattern sources this tree was modelled on — `Organizer/FileNameBuilder.cs` + `Tv/SeriesPathBuilder.cs` — DELETED in the Phase 15 `Tv/` removal; cited for provenance only, absent at HEAD. The live builders are [`MangaFileNameBuilder.cs`](./MangaFileNameBuilder.cs) + [`MangaPathBuilder.cs`](./MangaPathBuilder.cs) in this directory.
