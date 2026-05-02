using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// Validator for <see cref="MangaDexIndexerSettings"/>. Mirrors the
    /// <c>HttpAggregatorSettingsValidator</c> floor (BaseUrl + non-empty SourceKey)
    /// with no additional MangaDex-specific rules — D-11/D-12 ships with sensible
    /// defaults; ToS compliance for the honest UA is enforced by *omission* of the
    /// UA-override UI affordance (T-CONFIG-DRIFT-01 mitigation by absence).
    /// </summary>
    public class MangaDexIndexerSettingsValidator : AbstractValidator<MangaDexIndexerSettings>
    {
        public MangaDexIndexerSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for the v1 BEDROCK <c>MangaDexIndexer</c>. Mirrors Phase 2
    /// <c>MangaDexMetadataSourceSettings</c> shape so <c>SourceKey="mangadex"</c> is shared
    /// between metadata source and indexer (single rate budget per Phase 1 D-11/D-12 +
    /// Phase 2 D-22). Implements <see cref="IHttpAggregatorSettings"/> — required by
    /// <see cref="HttpAggregatorBase{TSettings}"/> generic constraint.
    ///
    /// PER CONTEXT MangaDex constraints + Phase 1 D-13/D-14:
    /// <see cref="UserAgentOverride"/> is REQUIRED on the <see cref="IHttpAggregatorSettings"/>
    /// interface, but MangaDex ToS REQUIRES the honest <c>"Mangarr/{version}"</c> UA. We
    /// implement the property (interface contract) but DO NOT expose it via
    /// <c>[FieldDefinition]</c> — there is no UI affordance to override. T-CONFIG-DRIFT-01
    /// mitigated by absence; verified by reflection fixture
    /// <c>MangaDexIndexerSettingsHonestUaFixture</c> (Wave 0 / Plan 03-01).
    /// </summary>
    public class MangaDexIndexerSettings : IHttpAggregatorSettings
    {
        private static readonly MangaDexIndexerSettingsValidator Validator = new MangaDexIndexerSettingsValidator();

        public MangaDexIndexerSettings()
        {
            BaseUrl = "https://api.mangadex.org";
            SourceKey = "mangadex";

            // RateSeconds default = 1.5 (40 req/min — matches MangaDex /at-home/server hot-path
            // limit; ensures Phase 4 image-fetch sharing the SourceKey bucket cannot burst).
            RateSeconds = 1.5;

            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "URL", HelpText = "MangaDex API base URL — leave default unless mirroring.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Rate-limit Source Key", Advanced = true, HelpText = "Override the per-SourceKey rate budget grouping. Leave 'mangadex' unless splitting budgets.")]
        public string SourceKey { get; set; }

        // PER CONTEXT MangaDex constraints + Phase 1 D-13/D-14:
        // UserAgentOverride is REQUIRED on the IHttpAggregatorSettings interface, but MangaDex
        // ToS REQUIRES the honest "Mangarr/{version}" UA. We implement the property (interface
        // contract) but DO NOT expose it via [FieldDefinition] — there is no UI affordance to override.
        public string UserAgentOverride { get; set; }   // intentionally NO [FieldDefinition]

        [FieldDefinition(3, Label = "Rate (seconds)", Type = FieldType.Number, Advanced = true, HelpText = "Override the default 1.5s gap (40 req/min). Lower values risk MangaDex 429s and ToS violation.")]
        public double? RateSeconds { get; set; }

        // Inherited IIndexerSettings floor — not surfaced in UI for the MangaDex aggregator
        // (multi-language / fail-download policies aren't meaningful for HTTP-manga sources)
        // but required by the IIndexerSettings interface contract.
        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        // D-12 bridge: Rate is exposed via the IHttpAggregatorSettings interface as TimeSpan?
        // but persisted on disk as a numeric RateSeconds value (so the FieldDefinition produces
        // a Number control rather than a TimeSpan-shaped one). Mirrors HttpAggregatorSettings +
        // Phase 2 MangaDexMetadataSourceSettings.
        TimeSpan? IHttpAggregatorSettings.Rate
        {
            get => RateSeconds.HasValue ? TimeSpan.FromSeconds(RateSeconds.Value) : (TimeSpan?)null;
            set => RateSeconds = value?.TotalSeconds;
        }

        public NzbDroneValidationResult Validate()
            => new NzbDroneValidationResult(Validator.Validate(this));
    }
}
