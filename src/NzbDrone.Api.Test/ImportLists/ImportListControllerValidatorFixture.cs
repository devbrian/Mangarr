using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mangarr.Api.V5.ImportLists;
using Mangarr.Http.REST;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.RootFolders;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.ImportLists
{
    // Phase 27.1 Plan 27.1-01 Task 6 — STRIDE Tamper coverage for T-27.1-01-03 +
    // T-27.1-01-04 (bulk-edit can mutate RootFolderPath + TranslationProfileId via
    // the inherited PUT /api/v5/importlist/bulk endpoint). Plan 27.1-01 Task 4
    // wired the SharedValidator rules; this fixture asserts the negative paths so
    // the mitigations carry automated regression coverage.
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/MangaEditorControllerFixture.cs
    // (Phase 13 Plan 13-13 CR-03 closure pattern — same shape: TestBase<TController>
    // + AutoMoq + RootFolder/TranslationProfile service mocks + reflective access
    // to the protected SharedValidator).
    //
    // Per-plan unit-test filter:
    //   bash scripts/test.sh Windows Unit Test --filter=ImportListControllerValidatorFixture
    //
    // Tests:
    //   1. Validator_rejects_non_existent_root_folder_path — RootFolderExistsValidator
    //      fails on a path not in IRootFolderService.All() (T-27.1-01-03).
    //   2. Validator_rejects_non_existent_translation_profile_id — TranslationProfile
    //      ExistsValidator fails on a non-existent id (T-27.1-01-04).
    //   3. Validator_accepts_when_both_validators_pass — positive control: when both
    //      mocks return existence, validation passes.
    [TestFixture]
    public class ImportListControllerValidatorFixture : TestBase<ImportListController>
    {
        [SetUp]
        public void Setup()
        {
            // ProviderControllerBase ctor:45 carries a uniqueness rule
            // .Must((v, c) => !_providerFactory.All().Any(p => p.Name.EqualsIgnoreCase(c) && p.Id != v.Id))
            // which fires on every Validate() call. AutoMoq returns null for List<T> by
            // default — the .Any() then throws ArgumentNullException. Mock the factory to
            // return an empty list so the uniqueness rule is trivially satisfied and the
            // RootFolder + TranslationProfile rules (the rules under test) are exercised
            // in isolation.
            Mocker.GetMock<IImportListFactory>()
                .Setup(f => f.All())
                .Returns(new List<ImportListDefinition>());
        }

        // Test 1 — T-27.1-01-03 STRIDE Tamper mitigation regression coverage.
        [Test]
        public void Validator_rejects_non_existent_root_folder_path()
        {
            // Mock the registered roots — only "/manga" is valid; the resource will carry a
            // different path, so RootFolderExistsValidator must produce a ValidationFailure.
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new() { Id = 1, Path = ValidRootPath() },
                });

            // TranslationProfileId=0 → TranslationProfileExistsValidator short-circuits to
            // IsValid==true per src/NzbDrone.Core/Validation/TranslationProfileExistsValidator.cs:19.
            // We only want the RootFolder rule to fire here.
            var resource = new ImportListResource
            {
                Name = "Some Import List",
                Implementation = "Stub",
                ConfigContract = "StubSettings",
                RootFolderPath = UnregisteredRootPath(),
                TranslationProfileId = 0,
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "RootFolderExistsValidator must reject a path that no IRootFolderService.All() row matches");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListResource.RootFolderPath),
                "the failure must surface on the RootFolderPath property (T-27.1-01-03 mitigation)");
        }

        // Test 2 — T-27.1-01-04 STRIDE Tamper mitigation regression coverage.
        [Test]
        public void Validator_rejects_non_existent_translation_profile_id()
        {
            // Mock the registered roots so the RootFolderPath rule does NOT fire — we are
            // isolating the TranslationProfile validator here.
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new() { Id = 1, Path = ValidRootPath() },
                });

            // Profile id 999 does NOT exist → ITranslationProfileService.Exists(999) returns
            // false → TranslationProfileExistsValidator emits a ValidationFailure.
            Mocker.GetMock<ITranslationProfileService>()
                .Setup(s => s.Exists(999))
                .Returns(false);

            var resource = new ImportListResource
            {
                Name = "Some Import List",
                Implementation = "Stub",
                ConfigContract = "StubSettings",
                RootFolderPath = ValidRootPath(),
                TranslationProfileId = 999,
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "TranslationProfileExistsValidator must reject an id ITranslationProfileService.Exists returns false for");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListResource.TranslationProfileId),
                "the failure must surface on the TranslationProfileId property (T-27.1-01-04 mitigation)");
        }

        // Test 3 — positive control. Rules out false-positives by exercising the happy path.
        [Test]
        public void Validator_accepts_when_both_validators_pass()
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new() { Id = 1, Path = ValidRootPath() },
                });

            Mocker.GetMock<ITranslationProfileService>()
                .Setup(s => s.Exists(5))
                .Returns(true);

            var resource = new ImportListResource
            {
                Name = "Some Import List",
                Implementation = "Stub",
                ConfigContract = "StubSettings",
                RootFolderPath = ValidRootPath(),
                TranslationProfileId = 5,
            };

            var result = ValidateViaSharedValidator(resource);

            // The provider-base rules (Name not empty / unique, Implementation + ConfigContract
            // not empty) all pass here too — the Mocker-resolved IProviderFactory<,>.All()
            // default-returns an empty list so the uniqueness check is trivially satisfied.
            result.IsValid.Should().BeTrue(
                "with both validators mocked to pass, SharedValidator.Validate should return IsValid==true");
        }

        // Helpers ----------------------------------------------------------------

        // OS-aware root path so the PathValidator (IsPathValid with PathValidationType.CurrentOs)
        // passes regardless of where the test suite runs. Windows accepts drive-letter prefixes;
        // POSIX accepts a leading slash.
        private static string ValidRootPath() =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows)
                ? "C:\\Manga"
                : "/manga";

        // Phase 27.1 27.1-REVIEW post-ship CodeRabbit P3 fix-forward (2026-05-21):
        // RootFolderExistsValidator runs UNDER IsValidPath (CascadeMode.Stop on
        // PathValidator), so the unregistered-path probe must return a string
        // that passes IsValidPath() but does NOT match any IRootFolderService.All()
        // entry. On Windows IsValidPath() rejects POSIX-style paths (no drive
        // letter), so a hardcoded "/not/a/registered/root" passes format
        // validation on POSIX but fails it on Windows — making the test exercise
        // path-format rejection instead of root-folder-existence rejection.
        private static string UnregisteredRootPath() =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows)
                ? "C:\\not\\registered"
                : "/not/a/registered/root";

        private ValidationResult ValidateViaSharedValidator(ImportListResource resource)
        {
            // SharedValidator is a `protected` property on RestController<TResource>. The
            // fixture is not a derived class, so we lift it via reflection. Same seam used by
            // ImportListConfigControllerFixture (sibling fixture from Plan 27.1-01 Task 5).
            var prop = typeof(RestController<ImportListResource>)
                .GetProperty("SharedValidator", BindingFlags.NonPublic | BindingFlags.Instance);

            prop.Should().NotBeNull(
                "RestController<T>.SharedValidator must exist as a non-public instance property");

            var validator = (IValidator<ImportListResource>)prop!.GetValue(Subject);
            validator.Should().NotBeNull();

            return validator!.Validate(resource);
        }
    }
}
