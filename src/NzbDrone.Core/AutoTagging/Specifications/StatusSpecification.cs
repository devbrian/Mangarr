using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port with Pitfall 1 reshape (Option A).
    // Sonarr's canonical body compared series.Status against a SeriesStatus enum
    // cast — that cannot compile against Manga.Status (string vs enum). Option A
    // (chosen) introduces a peer MangaStatus enum sentinel mirror and maps the
    // user's int Value -> MangaStatus enum -> ToString() to compare against the
    // stored Manga.Status string (case-insensitive). The Sonarr SeriesStatus cast
    // pattern is FORBIDDEN here (anti-cast grep gate W-1 — only MangaStatus cast
    // is permitted).
    public class StatusSpecificationValidator : AbstractValidator<StatusSpecification>
    {
    }

    public class StatusSpecification : AutoTaggingSpecificationBase
    {
        private static readonly StatusSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Status";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationStatus", Type = FieldType.Select, SelectOptions = typeof(MangaStatus))]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            if (string.IsNullOrWhiteSpace(manga.Status))
            {
                return false;
            }

            // Pitfall 1 reshape: map int Value -> MangaStatus enum -> name -> lowercased
            // string -> compare against Manga.Status (also lowercased). MangaStatusType
            // string constants are all lowercase ("ongoing"/"completed"/...), so
            // enum.ToString().ToLowerInvariant() matches by construction.
            var expected = ((MangaStatus)Value).ToString();
            return expected.Equals(manga.Status, StringComparison.OrdinalIgnoreCase);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
