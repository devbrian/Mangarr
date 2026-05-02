using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// Validator for <see cref="ComixIndexerSettings"/>. Mirrors the
    /// <c>HttpAggregatorSettingsValidator</c> floor (BaseUrl + non-empty SourceKey)
    /// with no additional comix-specific rules — D-11/D-12 ships with sensible defaults.
    /// </summary>
    public class ComixIndexerSettingsValidator : AbstractValidator<ComixIndexerSettings>
    {
        public ComixIndexerSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for the v1 reference port #1 <c>ComixIndexer</c> — port from
    /// <c>keiyoushi/extensions-source/src/en/comix/Comix.kt</c> (Apache-2.0; PR #11658
    /// merged 2025-11-16). Implements <see cref="IHttpAggregatorSettings"/> — required by
    /// <see cref="HttpAggregatorBase{TSettings}"/> generic constraint.
    ///
    /// <para>
    /// Default <c>SourceKey="comix.to"</c> per CONTEXT Discretion (verbatim with the dot).
    /// If FluentValidation or Settings-UI rendering trips on the dot at runtime, the executor
    /// may switch to <c>"comixto"</c> and document the divergence in <c>SOURCE-PROBE-comix.md</c>
    /// (Plan 03-06).
    /// </para>
    ///
    /// <para>
    /// UNLIKE MangaDex (Plan 03-04), <see cref="UserAgentOverride"/> IS exposed via
    /// <see cref="FieldDefinitionAttribute"/> — comix.to is Cloudflare-protected and users
    /// may need to spoof a browser UA to dodge low-grade UA blocks per Phase 1 D-14 /
    /// Phase 3 D-11. Honest <c>Mangarr/{version}</c> still applies by default.
    /// </para>
    /// </summary>
    public class ComixIndexerSettings : IHttpAggregatorSettings
    {
        private static readonly ComixIndexerSettingsValidator Validator = new ComixIndexerSettingsValidator();

        public ComixIndexerSettings()
        {
            BaseUrl = "https://comix.to";
            SourceKey = "comix.to";

            // RateSeconds default = 0.2 (200ms gap = 5 req/s — matches keiyoushi rateLimit(5)
            // verbatim per RESEARCH.md Per-Port Rate summary). Single budget shared with
            // Phase 4 image-fetch via SourceKey="comix.to".
            RateSeconds = 0.2;

            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "URL", HelpText = "comix.to base URL — leave default unless mirroring.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Rate-limit Source Key", Advanced = true,
                         HelpText = "Override the per-SourceKey rate budget grouping. Leave 'comix.to' unless splitting budgets.")]
        public string SourceKey { get; set; }

        // UNLIKE MangaDex — UserAgentOverride IS exposed via [FieldDefinition] for the comix.to
        // Cloudflare workaround per RESEARCH.md anti-bot section. Honest UA still applied by default.
        [FieldDefinition(2, Label = "User-Agent Override", Advanced = true,
                         HelpText = "Spoof a browser User-Agent to dodge low-grade UA blocks (comix.to is Cloudflare-protected). Leave blank for honest 'Mangarr/{version}' default.")]
        public string UserAgentOverride { get; set; }

        [FieldDefinition(3, Label = "Rate (seconds)", Type = FieldType.Number, Advanced = true,
                         HelpText = "Override the default 200ms gap (5 req/s — keiyoushi rateLimit(5)).")]
        public double? RateSeconds { get; set; }

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
