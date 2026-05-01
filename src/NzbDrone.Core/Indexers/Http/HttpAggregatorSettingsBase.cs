using System;
using System.Collections.Generic;
using Equ;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Settings contract every concrete <see cref="HttpAggregatorBase{TSettings}"/> subclass must
    /// satisfy. Extends <see cref="IIndexerSettings"/> with the three Mangarr-aggregator-specific
    /// fields (D-11/D-12/D-14).
    /// </summary>
    public interface IHttpAggregatorSettings : IIndexerSettings
    {
        /// <summary>
        /// D-12: User-overridable. Falls back to
        /// <see cref="HttpAggregatorBase{TSettings}.DefaultSourceKey"/> when blank.
        /// </summary>
        string SourceKey { get; set; }

        /// <summary>
        /// D-12: User-overridable rate limit (TimeSpan). Falls back to base 2-second default when
        /// null. The concrete <see cref="HttpAggregatorSettings"/> POCO computes this from
        /// <see cref="HttpAggregatorSettings.RateSeconds"/>.
        /// </summary>
        TimeSpan? Rate { get; set; }

        /// <summary>
        /// D-14: User-overridable opt-out. Honest <c>Mangarr/{version}</c> applied when blank.
        /// </summary>
        string UserAgentOverride { get; set; }
    }

    /// <summary>
    /// FluentValidation rules for <see cref="HttpAggregatorSettings"/>. Source plugins typically
    /// subclass <see cref="HttpAggregatorSettings"/> and may extend or replace this validator with
    /// source-specific rules; the two rules enforced here (BaseUrl, SourceKey) are the
    /// Mangarr-aggregator floor.
    /// </summary>
    public class HttpAggregatorSettingsValidator : AbstractValidator<HttpAggregatorSettings>
    {
        public HttpAggregatorSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();

            // D-12: SourceKey is required (default supplied by subclass).
            // Rate and UserAgentOverride are optional.
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Reusable Settings POCO. Concrete source plugins MAY extend or reuse directly; Phase 3
    /// plugins typically subclass this for source-specific fields.
    /// </summary>
    public class HttpAggregatorSettings : PropertywiseEquatable<HttpAggregatorSettings>, IHttpAggregatorSettings
    {
        private static readonly HttpAggregatorSettingsValidator Validator = new HttpAggregatorSettingsValidator();

        public HttpAggregatorSettings()
        {
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "URL")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Source Key", HelpText = "Logical source name; rate budget is shared between indexer instances with the same key (e.g. \"mangadex\").")]
        public string SourceKey { get; set; }

        [FieldDefinition(2, Label = "Rate (seconds)", Type = FieldType.Number, Advanced = true, HelpText = "Override the default per-source rate limit (seconds between requests).")]
        public double? RateSeconds { get; set; }

        [FieldDefinition(3, Label = "User-Agent Override", Advanced = true, HelpText = "Spoof a non-Mangarr User-Agent for sources that block honest UAs (e.g. Cloudflare-protected aggregators). Leave blank for honest default 'Mangarr/{version}'.")]
        public string UserAgentOverride { get; set; }

        // D-12: Rate is exposed via the interface as TimeSpan? but persisted on disk as a numeric
        // RateSeconds value (so the FieldDefinition produces a Number control rather than a
        // TimeSpan-shaped one). Explicit interface impl bridges the two: the getter computes from
        // RateSeconds, the setter writes back as seconds.
        TimeSpan? IHttpAggregatorSettings.Rate
        {
            get => RateSeconds.HasValue ? TimeSpan.FromSeconds(RateSeconds.Value) : (TimeSpan?)null;
            set => RateSeconds = value?.TotalSeconds;
        }

        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate()
            => new NzbDroneValidationResult(Validator.Validate(this));
    }
}
