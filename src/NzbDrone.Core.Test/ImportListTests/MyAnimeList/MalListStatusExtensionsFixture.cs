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
    // RED before Task 3 lands: the MalListStatusExtensions class doesn't exist yet, so this
    // fixture fails to compile. The fixture is authored upfront (Task 1 RED) per the plan's
    // TDD ordering, then turns GREEN when Task 3 ships the extension method.
    //
    // GREEN after Task 3 lands: extension method reads [EnumMember] via cached reflection;
    // each TestCase passes round-trip.
    //
    // WAVE 0 RED stub: the MalListStatusExtensions class does not exist until Task 3 ships.
    // To keep the build green at Task 1 (so the other two fixtures' RED-ness is observable),
    // the test bodies below are kept empty + marked [Ignore("Wave 0 — implementation in
    // Task 3")] until Task 3 removes the [Ignore] attributes and writes the real assertions.
    // The TestCase parameters + naming preserve the test contract so Task 3 only needs to
    // (a) drop [Ignore], (b) fill in the bodies with the real ToApiString assertions.
    [TestFixture]
    public class MalListStatusExtensionsFixture
    {
        [TestCase(MalListStatus.Reading, "reading")]
        [TestCase(MalListStatus.PlanToRead, "plan_to_read")]
        [TestCase(MalListStatus.Completed, "completed")]
        [TestCase(MalListStatus.OnHold, "on_hold")]
        [TestCase(MalListStatus.Dropped, "dropped")]
        [Ignore("Wave 0 — implementation in Task 3 (MalListStatusExtensions class does not exist yet).")]
        public void ToApiString_returns_EnumMember_value(MalListStatus status, string expected)
        {
            // Task 3 fills in: status.ToApiString().Should().Be(expected, ...);
            _ = status;
            _ = expected;
        }

        // ── cache-once invariant ──────────────────────────────────────────────────────
        [Test]
        [Ignore("Wave 0 — implementation in Task 3 (MalListStatusExtensions class does not exist yet).")]
        public void cache_is_built_once_per_assembly_load()
        {
            // Task 3 fills in:
            //   var first = MalListStatus.Reading.ToApiString();
            //   var second = MalListStatus.Reading.ToApiString();
            //   first.Should().Be("reading", ...);
            //   second.Should().Be(first, ...);
        }
    }
}
