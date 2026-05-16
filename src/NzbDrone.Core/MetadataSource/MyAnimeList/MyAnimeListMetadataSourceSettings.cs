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

        [FieldDefinition(0, Label = "MyAnimeListMetadataSourceUrl", HelpText = "MyAnimeListMetadataSourceUrlHelpText")]
        public string BaseUrl { get; set; }

        [FieldDefinition(
            1,
            Label = "MyAnimeListMetadataSourceClientId",
            HelpText = "MyAnimeListMetadataSourceClientIdHelpText",
            Privacy = PrivacyLevel.ApiKey)]
        public string ClientId { get; set; }

        [FieldDefinition(2, Label = "MyAnimeListMetadataSourceSourceKey", Advanced = true, HelpText = "MyAnimeListMetadataSourceSourceKeyHelpText")]
        public string SourceKey { get; set; }

        [FieldDefinition(3, Label = "MyAnimeListMetadataSourceUserAgentOverride", Advanced = true, HelpText = "MyAnimeListMetadataSourceUserAgentOverrideHelpText")]
        public string UserAgentOverride { get; set; }

        [FieldDefinition(4, Label = "MyAnimeListMetadataSourceRateOverride", Advanced = true, HelpText = "MyAnimeListMetadataSourceRateOverrideHelpText")]
        public TimeSpan? Rate { get; set; }

        // IIndexerSettings members — unused by metadata sources but required by the
        // IHttpAggregatorSettings : IIndexerSettings contract. Default to empty arrays.
        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate() => new(Validator.Validate(this));
    }
}
