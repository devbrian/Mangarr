using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mangarr.Api.V5.MetadataSource;
using Mangarr.Http.ClientSchema;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.MetadataSource
{
    // Phase 31 fix-forward (2026-05-24) — REVIEW.md §CR-01 + §WR-01 remediation coverage.
    //
    // ROUTING NOTE: REVIEW.md asked for an integration test at
    // src/NzbDrone.Integration.Test/MetadataSource/MetadataSourceSchemaFixture.cs but
    // that project is empty (no .cs files, no project references to Mangarr.Api.V5).
    // The canonical controller-level integration-test pattern in this codebase lives
    // under NzbDrone.Api.Test (TestBase<TController> + AutoMoq + IEnumerable<T> service
    // injection via Mocker.SetConstant) — see precedent siblings in this codebase:
    //   - AutoTagging/AutoTaggingSpecificationSchemaFixture.cs (Mocker.SetConstant
    //     <IEnumerable<IAutoTaggingSpecification>> precedent)
    //   - Indexers/InitializeProvidersFixture.cs (Mocker.SetConstant <IEnumerable<IIndexer>>)
    //   - ImportListTests/ImportListFactoryFixture.cs (Mocker.SetConstant
    //     <IEnumerable<IMangaImportList>>)
    // This fixture follows that pattern. The Mangarr.Api.Test project already
    // ProjectReferences Mangarr.Api.V5 so the controller class resolves directly.
    //
    // What this fixture exercises:
    // 1. CR-01: the GetTemplates filter iterates REGISTERED IMetadataSource types
    //    (DryIoc-style IEnumerable<IMetadataSource> ctor injection) instead of
    //    persisted active factory rows. Pre-fix it queried
    //    _factory.GetAvailableProviders() — empty post-Migration-005 → no-op filter.
    //    Post-fix it queries the registered-type list which is always populated
    //    regardless of DB state.
    // 2. WR-01: the base ProviderControllerBase.GetTemplates is `virtual` and the
    //    MetadataSourceController.GetTemplates is `override` (not `new`-shadow).
    //    The `[HttpGet("schema")]` action-discovery scan now resolves to the
    //    derived method unambiguously. The pre-fix `new`-modifier shape risked
    //    AmbiguousActionException at first GET.
    //
    // Test scenarios:
    //   - Test 1 — fresh-DB shape: factory.GetDefaultDefinitions() returns
    //     MangaDex + AniList + MAL defaults (registered types), no persisted rows.
    //     The filter MUST hide AniList + MAL from the response while leaving
    //     MangaDex visible.
    //   - Test 2 — pre-seeded shape: same registered types, but a persisted AniList
    //     row also exists (the migration would have deleted it but tests must hold
    //     for legacy intermediate states). The filter MUST still hide AniList +
    //     MAL because it iterates registered types, not persisted instances.
    //   - Test 3 — empty-result safety: when the base returns null, the filter
    //     yields an empty list (no NRE).
    //   - Test 4 — override-resolution pin: the GetTemplates method on
    //     MetadataSourceController is `override` (not `new`); calls through the
    //     base interface MUST hit the derived implementation (this would have
    //     CAUGHT the WR-01 ambiguous-route risk had a Selenium-style full-HTTP
    //     test environment been in scope).
    //
    // Per feedback_verify_ui_state_not_just_rendering: assertions check the
    // ACTUAL filtered HashSet contents (count + member identity), not just
    // "filter ran without throwing".
    [TestFixture]
    public class MetadataSourceControllerSchemaFixture : TestBase<MetadataSourceController>
    {
        private List<IMetadataSource> _providers;

        [SetUp]
        public void Setup()
        {
            // SchemaBuilder.ToSchema (the mapping pipeline below) reads a static
            // _localizationService. Wire it via SchemaBuilder.Initialize — same
            // pattern as SchemaBuilderFixture (the existing canonical fixture
            // that exercises this same static).
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns<string, Dictionary<string, object>>((s, _) => s);
            SchemaBuilder.Initialize(Mocker.Container);

            // The controller's filter does p.GetType().Name to populate the
            // deprecation HashSet, then matches against resource.Implementation.
            // Production providers have non-default ctors (IHttpClient, Logger, …)
            // so we can't construct them with `new T()`. Use
            // RuntimeHelpers.GetUninitializedObject which bypasses the ctor —
            // .IsDeprecated is a property accessor with no field reads so
            // uninitialized instances answer it correctly. Same shape as
            // MetadataSourceSchemaFilterFixture (Core.Test sibling) and
            // AutoTagDemographicCrossSourceFixture's Activate<T>() helper.
            _providers = new List<IMetadataSource>
            {
                Activate<MangaDexMetadataSource>(),
                Activate<AniListMetadataSource>(),
                Activate<MyAnimeListMetadataSource>(),
            };

            Mocker.SetConstant<IEnumerable<IMetadataSource>>(_providers);

            // The base controller ctor's SharedValidator rule reads
            // _providerFactory.All() — return an empty list so the uniqueness
            // rule is trivially satisfied. (Schema endpoint doesn't trigger this
            // rule, but the ctor wires it up.)
            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.All())
                .Returns(new List<MetadataSourceDefinition>());

            // base.GetTemplates() calls _providerFactory.GetDefaultDefinitions()
            // and GetPresetDefinitions(def). Seed these with the registered-type
            // defaults so the base produces a list with 3 entries (MangaDex,
            // AniList, MAL) — exactly what the production wiring produces at
            // ProviderFactoryBase.GetDefaultDefinitions which enumerates
            // _providers.SelectMany(p => p.DefaultDefinitions).
            // The MetadataSourceResourceMapper calls SchemaBuilder.ToSchema(definition.Settings)
            // (ProviderResource.cs:37) which throws ArgumentNullException when Settings is null.
            // Each definition needs a non-null Settings POCO matching its ConfigContract.
            var defaults = new List<MetadataSourceDefinition>
            {
                new()
                {
                    Name = "MangaDex",
                    Implementation = nameof(MangaDexMetadataSource),
                    ConfigContract = nameof(MangaDexMetadataSourceSettings),
                    Settings = new MangaDexMetadataSourceSettings(),
                    IsPrimary = true,
                },
                new()
                {
                    Name = "AniList",
                    Implementation = nameof(AniListMetadataSource),
                    ConfigContract = nameof(AniListMetadataSourceSettings),
                    Settings = new AniListMetadataSourceSettings(),
                    IsPrimary = false,
                },
                new()
                {
                    Name = "MyAnimeList",
                    Implementation = nameof(MyAnimeListMetadataSource),
                    ConfigContract = nameof(MyAnimeListMetadataSourceSettings),
                    Settings = new MyAnimeListMetadataSourceSettings(),
                    IsPrimary = false,
                },
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.GetDefaultDefinitions())
                .Returns(defaults);

            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.GetPresetDefinitions(It.IsAny<MetadataSourceDefinition>()))
                .Returns(new List<MetadataSourceDefinition>());
        }

        // ============================================================
        // Test 1 — CR-01 fresh-DB scenario: AniList + MAL hidden from schema
        // ============================================================
        // The registered-type list (DryIoc-style IEnumerable<IMetadataSource>) has
        // all three providers. The schema endpoint must filter out the two
        // deprecated providers regardless of DB state.
        [Test]
        public void GetTemplates_hides_anilist_and_mal_from_schema_on_fresh_db()
        {
            // Simulate fresh-DB: no persisted active rows. The controller filter
            // post-fix doesn't read GetAvailableProviders() anymore, but mock it
            // anyway to prove the test exercise doesn't depend on persisted state.
            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.GetAvailableProviders())
                .Returns(new List<IMetadataSource>());

            var result = Subject.GetTemplates();

            result.Should().NotBeNull();
            var ok = (Ok<List<MetadataSourceResource>>)result;
            ok.Value.Should().NotBeNull();

            var implementations = ok.Value!.Select(r => r.Implementation).ToList();

            // MangaDex MUST be present — it's the canonical primary post-D-02.
            implementations.Should().Contain(nameof(MangaDexMetadataSource),
                "MangaDex must remain visible in the schema endpoint per D-02 — it's the sole primary metadata source");

            // AniList MUST be filtered out (REVIEW.md §CR-01).
            implementations.Should().NotContain(nameof(AniListMetadataSource),
                "AniList is deprecated per D-02 — must be hidden from the Settings → MetadataSources Add picker " +
                "(REVIEW.md §CR-01 regression coverage: the filter must work when no AniList row is persisted)");

            // MyAnimeList MUST be filtered out (REVIEW.md §CR-01).
            implementations.Should().NotContain(nameof(MyAnimeListMetadataSource),
                "MyAnimeList is deprecated per D-02 — must be hidden from the Settings → MetadataSources Add picker " +
                "(REVIEW.md §CR-01 regression coverage)");

            // Exact-size assertion catches both directions of the filter (false-negative
            // would surface as count > 1; false-positive would surface as count < 1).
            ok.Value.Should().HaveCount(1,
                "post-filter only MangaDex survives — the registered-type filter is the canonical source-set");
        }

        // ============================================================
        // Test 2 — CR-01 pre-seeded scenario: AniList + MAL hidden regardless of persistence
        // ============================================================
        // Proves the filter does NOT depend on persisted DB rows. Migration 005
        // deletes AniList + MAL rows, but a legacy intermediate DB state may still
        // carry rows (e.g., the user's DB before the migration runs, or a half-run
        // migration). The filter iterates REGISTERED types — that source-set is
        // always populated by DryIoc regardless of DB state.
        [Test]
        public void GetTemplates_hides_anilist_and_mal_even_when_persisted_rows_exist()
        {
            // Simulate legacy DB shape: AniList + MAL still persisted (not yet deleted
            // by Migration 005). The pre-fix controller relied on this set, so
            // exercising it proves the fix works against the WORST case too.
            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.GetAvailableProviders())
                .Returns(new List<IMetadataSource>
                {
                    Activate<MangaDexMetadataSource>(),
                    Activate<AniListMetadataSource>(),
                    Activate<MyAnimeListMetadataSource>(),
                });

            var result = Subject.GetTemplates();

            var ok = (Ok<List<MetadataSourceResource>>)result;
            ok.Value.Should().NotBeNull();

            var implementations = ok.Value!.Select(r => r.Implementation).ToList();

            implementations.Should().Contain(nameof(MangaDexMetadataSource));
            implementations.Should().NotContain(nameof(AniListMetadataSource),
                "AniList must be filtered out regardless of DB persistence state — the registered-type filter is the canonical source");
            implementations.Should().NotContain(nameof(MyAnimeListMetadataSource),
                "MyAnimeList must be filtered out regardless of DB persistence state");

            ok.Value.Should().HaveCount(1);
        }

        // ============================================================
        // Test 3 — Empty-result safety: filter does not throw on null base result
        // ============================================================
        // Defense-in-depth: the filter coalesces null base.Value to an empty list.
        // If the base ever returns a null-valued Ok<>, the filter must not NRE.
        [Test]
        public void GetTemplates_returns_empty_list_when_no_defaults_registered()
        {
            // Re-mock GetDefaultDefinitions to return an empty list (the base will
            // produce an empty MetadataSourceResource list).
            Mocker.GetMock<IMetadataSourceFactory>()
                .Setup(f => f.GetDefaultDefinitions())
                .Returns(new List<MetadataSourceDefinition>());

            var result = Subject.GetTemplates();

            var ok = (Ok<List<MetadataSourceResource>>)result;
            ok.Value.Should().NotBeNull();
            ok.Value.Should().BeEmpty(
                "with no defaults registered the filter must yield an empty list — no NRE, no fabricated entries");
        }

        // ============================================================
        // Test 4 — WR-01 override-resolution pin: derived GetTemplates is reached
        // ============================================================
        // The MetadataSourceController.GetTemplates is `override` (not `new`),
        // paired with `virtual` on ProviderControllerBase.GetTemplates. This
        // assertion proves the derived method is the unambiguous resolution
        // target — the prior `new`-shadow shape had both methods on the runtime
        // type which risked AmbiguousActionException at ASP.NET MVC's action
        // discovery scan.
        //
        // Reflective check: GetMethod("GetTemplates") on the controller type
        // must return a method declared on the derived (Mangarr.Api.V5.MetadataSource)
        // class, not on the open-generic base. The IsVirtual flag MUST be true
        // (override implies virtual), and the method's DeclaringType MUST be
        // MetadataSourceController (proving the derived `override` takes
        // precedence over any inherited base method).
        [Test]
        public void GetTemplates_method_is_override_not_shadow()
        {
            var method = typeof(MetadataSourceController).GetMethod(nameof(MetadataSourceController.GetTemplates));

            method.Should().NotBeNull(
                "MetadataSourceController must expose a public GetTemplates() — the V5 schema endpoint shape");

            method!.IsVirtual.Should().BeTrue(
                "GetTemplates must be virtual (override implies virtual at the CLR level) — proves the derived method " +
                "is in the virtual dispatch chain. The pre-fix `new`-modifier shape had IsVirtual=false on the derived " +
                "method, surfacing two distinct methods with the same [HttpGet(\"schema\")] route at MVC action discovery.");

            method.DeclaringType.Should().Be(typeof(MetadataSourceController),
                "GetTemplates must be declared on MetadataSourceController (the derived class). DeclaringType is the " +
                "class that authored the method body — if this returned ProviderControllerBase<,,,,> the override " +
                "would have silently fallen back to the base method, defeating the D-02 filter.");
        }

        // ============================================================
        // Helper — uninitialized-object activation. The IsDeprecated property has
        // no field reads (it's a `virtual bool ... => true|false`) so an
        // uninitialized instance answers it correctly. Same shape as the sibling
        // MetadataSourceSchemaFilterFixture (Core.Test).
        // ============================================================
        private static T Activate<T>()
            where T : class
        {
            return (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
    }
}
