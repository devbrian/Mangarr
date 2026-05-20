namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 (D-10) — AniList MediaListStatus enum projected to
    // GraphQL variable strings.
    //
    // AniList's GraphQL schema declares `enum MediaListStatus { CURRENT, PLANNING,
    // COMPLETED, DROPPED, PAUSED, REPEATING }` (verified — https://docs.anilist.co/
    // reference/object/medialist). The 6 enum values below match the API's string
    // projections exactly (UPPERCASE) so `Settings.Status.ToString().ToUpperInvariant()`
    // round-trips into the GraphQL `$status: MediaListStatus` variable verbatim per
    // Plan 27-03 Task 2 Test 4.
    //
    // D-10 single-select shape (Sonarr-canonical Trakt user-list pattern): users who
    // want multiple statuses create multiple ImportLists (one per status). No
    // multi-select / TagSelect — that's a v1.2+ enhancement per CONTEXT line 442.
    //
    // Field rendering is driven by `[FieldDefinition(Type = FieldType.Select,
    // SelectOptions = typeof(AniListListStatus))]` on `AniListImportListSettings.Status`;
    // the FE generic ProviderFieldFormGroup renders the enum members as a
    // single-select dropdown.
    public enum AniListListStatus
    {
        CURRENT,
        PLANNING,
        COMPLETED,
        PAUSED,
        DROPPED,
        REPEATING
    }
}
