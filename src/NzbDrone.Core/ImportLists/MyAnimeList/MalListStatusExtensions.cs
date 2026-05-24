using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 31 D-10 (IL2-03) — single source-of-truth for MalListStatus → MAL API
    // snake_case string. Reads [EnumMember(Value="...")] attributes on
    // MalListStatus.cs:25-41 via cached reflection built ONCE in the static
    // initializer; per-call O(1) dictionary lookup.
    //
    // Replaces the duplicated local static `MapStatus`-switch helper methods at:
    //   * MalImportListRequestGenerator.cs:86-101 (DELETED by Plan 31-02 Task 3)
    //   * MalImportListProxy.cs:283-298         (DELETED by Plan 31-02 Task 3)
    //
    // The [EnumMember] attribute strings on MalListStatus.cs:27-39 are the
    // canonical mapping per
    // https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_mangalist_get
    // (verified Phase 27 D-10 — snake_case verbatim). Reading them via reflection
    // here means the snake_case constants live in exactly one place; updating an
    // [EnumMember(Value="...")] string on the enum updates every call site
    // automatically.
    //
    // Pattern: static-class skeleton mirrors src/NzbDrone.Core/MediaFiles/FileExtensions.cs
    // (also a static class with a static-cached collection); the cached-reflection
    // shape itself has no prior analog in the codebase per 31-PATTERNS.md §Plan 31-02.
    public static class MalListStatusExtensions
    {
        private static readonly Dictionary<MalListStatus, string> StringMap = BuildMap();

        // Public API — extension method on MalListStatus. Returns the [EnumMember]
        // value verbatim; falls back to the Reading value when an unknown enum
        // value is somehow passed in (back-compat with the deleted switch
        // statements' default arm `_ => "reading"`).
        public static string ToApiString(this MalListStatus status)
        {
            return StringMap.TryGetValue(status, out var v) ? v : StringMap[MalListStatus.Reading];
        }

        // Build the dictionary once at type-init time. Uses Enum.GetValues<T>() +
        // reflection over the enum's static fields to read the [EnumMember]
        // attribute on each value.
        //
        // WR-06 (GH #257): if a future refactor drops the [EnumMember] attribute from
        // any value (e.g., PlanToRead, OnHold) the prior fallback `v.ToString().ToLowerInvariant()`
        // would yield "plantoread" / "onhold" — incorrect MAL API snake_case. MAL's API
        // would 400 silently, breaking the corresponding ImportList until a user reports
        // it. We now throw at static-init time so the failure is LOUD: assembly load
        // fails immediately with a clear actionable message, and the regression is
        // impossible to land via PR review without surfacing.
        //
        // The defensive regression-guard test
        // (MalListStatusExtensionsFixture.all_MalListStatus_members_have_EnumMember_attribute)
        // pins this invariant at test-time.
        private static Dictionary<MalListStatus, string> BuildMap()
        {
            var type = typeof(MalListStatus);
            return Enum.GetValues<MalListStatus>().ToDictionary(
                v => v,
                v => type.GetField(v.ToString())
                         ?.GetCustomAttribute<EnumMemberAttribute>()
                         ?.Value ?? throw new InvalidOperationException(
                             $"MalListStatus.{v} is missing [EnumMember(Value=\"...\")] attribute — " +
                             "required for ToApiString() to return the canonical MAL snake_case string."));
        }
    }
}
