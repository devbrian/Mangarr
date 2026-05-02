using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.MyAnimeList
{
    public class MyAnimeListMetadataSourceSettingsValidator : AbstractValidator<MyAnimeListMetadataSourceSettings>
    {
        public MyAnimeListMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ClientId).NotEmpty();
        }
    }

    /// <summary>
    /// MAL v2 metadata source settings (D-24 — client-ID-only auth, NOT OAuth). The user
    /// pastes a free Client ID from https://myanimelist.net/apiconfig; the provider injects
    /// it as the X-MAL-CLIENT-ID header on every outbound request.
    /// </summary>
    public class MyAnimeListMetadataSourceSettings : IHttpAggregatorSettings
    {
        private static readonly MyAnimeListMetadataSourceSettingsValidator Validator = new();

        public MyAnimeListMetadataSourceSettings()
        {
            BaseUrl = "https://api.myanimelist.net/v2";
            SourceKey = "myanimelist";
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "URL", HelpText = "MAL API v2 base URL — leave default.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(
            1,
            Label = "MAL API Client ID",
            HelpText = "Get a free client ID at https://myanimelist.net/apiconfig — create an app, copy the Client ID here. Read-only access; no OAuth required.",
            Privacy = PrivacyLevel.ApiKey)]
        public string ClientId { get; set; }

        [FieldDefinition(2, Label = "Rate-limit Source Key", Advanced = true, HelpText = "Override the per-SourceKey rate budget grouping.")]
        public string SourceKey { get; set; }

        [FieldDefinition(3, Label = "User-Agent Override", Advanced = true, HelpText = "Optional UA override. Leave blank for honest 'Mangarr/{version}'.")]
        public string UserAgentOverride { get; set; }

        [FieldDefinition(4, Label = "Rate Override", Advanced = true, HelpText = "Override default request rate (e.g., 00:00:01 = 60 req/min). Leave blank for the conservative MAL ~60 req/min default.")]
        public TimeSpan? Rate { get; set; }

        // IIndexerSettings members — unused by metadata sources but required by the
        // IHttpAggregatorSettings : IIndexerSettings contract. Default to empty arrays.
        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate() => new(Validator.Validate(this));
    }
}
