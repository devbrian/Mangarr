using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Gateway
{
    /// <summary>
    /// Validator for <see cref="GatewaySettings"/>. Mirrors the canonical indexer-settings
    /// floor: a valid root URL (security V5) plus a non-empty API key (the gateway requires
    /// an <c>X-Api-Key</c> header on every call). No aggregator-specific rules — the gateway
    /// is NOT an <c>IHttpAggregatorSettings</c> (Pitfall #1).
    /// </summary>
    public class GatewaySettingsValidator : AbstractValidator<GatewaySettings>
    {
        public GatewaySettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for the external manga-gateway indexer (Phase 37). Implements the
    /// Sonarr-canonical <see cref="IIndexerSettings"/> directly — NOT
    /// <c>IHttpAggregatorSettings</c> (the near-namesake trap, Pitfall #1). The gateway is a
    /// single external host exposing <c>/caps</c> + <c>/search</c> + <c>/recent</c>; there is
    /// NO per-SourceKey rate budget, NO UA override, and NO <c>IHttpAggregatorSettings.Rate</c>
    /// bridge — those are aggregator-only concepts.
    /// </summary>
    public class GatewaySettings : IIndexerSettings
    {
        private static readonly GatewaySettingsValidator Validator = new GatewaySettingsValidator();

        public GatewaySettings()
        {
            EnabledSources = Array.Empty<string>();
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "GatewayUrl", HelpText = "GatewayUrlHelpText")]
        public string BaseUrl { get; set; }

        // Security V6: PrivacyLevel.ApiKey — masked in the UI + scrubbed from logs. Sent as the
        // X-Api-Key header on every gateway call; never written to _logger.
        [FieldDefinition(1, Label = "GatewayApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "GatewayApiKeyHelpText")]
        public string ApiKey { get; set; }

        // Empty = all sources (D-04). The "gatewaySources" provider action is wired in Plan 04
        // (GatewayIndexer.RequestAction) and force-refetches the cached /caps document.
        [FieldDefinition(2, Label = "GatewaySources", Type = FieldType.Select, SelectOptionsProviderAction = "gatewaySources", HelpText = "GatewaySourcesHelpText")]
        public IEnumerable<string> EnabledSources { get; set; }

        [FieldDefinition(3, Label = "GatewayLanguages", Type = FieldType.Select, SelectOptions = typeof(RealLanguageFieldConverter), HelpText = "GatewayLanguagesHelpText")]
        public IEnumerable<int> MultiLanguages { get; set; }

        // IIndexerSettings floor member; no UI field.
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate()
            => new NzbDroneValidationResult(Validator.Validate(this));
    }
}
