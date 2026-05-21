using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mangarr.Api.V5.ImportLists;
using Mangarr.Http.REST;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.ImportLists
{
    // GH-225 (Phase 27.1 REVIEW WR-02) — regression coverage for the
    // SharedValidator Title.NotEmpty() guard in ImportListExclusionController.
    // The frontend leg (`ImportListExclusions.tsx` concurrent-removal guard)
    // prevents a blank-titled POST from ever being emitted, but the backend
    // validator is the defense-in-depth: a malformed PUT/POST from any source
    // (cURL, Bruno, future FE refactor) must still surface a 400 instead of
    // silently persisting an empty-Title exclusion that the UI would then
    // render as an empty row.
    //
    // Role-match analog: src/NzbDrone.Api.Test/ImportLists/ImportListControllerValidatorFixture.cs
    // (Phase 27.1 Plan 27.1-01 Task 6 STRIDE Tamper coverage) — same shape:
    // TestBase<TController> + AutoMoq + reflective SharedValidator access.
    //
    // Per-plan unit-test filter:
    //   bash scripts/test.sh Windows Unit Test --filter=ImportListExclusionControllerValidatorFixture
    //
    // Tests:
    //   1. Validator_rejects_empty_Title_on_POST_body — empty-Title resource
    //      must fail validation with the failure surfacing on the Title property.
    //   2. Validator_rejects_whitespace_Title — `.NotEmpty()` rejects whitespace
    //      as well (FluentValidation NotEmpty also rejects whitespace strings).
    //   3. Validator_rejects_null_Title — null Title also fails (same rule).
    //   4. Validator_accepts_non_empty_Title — positive control. With a
    //      well-formed Title and the uniqueness probe mocked to pass, the
    //      validator returns IsValid==true.
    [TestFixture]
    public class ImportListExclusionControllerValidatorFixture : TestBase<ImportListExclusionController>
    {
        [SetUp]
        public void Setup()
        {
            // The companion ImportListExclusionExistsValidator probes
            // IImportListExclusionService.All() for MangaDexId uniqueness. Mock
            // it empty so the uniqueness rule trivially passes — these tests
            // isolate the Title.NotEmpty() rule.
            Mocker.GetMock<IImportListExclusionService>()
                .Setup(s => s.All())
                .Returns(new List<ImportListExclusion>());
        }

        // Test 1 — GH-225 WR-02 mitigation. Empty-string Title must fail.
        [Test]
        public void Validator_rejects_empty_Title_on_POST_body()
        {
            var resource = new ImportListExclusionResource
            {
                Id = 0,
                MangaDexId = "33333333-3333-3333-3333-333333333333",
                MalId = 100,
                AniListId = 200,
                Title = string.Empty,
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "SharedValidator.RuleFor(Title).NotEmpty() must reject an empty-string Title (GH-225 WR-02 mitigation)");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListExclusionResource.Title),
                "the failure must surface on the Title property so the FE can highlight the right field");
        }

        // Test 2 — whitespace also fails. FluentValidation NotEmpty rejects
        // whitespace by design.
        [Test]
        public void Validator_rejects_whitespace_Title()
        {
            var resource = new ImportListExclusionResource
            {
                Id = 0,
                MangaDexId = "44444444-4444-4444-4444-444444444444",
                MalId = 100,
                AniListId = 200,
                Title = "   ",
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "NotEmpty() also rejects whitespace-only strings; defense-in-depth for trimmed-empty inputs");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListExclusionResource.Title));
        }

        // Test 3 — null Title also fails.
        [Test]
        public void Validator_rejects_null_Title()
        {
            var resource = new ImportListExclusionResource
            {
                Id = 0,
                MangaDexId = "55555555-5555-5555-5555-555555555555",
                MalId = 100,
                AniListId = 200,
                Title = null,
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse(
                "NotEmpty() rejects null — the same defense for malformed JSON bodies that omit Title");

            result.Errors.Should().Contain(e =>
                e.PropertyName == nameof(ImportListExclusionResource.Title));
        }

        // Test 4 — positive control. With a well-formed Title and the
        // uniqueness probe mocked to pass, validation succeeds.
        [Test]
        public void Validator_accepts_non_empty_Title()
        {
            var resource = new ImportListExclusionResource
            {
                Id = 0,
                MangaDexId = "66666666-6666-6666-6666-666666666666",
                MalId = 100,
                AniListId = 200,
                Title = "Solo Leveling",
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeTrue(
                "with a well-formed Title and the uniqueness mock returning empty list, SharedValidator must pass");
        }

        // Helpers ----------------------------------------------------------------

        private ValidationResult ValidateViaSharedValidator(ImportListExclusionResource resource)
        {
            // SharedValidator is a `protected` property on RestController<TResource>.
            // Same reflective seam used by ImportListControllerValidatorFixture
            // (sibling fixture from Phase 27.1 Plan 27.1-01 Task 6).
            var prop = typeof(RestController<ImportListExclusionResource>)
                .GetProperty("SharedValidator", BindingFlags.NonPublic | BindingFlags.Instance);

            prop.Should().NotBeNull(
                "RestController<T>.SharedValidator must exist as a non-public instance property");

            var validator = (IValidator<ImportListExclusionResource>)prop!.GetValue(Subject);
            validator.Should().NotBeNull();

            return validator!.Validate(resource);
        }
    }
}
