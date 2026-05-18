using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mangarr.Api.V5.AutoTagging;
using Mangarr.Http.ClientSchema;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.AutoTagging
{
    // Phase 24 Plan 24-04 — schema endpoint fixture covering the [HttpGet("schema")]
    // path on AutoTaggingController. Per Open Q #3 resolution (24-PLAN-DECISIONS.md
    // §Open Q #3), the schema endpoint is INLINED on the main controller — no
    // separate AutoTaggingSpecificationController.cs file. This fixture exercises
    // GetTemplates() against the catalog of 11 IAutoTaggingSpecification impls that
    // Plan 24-03 shipped.
    //
    // Spec total constant = 11 per Plan 24-01 §Spec Catalog Plan Map. The 11 catalog
    // entries are pin-asserted in 24-03 via
    // AutoTaggingSpecificationCatalogFixture + AutoTaggingDryIocAutoDiscoveryFixture;
    // this fixture asserts the controller's enumeration order + the schema-row shape
    // for representative concrete specs (Demographic + ContentRating — both use
    // FieldType.Select with enum SelectOptions, the schema-reflection edge case).
    [TestFixture]
    public class AutoTaggingSpecificationSchemaFixture : TestBase<AutoTaggingController>
    {
        private List<IAutoTaggingSpecification> _specifications = null!;

        [SetUp]
        public void Setup()
        {
            // Echo-back localization mock — schema labels come back as the raw
            // localization key string, which is what the FE expects for non-localized
            // dev surface.
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns<string, Dictionary<string, object>>((s, _) => s);

            SchemaBuilder.Initialize(Mocker.Container);

            // Catalog mirror — instantiate the 11 specs the way Plan 24-03 ships
            // them. Stays in the catalog order Plan 24-01 §Spec Catalog Plan Map
            // documented; the controller's GetTemplates ORDERS BY Order (all 11
            // share Order=1, so iteration order equals construction order in
            // practice).
            _specifications = new List<IAutoTaggingSpecification>
            {
                new GenreSpecification(),
                new YearSpecification(),
                new MonitoredSpecification(),
                new StatusSpecification(),
                new RootFolderSpecification(),
                new TranslationProfileSpecification(),
                new CustomFormatProfileSpecification(),
                new TagSpecification(),
                new AuthorArtistSpecification(),
                new DemographicSpecification(),
                new ContentRatingSpecification()
            };

            Mocker.SetConstant<IEnumerable<IAutoTaggingSpecification>>(_specifications);
        }

        [Test]
        public void GetTemplates_returns_eleven_schema_rows()
        {
            // Plan 24-01 §Spec Catalog Plan Map pinned Spec total = 11.
            var ok = Subject.GetTemplates();
            ok.Value.Should().HaveCount(11);
        }

        [Test]
        public void GetTemplates_includes_all_expected_implementations()
        {
            var ok = Subject.GetTemplates();
            var implementations = ok.Value!.Select(s => s.Implementation).ToList();

            implementations.Should().Contain("GenreSpecification");
            implementations.Should().Contain("YearSpecification");
            implementations.Should().Contain("MonitoredSpecification");
            implementations.Should().Contain("StatusSpecification");
            implementations.Should().Contain("RootFolderSpecification");
            implementations.Should().Contain("TranslationProfileSpecification");
            implementations.Should().Contain("CustomFormatProfileSpecification");
            implementations.Should().Contain("TagSpecification");
            implementations.Should().Contain("AuthorArtistSpecification");
            implementations.Should().Contain("DemographicSpecification");
            implementations.Should().Contain("ContentRatingSpecification");
        }

        [Test]
        public void GetTemplates_excludes_dropped_specs()
        {
            // Plan 24-01 Open Q #1 dropped OriginalLanguageSpecification;
            // AT-05 dropped Network/SeriesType/OriginalCountry. The 11-spec
            // catalog must not contain any of these.
            var ok = Subject.GetTemplates();
            var implementations = ok.Value!.Select(s => s.Implementation).ToList();

            implementations.Should().NotContain("OriginalLanguageSpecification");
            implementations.Should().NotContain("NetworkSpecification");
            implementations.Should().NotContain("SeriesTypeSpecification");
            implementations.Should().NotContain("OriginalCountrySpecification");
        }

        [Test]
        public void GetTemplates_each_row_carries_implementation_name_and_fields()
        {
            // Each schema row carries: Implementation (class name), ImplementationName
            // (human-readable label per Plan 24-01 §Spec Catalog), Fields[] populated
            // from SchemaBuilder reflection.
            var ok = Subject.GetTemplates();
            var rows = ok.Value!;

            foreach (var row in rows)
            {
                row.Implementation.Should().NotBeNullOrEmpty($"{row.ImplementationName} must carry Implementation class name");
                row.ImplementationName.Should().NotBeNullOrEmpty($"{row.Implementation} must carry ImplementationName label");
                row.Fields.Should().NotBeNull($"{row.ImplementationName} must carry Fields collection");
            }
        }

        [Test]
        public void GetTemplates_demographic_spec_carries_select_options_from_enum()
        {
            // DemographicSpec is FieldType.Select with SelectOptions = typeof(MangaDemographic).
            // SchemaBuilder reflects the enum into SelectOptions list. Per Plan 24-02:
            // Shonen=1, Shojo=2, Seinen=3, Josei=4 (4 values).
            var ok = Subject.GetTemplates();
            var demographic = ok.Value!.Single(s => s.Implementation == "DemographicSpecification");

            demographic.Fields.Should().NotBeEmpty();
            var valueField = demographic.Fields.Single(f => f.Name == "value");
            valueField.Type.Should().Be("select");
            valueField.SelectOptions.Should().NotBeNull();
            valueField.SelectOptions.Should().HaveCount(4);
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(1) && o.Name == "Shonen");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(2) && o.Name == "Shojo");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(3) && o.Name == "Seinen");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(4) && o.Name == "Josei");
        }

        [Test]
        public void GetTemplates_content_rating_spec_carries_select_options_from_enum()
        {
            // ContentRatingSpec is FieldType.Select with SelectOptions = typeof(MangaContentRating).
            // Per Plan 24-03 D-03: Safe=1, Suggestive=2, Erotica=3, Pornographic=4.
            var ok = Subject.GetTemplates();
            var contentRating = ok.Value!.Single(s => s.Implementation == "ContentRatingSpecification");

            contentRating.Fields.Should().NotBeEmpty();
            var valueField = contentRating.Fields.Single(f => f.Name == "value");
            valueField.Type.Should().Be("select");
            valueField.SelectOptions.Should().NotBeNull();
            valueField.SelectOptions.Should().HaveCount(4);
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(1) && o.Name == "Safe");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(2) && o.Name == "Suggestive");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(3) && o.Name == "Erotica");
            valueField.SelectOptions.Should().Contain(o => o.Value.Equals(4) && o.Name == "Pornographic");
        }
    }
}
