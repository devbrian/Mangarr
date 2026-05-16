using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.MangaDex
{
    /// <summary>
    /// Validator for <see cref="MangaDexMetadataSourceSettings"/>. Mirrors
    /// <c>HttpAggregatorSettingsValidator</c>'s floor (BaseUrl + non-empty SourceKey)
    /// with no additional MangaDex-specific rules — D-16 ships with sensible defaults
    /// and ToS compliance is enforced by *omission* of UA override UI affordance.
    /// </summary>
    public class MangaDexMetadataSourceSettingsValidator : AbstractValidator<MangaDexMetadataSourceSettings>
    {
        public MangaDexMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for <see cref="MangaDexMetadataSource"/>. Implements
    /// <see cref="IHttpAggregatorSettings"/> — required by <see cref="HttpMetadataSourceBase{TSettings}"/>
    /// generic constraint (carries SourceKey + Rate + UserAgentOverride + the
    /// <c>IIndexerSettings</c> floor of BaseUrl + MultiLanguages + FailDownloads).
    ///
    /// PER CONTEXT MangaDex constraints + Phase 1 D-13/D-14 — UA override is required by
    /// the interface contract but DELIBERATELY NOT exposed via [FieldDefinition]. This
    /// realizes threat T-CONFIG-DRIFT-01 mitigation via UI absence: there is no surface
    /// for the user to spoof the honest "Mangarr/{version}" UA that MangaDex ToS requires.
    /// </summary>
    public class MangaDexMetadataSourceSettings : IHttpAggregatorSettings
    {
        private static readonly MangaDexMetadataSourceSettingsValidator Validator = new MangaDexMetadataSourceSettingsValidator();

        public MangaDexMetadataSourceSettings()
        {
            BaseUrl = "https://api.mangadex.org";
            SourceKey = "mangadex";
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "MangaDexMetadataSourceUrl", HelpText = "MangaDexMetadataSourceUrlHelpText")]
        public string BaseUrl { get; set; }

        // SourceKey is internal infrastructure; expose for advanced override only via hidden field.
        [FieldDefinition(1, Label = "MangaDexMetadataSourceSourceKey", Advanced = true, HelpText = "MangaDexMetadataSourceSourceKeyHelpText")]
        public string SourceKey { get; set; }

        // PER CONTEXT MangaDex constraints + Phase 1 D-13/D-14:
        // UserAgentOverride is REQUIRED on the IHttpAggregatorSettings interface, but MangaDex
        // ToS REQUIRES the honest "Mangarr/{version}" UA. We implement the property (interface
        // contract) but DO NOT expose it via [FieldDefinition] — there is no UI affordance to
        // override. Threat T-CONFIG-DRIFT-01 is mitigated by absence.
        public string UserAgentOverride { get; set; }   // intentionally NO [FieldDefinition]

        [FieldDefinition(3, Label = "MangaDexMetadataSourceRateOverride", Type = FieldType.Number, Advanced = true, HelpText = "MangaDexMetadataSourceRateOverrideHelpText")]
        public double? RateSeconds { get; set; }

        // D-12 bridge: TimeSpan? on the interface, persisted as numeric RateSeconds on disk
        // (so the FieldDefinition produces a Number control). Mirrors HttpAggregatorSettings.
        TimeSpan? IHttpAggregatorSettings.Rate
        {
            get => RateSeconds.HasValue ? TimeSpan.FromSeconds(RateSeconds.Value) : (TimeSpan?)null;
            set => RateSeconds = value?.TotalSeconds;
        }

        // Inherited IIndexerSettings floor — not surfaced in UI for metadata sources but
        // required by the generic constraint on HttpMetadataSourceBase<TSettings>.
        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate()
            => new NzbDroneValidationResult(Validator.Validate(this));
    }
}
