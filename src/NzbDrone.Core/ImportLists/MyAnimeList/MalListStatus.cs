using System.Runtime.Serialization;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 (D-10) — MyAnimeList list-status enum projected to MAL API
    // string variable values. 5 values per CONTEXT line 53 + RESEARCH §STACK §Surface 2.
    //
    // MAL's `/v2/users/@me/mangalist?status={value}` query accepts SNAKE_CASE values
    // verbatim — `reading`, `plan_to_read`, `completed`, `on_hold`, `dropped` — per
    // https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_mangalist_get.
    //
    // C# enum members use Pascal-case for project-wide convention (Sonarr-canonical
    // enum identifier style). The `[EnumMember(Value = "...")]` Newtonsoft attribute
    // overrides the default serialized form so the enum round-trips through JSON
    // bodies AND the MAL query string in the MAL-required snake_case spelling.
    //
    // D-10 single-select shape (Sonarr-canonical Trakt user-list pattern): users who
    // want multiple statuses create multiple ImportLists (one per status). No
    // multi-select / TagSelect — that's a v1.2+ enhancement per CONTEXT line 442.
    //
    // Field rendering is driven by `[FieldDefinition(Type = FieldType.Select,
    // SelectOptions = typeof(MalListStatus))]` on `MalImportListSettings.Status`; the
    // FE generic ProviderFieldFormGroup renders the enum members as a single-select
    // dropdown.
    public enum MalListStatus
    {
        [EnumMember(Value = "reading")]
        Reading,

        [EnumMember(Value = "plan_to_read")]
        PlanToRead,

        [EnumMember(Value = "completed")]
        Completed,

        [EnumMember(Value = "on_hold")]
        OnHold,

        [EnumMember(Value = "dropped")]
        Dropped
    }
}
