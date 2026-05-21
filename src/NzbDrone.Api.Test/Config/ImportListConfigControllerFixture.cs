using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mangarr.Api.V5.Config;
using Mangarr.Http;
using Mangarr.Http.REST;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Config
{
    // Phase 27.1 Plan 27.1-01 — NUnit fixture pinning the V5 ImportListConfigController
    // contract (closes GH #220 backend half).
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/MangaFolderControllerFixture.cs
    // (Plan 13-05 reflective Attribute-lookup pattern + TestBase<TController> + AutoMoq).
    // This is the FIRST fixture under NzbDrone.Api.Test/Config/ — the Config subdirectory
    // was not present before this plan.
    //
    // Per-plan unit-test filter:
    //   bash scripts/test.sh Windows Unit Test --filter "FullyQualifiedName~ImportListConfigController"
    //
    // Tests:
    //   1. Route_attribute_is_config_importlist — pin [V5ApiController("config/importlist")]
    //      literal so a future refactor cannot silently drift the route (the Redux store
    //      importListOptions.js:53 hard-codes "/config/importlist" — drift would 404).
    //   2. Controller_extends_RestController_ImportListConfigResource — pin base class to
    //      RestController<ImportListConfigResource> per RESEARCH Pitfall 2: Mangarr has no
    //      ConfigController<T> abstraction; CONTEXT.md D-06's reference is incorrect.
    //   3. Get_returns_defaults_on_fresh_db — mocked IConfigService returns Disabled + 0;
    //      controller GET returns Ok<ImportListConfigResource> with id=1.
    //   4. Put_rejects_KeepAndTag_without_tag — exercise the Sonarr v3 parity .When rule:
    //      submitting { listSyncLevel=KeepAndTag, listSyncTag=0 } via the protected
    //      SharedValidator (reflection-accessed) returns ValidationFailure on ListSyncTag.
    //   5. Put_accepts_KeepAndTag_with_tag — same path with listSyncTag=5 passes; the
    //      SaveConfig action calls _configService.SaveConfigDictionary with the expected
    //      dictionary entries.
    [TestFixture]
    public class ImportListConfigControllerFixture : TestBase<ImportListConfigController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.ListSyncLevel)
                .Returns(ListSyncLevelType.Disabled);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.ListSyncTag)
                .Returns(0);
        }

        [Test]
        public void Route_attribute_is_config_importlist()
        {
            // Load-bearing contract: Redux store importListOptions.js:53 calls /config/importlist.
            // Route drift here silently 404s the Settings → ImportLists Options form.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(ImportListConfigController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("config/importlist");
        }

        [Test]
        public void Controller_extends_RestController_ImportListConfigResource()
        {
            // RESEARCH Pitfall 2 amendment to CONTEXT.md D-06: Mangarr has no ConfigController<T>
            // abstraction. The correct base is RestController<ImportListConfigResource> mirroring
            // DownloadClientConfigController.cs. Pinning the base-class shape guards against a
            // future planner re-introducing a ConfigController<T> base they think exists.
            var baseType = typeof(ImportListConfigController).BaseType;

            baseType.Should().NotBeNull();
            baseType!.IsGenericType.Should().BeTrue();
            baseType.GetGenericTypeDefinition().Should().Be(typeof(RestController<>));
            baseType.GetGenericArguments().Should().ContainSingle()
                .Which.Should().Be(typeof(ImportListConfigResource));
        }

        [Test]
        public void Get_returns_defaults_on_fresh_db()
        {
            // Mocked IConfigService.ListSyncLevel == Disabled + ListSyncTag == 0 per [SetUp].
            // GET /api/v5/config/importlist should return Ok<ImportListConfigResource> with
            // id=1 (Mangarr KV config convention — all config rows project to id=1).
            var result = Subject.GetImportListConfig();

            result.Should().NotBeNull();

            var resource = result.Value;
            resource.Should().NotBeNull();
            resource!.Id.Should().Be(1);
            resource.ListSyncLevel.Should().Be(ListSyncLevelType.Disabled);
            resource.ListSyncTag.Should().Be(0);
        }

        [Test]
        public void Put_rejects_KeepAndTag_without_tag()
        {
            // Sonarr v3 parity validator (ImportListConfigController:18-21):
            // .RuleFor(x => x.ListSyncTag).ValidId().When(x => x.ListSyncLevel == KeepAndTag)
            // — saving listSyncLevel=KeepAndTag with listSyncTag=0 must fail with
            // "Tag must be specified". Without this guard, ImportListSyncService.ProcessListItems
            // would attempt to tag manga with id=0 (silent corruption — RESEARCH §Security
            // Domain "KeepAndTag with tag-id = 0 silent corruption" mitigation).
            var resource = new ImportListConfigResource
            {
                Id = 1,
                ListSyncLevel = ListSyncLevelType.KeepAndTag,
                ListSyncTag = 0
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "SharedValidator must reject KeepAndTag without a tag id per Sonarr v3 parity");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListConfigResource.ListSyncTag) &&
                e.ErrorMessage == "Tag must be specified");
        }

        [Test]
        public void Put_accepts_KeepAndTag_with_tag()
        {
            // Positive control + SaveConfigDictionary delegation contract: a valid resource
            // (listSyncLevel=KeepAndTag, listSyncTag=5) passes the SharedValidator AND the
            // SaveConfig action delegates to IConfigService.SaveConfigDictionary with the
            // reflection-built dictionary containing both fields.
            var resource = new ImportListConfigResource
            {
                Id = 1,
                ListSyncLevel = ListSyncLevelType.KeepAndTag,
                ListSyncTag = 5
            };

            // Validator passes
            var validation = ValidateViaSharedValidator(resource);
            validation.IsValid.Should().BeTrue(
                "valid KeepAndTag + non-zero tag should pass SharedValidator");

            // SaveConfig action delegates to SaveConfigDictionary with both fields populated.
            // Note: at the unit-test layer Url.Action(...) is null (no HTTP context wired by
            // AutoMoq), so the trailing TypedAccepted call throws ArgumentNullException AFTER
            // SaveConfigDictionary already ran. We catch the post-delegation throw and assert
            // the delegation contract — which is the load-bearing behavior under test.
            try
            {
                Subject.SaveConfig(resource);
            }
            catch (ArgumentNullException)
            {
                // expected: Url.Action(null helper) — Mocker doesn't wire an UrlHelper. The
                // SaveConfigDictionary call has already fired by this point in the action.
            }

            Mocker.GetMock<IConfigService>()
                .Verify(
                    s => s.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d =>
                        d.ContainsKey(nameof(ImportListConfigResource.ListSyncLevel)) &&
                        d.ContainsKey(nameof(ImportListConfigResource.ListSyncTag)) &&
                        (ListSyncLevelType)d[nameof(ImportListConfigResource.ListSyncLevel)] == ListSyncLevelType.KeepAndTag &&
                        (int)d[nameof(ImportListConfigResource.ListSyncTag)] == 5)),
                    Times.Once,
                    "SaveConfig must call SaveConfigDictionary with a reflection-built dictionary " +
                    "containing both ListSyncLevel and ListSyncTag values");
        }

        // Helpers ----------------------------------------------------------------

        private ValidationResult ValidateViaSharedValidator(ImportListConfigResource resource)
        {
            // SharedValidator is a `protected` property on RestController<TResource>. The fixture
            // is not a derived class, so we lift it via reflection. This is the canonical seam
            // for FluentValidation rule tests against Mangarr controllers (see also Sonarr's
            // v3 ImportListConfigController test pattern).
            var prop = typeof(RestController<ImportListConfigResource>)
                .GetProperty("SharedValidator", BindingFlags.NonPublic | BindingFlags.Instance);

            prop.Should().NotBeNull(
                "RestController<T>.SharedValidator must exist as a non-public instance property");

            var validator = (IValidator<ImportListConfigResource>)prop!.GetValue(Subject);
            validator.Should().NotBeNull();

            return validator!.Validate(resource);
        }
    }
}
