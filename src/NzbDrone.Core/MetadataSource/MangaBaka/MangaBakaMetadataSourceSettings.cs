using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.MangaBaka
{
    /// <summary>
    /// Validator for <see cref="MangaBakaMetadataSourceSettings"/>. Mirrors
    /// <c>MangaDexMetadataSourceSettingsValidator</c>'s floor (BaseUrl + non-empty
    /// SourceKey) with no additional MangaBaka-specific rules.
    /// </summary>
    public class MangaBakaMetadataSourceSettingsValidator : AbstractValidator<MangaBakaMetadataSourceSettings>
    {
        public MangaBakaMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for the MangaBaka metadata source. Implements
    /// <see cref="IHttpAggregatorSettings"/> — required by <c>HttpMetadataSourceBase{TSettings}</c>
    /// generic constraint (carries SourceKey + Rate + UserAgentOverride + the
    /// <c>IIndexerSettings</c> floor of BaseUrl + MultiLanguages + FailDownloads).
    ///
    /// Near-verbatim mirror of <c>MangaDexMetadataSourceSettings</c>. Per D-10, UA override
    /// is required by the interface contract but DELIBERATELY NOT exposed via
    /// [FieldDefinition]: this realizes threat T-CONFIG-DRIFT-01 mitigation via UI absence —
    /// there is no surface for the user to spoof the honest "Mangarr/{version}" UA.
    /// MangaBaka uses its OWN SourceKey/RateLimitKey = "mangabaka", distinct from
    /// MangaDex's "mangadex" budget (D-10).
    /// </summary>
    public class MangaBakaMetadataSourceSettings : IHttpAggregatorSettings
    {
        private static readonly MangaBakaMetadataSourceSettingsValidator Validator = new MangaBakaMetadataSourceSettingsValidator();

        public MangaBakaMetadataSourceSettings()
        {
            BaseUrl = "https://api.mangabaka.org";
            SourceKey = "mangabaka";
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "MangaBakaMetadataSourceUrl", HelpText = "MangaBakaMetadataSourceUrlHelpText")]
        public string BaseUrl { get; set; }

        // SourceKey is internal infrastructure; expose for advanced override only via hidden field.
        [FieldDefinition(1, Label = "MangaBakaMetadataSourceSourceKey", Advanced = true, HelpText = "MangaBakaMetadataSourceSourceKeyHelpText")]
        public string SourceKey { get; set; }

        // PER D-10 (UA-by-absence): UserAgentOverride is REQUIRED on the
        // IHttpAggregatorSettings interface, but MangaBaka requests must carry the honest
        // "Mangarr/{version}" UA. We implement the property (interface contract) but DO NOT
        // expose it via [FieldDefinition] — there is no UI affordance to override.
        // Threat T-CONFIG-DRIFT-01 is mitigated by absence.
        public string UserAgentOverride { get; set; }   // intentionally NO [FieldDefinition]

        [FieldDefinition(3, Label = "MangaBakaMetadataSourceRateOverride", Type = FieldType.Number, Advanced = true, HelpText = "MangaBakaMetadataSourceRateOverrideHelpText")]
        public double? RateSeconds { get; set; }

        // Bridge: TimeSpan? on the interface, persisted as numeric RateSeconds on disk
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
