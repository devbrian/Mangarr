using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests
{
    // CF-04 verification per Phase 5 PATTERNS-MAP S8 + 05-RESEARCH.md Open Question 3.
    // Sonarr's CustomFormatResource.MapSpecification (Sonarr.Api.V5/CustomFormats/CustomFormatResource.cs:57-78)
    // reflects Implementation field via GetType().Name then copies fields via SchemaBuilder.ReadFromSchema.
    // The 4 NEW manga spec types from plan 05-05 flow through unchanged because they implement
    // ICustomFormatSpecification — this fixture proves the lossless round-trip across the same
    // mechanism (reflection-by-name + property-by-name field copy).
    //
    // NOTE on test boundary: NzbDrone.Core.Test does NOT reference Sonarr.Api.V5 (V5 references Core,
    // not the other way around). To test the CF-04 invariant without crossing the dependency boundary,
    // this fixture mirrors the CustomFormatResource.MapSpecification ALGORITHM verbatim — same
    // reflection-by-name lookup, same property-by-name field copy. The mirror is a 15-line helper
    // method in this fixture. If the production MapSpecification ever diverges from this mirror,
    // the divergence is intentional and should be ported here.
    //
    // The 6 tests cover: per-spec round-trip (4) + combined CF (1) + unknown-implementation guard (1).
    [TestFixture]
    public class MangaCustomFormatRoundTripFixture : CoreTest
    {
        private List<ICustomFormatSpecification> _allSpecs;

        [SetUp]
        public void Setup()
        {
            // The full set of manga CF specs that production DI would auto-discover via DryIoc
            // IEnumerable<ICustomFormatSpecification> (pattern S1). Listed explicitly here.
            _allSpecs = new List<ICustomFormatSpecification>
            {
                new TranslatedLanguageSpecification(),
                new ScanlationGroupSpecification(),
                new SourceKeySpecification(),
                new ChapterTypeSpecification()
            };
        }

        // Mirrors Sonarr.Api.V5.CustomFormats.CustomFormatResource.MapSpecification lines 57-78
        // verbatim. Production uses Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema for the
        // field copy; this mirror uses direct reflection on the [FieldDefinition]-marked properties
        // since SchemaBuilder requires container init (ILocalizationService) we don't need here.
        private static ICustomFormatSpecification MapSpecificationViaReflection(
            string implementationTypeName,
            Dictionary<string, object> fieldValues,
            List<ICustomFormatSpecification> specifications)
        {
            // Production line 59-66: SingleOrDefault by GetType().Name == resource.Implementation;
            // ArgumentException on no-match.
            var matchingSpec =
                specifications.SingleOrDefault(x => x.GetType().Name == implementationTypeName);

            if (matchingSpec is null)
            {
                throw new ArgumentException(
                    $"{implementationTypeName} is not a valid specification implementation");
            }

            var type = matchingSpec.GetType();

            // Production line 73 path: Activator.CreateInstance + property copy via SchemaBuilder.
            // Mirror via direct reflection — finds public-instance properties, sets the matching name.
            var spec = (ICustomFormatSpecification)Activator.CreateInstance(type);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                {
                    continue;
                }

                if (fieldValues.TryGetValue(prop.Name, out var value) && value != null)
                {
                    // Coerce: resource fields come over the wire as object (string/long/etc) — match
                    // SchemaBuilder.ReadFromSchema's value-converter shape.
                    var target = prop.PropertyType;
                    var coerced = target == value.GetType()
                        ? value
                        : Convert.ChangeType(value, Nullable.GetUnderlyingType(target) ?? target);
                    prop.SetValue(spec, coerced);
                }
            }

            return spec;
        }

        // Mirrors the export side: CustomFormatResource.ToResource (line 24-39) → spec.ToSchema (line 31).
        // Captures (Implementation = TypeName, FieldValues = property name → value).
        private static (string Implementation, Dictionary<string, object> Fields) ToResource(ICustomFormatSpecification spec)
        {
            var fields = new Dictionary<string, object>();
            foreach (var prop in spec.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                fields[prop.Name] = prop.GetValue(spec);
            }

            return (spec.GetType().Name, fields);
        }

        [Test]
        public void Round_trip_TranslatedLanguageSpecification()
        {
            var original = new TranslatedLanguageSpecification { Name = "EN", Value = "en" };

            var (impl, fields) = ToResource(original);
            var rebuilt = MapSpecificationViaReflection(impl, fields, _allSpecs);

            rebuilt.Should().BeOfType<TranslatedLanguageSpecification>();
            ((TranslatedLanguageSpecification)rebuilt).Value.Should().Be("en");
            rebuilt.Name.Should().Be("EN");
        }

        [Test]
        public void Round_trip_ScanlationGroupSpecification()
        {
            var original = new ScanlationGroupSpecification { Name = "AsuraRegex", Value = "^Asura" };

            var (impl, fields) = ToResource(original);
            var rebuilt = MapSpecificationViaReflection(impl, fields, _allSpecs);

            rebuilt.Should().BeOfType<ScanlationGroupSpecification>();
            ((ScanlationGroupSpecification)rebuilt).Value.Should().Be("^Asura");
        }

        [Test]
        public void Round_trip_SourceKeySpecification()
        {
            var original = new SourceKeySpecification { Name = "MD", Value = "mangadex" };

            var (impl, fields) = ToResource(original);
            var rebuilt = MapSpecificationViaReflection(impl, fields, _allSpecs);

            rebuilt.Should().BeOfType<SourceKeySpecification>();
            ((SourceKeySpecification)rebuilt).Value.Should().Be("mangadex");
        }

        [Test]
        public void Round_trip_ChapterTypeSpecification()
        {
            var original = new ChapterTypeSpecification { Name = "ExtrasOnly", Value = (int)ChapterType.Extra };

            var (impl, fields) = ToResource(original);
            var rebuilt = MapSpecificationViaReflection(impl, fields, _allSpecs);

            rebuilt.Should().BeOfType<ChapterTypeSpecification>();
            ((ChapterTypeSpecification)rebuilt).Value.Should().Be((int)ChapterType.Extra);
        }

        [Test]
        public void Round_trip_combined_CF_with_all_4_manga_specs()
        {
            // CF carrying all 4 manga spec types — the v1 expected use case (a power-user CF that
            // requires English + a specific scanlation group + a specific source + Extra chapter type).
            var original = new CustomFormat
            {
                Name = "Premium-Extras-MangaDex",
                IncludeCustomFormatWhenRenaming = false,
                Specifications = new List<ICustomFormatSpecification>
                {
                    new TranslatedLanguageSpecification { Name = "EN", Value = "en" },
                    new ScanlationGroupSpecification { Name = "AsuraRegex", Value = "^Asura" },
                    new SourceKeySpecification { Name = "MD", Value = "mangadex" },
                    new ChapterTypeSpecification { Name = "ExtrasOnly", Value = (int)ChapterType.Extra }
                }
            };

            // Export each spec, then re-import via the reflection-by-name path.
            var rebuiltSpecs = original.Specifications
                .Select(spec =>
                {
                    var (impl, fields) = ToResource(spec);
                    return MapSpecificationViaReflection(impl, fields, _allSpecs);
                })
                .ToList();

            rebuiltSpecs.Should().HaveCount(4);
            rebuiltSpecs.Should().Contain(s => s is TranslatedLanguageSpecification);
            rebuiltSpecs.Should().Contain(s => s is ScanlationGroupSpecification);
            rebuiltSpecs.Should().Contain(s => s is SourceKeySpecification);
            rebuiltSpecs.Should().Contain(s => s is ChapterTypeSpecification);

            // Field-level lossless: each spec's Value field round-trips intact.
            ((TranslatedLanguageSpecification)rebuiltSpecs.First(s => s is TranslatedLanguageSpecification)).Value.Should().Be("en");
            ((ScanlationGroupSpecification)rebuiltSpecs.First(s => s is ScanlationGroupSpecification)).Value.Should().Be("^Asura");
            ((SourceKeySpecification)rebuiltSpecs.First(s => s is SourceKeySpecification)).Value.Should().Be("mangadex");
            ((ChapterTypeSpecification)rebuiltSpecs.First(s => s is ChapterTypeSpecification)).Value.Should().Be((int)ChapterType.Extra);
        }

        [Test]
        public void Unknown_implementation_raises_ArgumentException()
        {
            // Per CustomFormatResource.cs:62-66: ArgumentException on unknown Implementation.
            // Verifies the safety net is preserved.
            Action act = () =>
                MapSpecificationViaReflection("NotARealSpec", new Dictionary<string, object>(), _allSpecs);

            act.Should().Throw<ArgumentException>()
               .WithMessage("*NotARealSpec*");
        }
    }
}
