using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public abstract class IndexerBase<TSettings> : IIndexer
        where TSettings : IIndexerSettings, new()
    {
        protected readonly IIndexerStatusService _indexerStatusService;
        protected readonly IConfigService _configService;
        protected readonly IMangaParsingService _parsingService;
        protected readonly Logger _logger;
        protected readonly ILocalizationService _localizationService;

        public abstract string Name { get; }
        public abstract DownloadProtocol Protocol { get; }
        public int Priority { get; set; }
        public int SeasonSearchMaximumSingleEpisodeAge { get; set; }

        public abstract bool SupportsRss { get; }
        public abstract bool SupportsSearch { get; }

        public IndexerBase(IIndexerStatusService indexerStatusService, IConfigService configService, IMangaParsingService parsingService, Logger logger, ILocalizationService localizationService)
        {
            _indexerStatusService = indexerStatusService;
            _configService = configService;
            _parsingService = parsingService;
            _logger = logger;
            _localizationService = localizationService;
        }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                var config = (IProviderConfig)new TSettings();

                yield return new IndexerDefinition
                {
                    Name = GetType().Name,
                    EnableRss = config.Validate().IsValid && SupportsRss,
                    EnableAutomaticSearch = config.Validate().IsValid && SupportsSearch,
                    EnableInteractiveSearch = config.Validate().IsValid && SupportsSearch,
                    Implementation = GetType().Name,
                    Settings = config
                };
            }
        }

        public virtual ProviderDefinition Definition { get; set; }

        public virtual object RequestAction(string action, IDictionary<string, string> query)
        {
            return null;
        }

        protected TSettings Settings => (TSettings)Definition.Settings;

        public abstract Task<IList<ReleaseInfo>> FetchRecent();

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape Fetch overloads
        // (SeasonSearchCriteria / Single|Daily|Anime|SpecialEpisodeSearchCriteria, etc.)
        // stripped per Plan 15-10 IndexerSearch/Definitions DELETE. Manga overloads now
        // canonical (HttpAggregatorBase + descendants override concretely).
        public abstract Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria);
        public abstract Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria);

        public abstract HttpRequest GetDownloadRequest(string link);

        protected virtual IList<ReleaseInfo> CleanupReleases(IEnumerable<ReleaseInfo> releases)
        {
            var result = releases.DistinctBy(v => v.Guid).ToList();
            var settings = Definition.Settings as IIndexerSettings;

            result.ForEach(c =>
            {
                // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
                // Parser.Parser.HasMultipleLanguages stripped (Parser/Parser.cs DELETED);
                // multi-language fallback from settings still applies on empty Languages.
                if (c.Languages.Empty() && settings.MultiLanguages.Any())
                {
                    c.Languages = settings.MultiLanguages.Select(i => (Language)i).ToList();
                }

                c.IndexerId = Definition.Id;
                c.Indexer = Definition.Name;
                c.DownloadProtocol = Protocol;
                c.IndexerPriority = ((IndexerDefinition)Definition).Priority;
                c.SeasonSearchMaximumSingleEpisodeAge = ((IndexerDefinition)Definition).SeasonSearchMaximumSingleEpisodeAge;
            });

            return result;
        }

        public ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                Test(failures).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test aborted due to exception");
                failures.Add(new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("IndexerValidationTestAbortedDueToError", new Dictionary<string, object> { { "exceptionMessage", ex.Message } })));
            }

            return new ValidationResult(failures);
        }

        protected abstract Task Test(List<ValidationFailure> failures);

        public override string ToString()
        {
            return Definition.Name;
        }
    }
}
