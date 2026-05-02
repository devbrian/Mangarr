using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public class AniListMetadataSourceSettingsValidator : AbstractValidator<AniListMetadataSourceSettings>
    {
        public AniListMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.SourceKey).NotEmpty();
        }
    }

    /// <summary>
    /// Settings POCO for <c>AniListMetadataSource</c>. Implements
    /// <see cref="IHttpAggregatorSettings"/> per Phase 1 D-12/D-13/D-14 — same SourceKey + Rate +
    /// UA-override contract that <c>HttpAggregatorSettings</c> uses for indexers, but as a
    /// stand-alone POCO so the metadata-source ThingiProvider family stays separate from the
    /// indexer family (per <c>HttpMetadataSourceBase</c> sibling note in MetadataSource/CLAUDE.md).
    ///
    /// Defaults:
    ///   - <see cref="BaseUrl"/>             = "https://graphql.anilist.co" (AniList GraphQL endpoint)
    ///   - <see cref="SourceKey"/>           = "anilist" (rate-limit bucket; 30 req/min per Phase 1 STACK research)
    ///   - <see cref="UserAgentOverride"/>   = blank → honest "Mangarr/{version}" applied by HttpMetadataSourceBase
    ///   - <see cref="Rate"/>                = null  → IHttpAggregatorSettings.Rate falls back to whatever the
    ///                                                  rate-limiter pre-defines for "anilist".
    /// </summary>
    public class AniListMetadataSourceSettings : IHttpAggregatorSettings
    {
        private static readonly AniListMetadataSourceSettingsValidator Validator = new();

        public AniListMetadataSourceSettings()
        {
            BaseUrl = "https://graphql.anilist.co";
            SourceKey = "anilist";
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
        }

        [FieldDefinition(0, Label = "URL", HelpText = "AniList GraphQL endpoint — leave default.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Rate-limit Source Key", Advanced = true, HelpText = "Override the per-SourceKey rate budget grouping.")]
        public string SourceKey { get; set; }

        [FieldDefinition(2, Label = "User-Agent Override", Advanced = true, HelpText = "Optional UA override. Leave blank for honest 'Mangarr/{version}'.")]
        public string UserAgentOverride { get; set; }

        [FieldDefinition(3, Label = "Rate Override", Advanced = true, HelpText = "Override default request rate (e.g., 00:00:02 = 30 req/min). Leave blank for AniList's published 30 req/min.")]
        public TimeSpan? Rate { get; set; }

        // IIndexerSettings requirements — metadata sources do not download nor multi-language;
        // wired empty so the ThingiProvider plumbing has the slots populated.
        public IEnumerable<int> MultiLanguages { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate() => new(Validator.Validate(this));
    }
}
