using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Phase 31 Plan 31-01 Task 1 (TDD RED) — verifies the Phase 31 D-02 schema-emit
    // filter contract: providers overriding MetadataSourceBase.IsDeprecated to true are
    // filtered out of the GET /api/v5/metadatasource/schema response so the Settings →
    // MetadataSources Add picker shows MangaDex only. The V5 controller filter
    // (MetadataSourceController.GetTemplates `new` override — Task 3) reads each
    // provider's IsDeprecated property; this fixture verifies the underlying contract
    // at the Core layer that the controller depends on.
    //
    // The Mangarr.Core.Test project cannot reference Mangarr.Api.V5 (no project reference;
    // see Mangarr.Core.Test.csproj). The IsDeprecated virtual + provider overrides ARE
    // the testable contract surface — once they ship, the controller-side `new`
    // GetTemplates override (Task 3) reads the same property and filters identically.
    //
    // Property access is via REFLECTION (System.Reflection.PropertyInfo) so the fixture
    // can compile in Task 1 BEFORE the IsDeprecated virtual is authored. Pre-Task-2 the
    // reflection lookup returns null (no such property) → coalesced to false → assertions
    // ('AniList must be deprecated') FAIL. Post-Task-2 the reflection lookup returns the
    // overridden true value → assertions pass.
    //
    // RED → GREEN transition:
    //   * Task 1 ships only this fixture (red — virtual + overrides don't exist yet).
    //   * Task 2 lands `public virtual bool IsDeprecated => false;` on MetadataSourceBase
    //     plus `public override bool IsDeprecated => true;` on AniList + MAL provider
    //     classes — fixture goes green.
    //   * Task 3 ships the V5 controller GetTemplates override consuming this contract.
    //
    // State-not-rendering assertion per feedback_verify_ui_state_not_just_rendering:
    // fixture asserts the actual virtual property VALUE on each provider class, not
    // just that the property exists.
    [TestFixture]
    public class MetadataSourceSchemaFilterFixture
    {
        // ============================================================
        // Test 1 — D-02: MangaDex provider stays VISIBLE (IsDeprecated == false default)
        // ============================================================
        [Test]
        public void MangaDex_provider_is_not_deprecated()
        {
            var provider = Activate<MangaDexMetadataSource>();

            ReadIsDeprecated(provider).Should().BeFalse(
                "MangaDex stays as the user-facing primary metadata source per D-01 reframe — its IsDeprecated must inherit the default false from MetadataSourceBase");
        }

        // ============================================================
        // Test 2 — D-02: AniList provider is DEPRECATED (filtered from Add picker)
        // ============================================================
        [Test]
        public void AniList_provider_is_deprecated()
        {
            var provider = Activate<AniListMetadataSource>();

            ReadIsDeprecated(provider).Should().BeTrue(
                "AniList must override IsDeprecated to true per Phase 31 D-02 — hidden from Settings → MetadataSources Add picker");
        }

        // ============================================================
        // Test 3 — D-02: MyAnimeList provider is DEPRECATED (filtered from Add picker)
        // ============================================================
        [Test]
        public void MyAnimeList_provider_is_deprecated()
        {
            var provider = Activate<MyAnimeListMetadataSource>();

            ReadIsDeprecated(provider).Should().BeTrue(
                "MyAnimeList must override IsDeprecated to true per Phase 31 D-02 — hidden from Settings → MetadataSources Add picker");
        }

        // ============================================================
        // Test 4 — D-02 filter pipeline: simulate the controller's filter against
        // a synthetic provider list. The V5 controller filter (Task 3) builds a
        // HashSet<string> of Implementation names whose IsDeprecated == true and
        // excludes those entries from the GetTemplates response. Verify the filter
        // predicate logic against the actual provider classes.
        // ============================================================
        [Test]
        public void IsDeprecated_filter_excludes_anilist_and_mal_keeps_mangadex()
        {
            var providers = new IMetadataSource[]
            {
                Activate<MangaDexMetadataSource>(),
                Activate<AniListMetadataSource>(),
                Activate<MyAnimeListMetadataSource>(),
            };

            // This is the same filter shape the V5 controller's GetTemplates `new`
            // override applies (Task 3). The Core-test layer can't import the V5
            // controller, so we mirror the contract here.
            var deprecatedImpls = providers
                .Where(ReadIsDeprecated)
                .Select(p => p.GetType().Name)
                .ToHashSet();

            deprecatedImpls.Should().BeEquivalentTo(
                new[] { "AniListMetadataSource", "MyAnimeListMetadataSource" },
                "the deprecation filter must capture AniList + MAL implementations and leave MangaDex unfiltered");

            var visibleImpls = providers
                .Select(p => p.GetType().Name)
                .Where(name => !deprecatedImpls.Contains(name))
                .ToList();

            visibleImpls.Should().BeEquivalentTo(
                new[] { "MangaDexMetadataSource" },
                "post-filter only MangaDexMetadataSource remains visible — Settings → MetadataSources Add picker shows MangaDex only per D-02");
        }

        // ============================================================
        // Helper — activate a provider via uninitialized-object construction
        // (bypasses DI ctor since IsDeprecated is a property accessor with no
        // dependency reads). RuntimeHelpers.GetUninitializedObject is the
        // .NET 8+ replacement for the obsolete FormatterServices equivalent.
        // ============================================================
        private static T Activate<T>()
            where T : class
        {
            return (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
        }

        // ============================================================
        // Helper — reflection-only IsDeprecated read. Returns false when the
        // property doesn't exist yet (pre-Task-2 RED state) — which makes the
        // "deprecated" assertions FAIL until the virtual + overrides ship.
        // ============================================================
        private static bool ReadIsDeprecated(IMetadataSource provider)
        {
            var prop = provider.GetType().GetProperty("IsDeprecated");
            return prop != null && (bool)(prop.GetValue(provider) ?? false);
        }
    }
}
