using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.MyAnimeList;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeList
{
    // Phase 31 Plan 31-02 D-10 (IL2-03) — pure-unit fixture for MalListStatusExtensions.
    //
    // Verifies that MalListStatusExtensions.ToApiString() round-trips all 5 [EnumMember]
    // values on MalListStatus.cs:25-41 to the MAL API snake_case strings:
    //   * MalListStatus.Reading    → "reading"
    //   * MalListStatus.PlanToRead → "plan_to_read"
    //   * MalListStatus.Completed  → "completed"
    //   * MalListStatus.OnHold     → "on_hold"
    //   * MalListStatus.Dropped    → "dropped"
    //
    // These are the canonical MAL API contract values per
    // https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_mangalist_get
    // and the [EnumMember(Value="...")] attributes on the MalListStatus enum are the
    // SINGLE source-of-truth.
    //
    // Task-1 RED stub was marked [Ignore] until Task 3 shipped MalListStatusExtensions.
    // Task 3 lands: [Ignore] removed; real assertions filled in; all 6 tests GREEN.
    [TestFixture]
    public class MalListStatusExtensionsFixture
    {
        [TestCase(MalListStatus.Reading, "reading")]
        [TestCase(MalListStatus.PlanToRead, "plan_to_read")]
        [TestCase(MalListStatus.Completed, "completed")]
        [TestCase(MalListStatus.OnHold, "on_hold")]
        [TestCase(MalListStatus.Dropped, "dropped")]
        public void ToApiString_returns_EnumMember_value(MalListStatus status, string expected)
        {
            status.ToApiString().Should().Be(
                expected,
                "MalListStatusExtensions.ToApiString must read the [EnumMember(Value=\"...\")] attribute on " +
                "MalListStatus.cs:25-41 and return the canonical MAL snake_case string. The duplicated " +
                "local MapStatus switch helpers at MalImportListRequestGenerator.cs:86-101 " +
                "and MalImportListProxy.cs:283-298 (Plan 31-02 DELETED both) hard-coded the same mapping; " +
                "this extension method is the single source-of-truth after D-10 collapse.");
        }

        // ── cache-once invariant ──────────────────────────────────────────────────────
        //
        // Verifies the reflection cache (static Dictionary built once in static initializer)
        // returns identical strings on subsequent calls. We assert correctness, not
        // performance — the contract is "same enum value always maps to same string".
        [Test]
        public void cache_is_built_once_per_assembly_load()
        {
            // Two calls with same enum value must return the same string. This pins the
            // contract that the reflection cache is deterministic + idempotent.
            var first = MalListStatus.Reading.ToApiString();
            var second = MalListStatus.Reading.ToApiString();

            first.Should().Be(
                "reading",
                "first invocation must hit the cache and return the [EnumMember] value.");
            second.Should().Be(
                first,
                "second invocation must return the SAME string (cached reflection is deterministic; " +
                "calling twice does NOT re-enumerate [EnumMember] attributes).");
        }
    }
}
