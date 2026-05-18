// Sonarr divergence: NEW per Phase 25 Plan 25-04 Task 4 (v1.1-03 + D-04) —
// see DIVERGENCE.md.
//
// Per-row 'On Existing File' behavior. 2-value enum per D-04
// recommendation (manga has no episode-merge analog for Sonarr's
// "Combine" — see PROJECT.md Out-of-Scope row "no folder-import for
// manga, no orphan-file recovery UI, no failed-parse override").
//
// Encoding LOCKED to camelCase strings ('skip' / 'replace') matching the
// C# `JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true)`
// registered globally in `src/NzbDrone.Common/Serializer/System.Text.Json/STJson.cs`
// line 33. Numeric encoding is FORBIDDEN — C#'s strict-mode converter
// would silently route an integer Replace value to the Skip slot via the
// integer-fallback path; locking to strings prevents the bug entirely.
//
// Default behavior: Skip — preserves user-locked no-destructive-default
// safety. Users must opt in per-row to enable Replace.
//
// Mirrors C# `src/NzbDrone.Core/MediaFiles/MangaImport/Manual/ExistingFileBehavior.cs`
// authored alongside in Plan 25-04 Task 7.
export enum ExistingFileBehavior {
  Skip = 'skip',
  Replace = 'replace',
}

export default ExistingFileBehavior;
