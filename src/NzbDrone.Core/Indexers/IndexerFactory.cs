using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerFactory : IProviderFactory<IIndexer, IndexerDefinition>
    {
        List<IIndexer> RssEnabled(bool filterBlockedIndexers = true);
        List<IIndexer> AutomaticSearchEnabled(bool filterBlockedIndexers = true);
        List<IIndexer> InteractiveSearchEnabled(bool filterBlockedIndexers = true);
        IndexerDefinition FindByName(string name);
        IndexerDefinition ResolveIndexer(int? id, string name);
    }

    public class IndexerFactory : ProviderFactory<IIndexer, IndexerDefinition>, IIndexerFactory
    {
        // Sonarr divergence: zero-config first-run UX (PROJECT.md v1 lock).
        // Sonarr does NOT auto-seed indexers — users opt in by adding one. Mangarr DOES,
        // because v1 ships with a known-good source. Without it pre-seeded,
        // /settings/indexers is empty on a fresh DB and the user has no signal which
        // Implementation to pick. Seed mirrors MetadataSourceFactory's S4-pattern
        // InitializeProviders override (idempotent on restart; sibling-divergence
        // rationale documented in DIVERGENCE.md Phase 15 entries).
        //
        // Phase 37 A3: GatewayIndexer joins the seed allow-list under the SAME divergence
        // rationale. Its empty default settings (BaseUrl/ApiKey blank) fail
        // config.Validate().IsValid, so IndexerBase.DefaultDefinitions seeds it with
        // EnableRss/EnableAutomaticSearch/EnableInteractiveSearch = false (DISABLED-by-default).
        // This is the runtime seed allow-list, NOT an Insert.IntoTable migration seed; it fires
        // ONLY on an empty Indexers table, so existing users get no surprise row.
        //
        // Phase 39 (RETIRE-02): the in-process MangaDexIndexer + ComixIndexer were deleted;
        // GatewayIndexer (Phase 37) is now the SOLE IIndexer and the sole seeded implementation.
        private static readonly string[] SeededIndexerImplementations =
        {
            nameof(GatewayIndexer)
        };

        private readonly IIndexerRepository _indexerRepository;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly Logger _logger;

        public IndexerFactory(IIndexerStatusService indexerStatusService,
                              IIndexerRepository providerRepository,
                              IEnumerable<IIndexer> providers,
                              IServiceProvider container,
                              IEventAggregator eventAggregator,
                              Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _indexerRepository = providerRepository;
            _indexerStatusService = indexerStatusService;
            _logger = logger;
        }

        protected override List<IndexerDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IIndexer provider, IndexerDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.Protocol = provider.Protocol;
            definition.SupportsRss = provider.SupportsRss;
            definition.SupportsSearch = provider.SupportsSearch;
        }

        public List<IIndexer> RssEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = GetAvailableProviders().Where(n => ((IndexerDefinition)n.Definition).EnableRss);

            if (filterBlockedIndexers)
            {
                return FilterBlockedIndexers(enabledIndexers).ToList();
            }

            return enabledIndexers.ToList();
        }

        public List<IIndexer> AutomaticSearchEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = GetAvailableProviders().Where(n => ((IndexerDefinition)n.Definition).EnableAutomaticSearch);

            if (filterBlockedIndexers)
            {
                return FilterBlockedIndexers(enabledIndexers).ToList();
            }

            return enabledIndexers.ToList();
        }

        public List<IIndexer> InteractiveSearchEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = GetAvailableProviders().Where(n => ((IndexerDefinition)n.Definition).EnableInteractiveSearch);

            if (filterBlockedIndexers)
            {
                return FilterBlockedIndexers(enabledIndexers).ToList();
            }

            return enabledIndexers.ToList();
        }

        public IndexerDefinition FindByName(string name)
        {
            return _indexerRepository.FindByName(name);
        }

        public IndexerDefinition ResolveIndexer(int? id, string name)
        {
            var all = All();
            var clientByName = name.IsNullOrWhiteSpace() ? null : all.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
            var clientById = id is > 0 ? all.FirstOrDefault(c => c.Id == id.Value) : null;

            if (id is > 0 && clientById == null)
            {
                throw new ResolveIndexerException("Indexer with ID '{0}' could not be found", id.Value);
            }

            if (name.IsNotNullOrWhiteSpace() && clientByName == null)
            {
                throw new ResolveIndexerException("Indexer with name '{0}' could not be found", name);
            }

            if (clientByName == null && clientById == null)
            {
                return null;
            }

            if (clientByName != null && clientById != null && clientByName.Id != clientById.Id)
            {
                throw new ResolveIndexerException("Indexer with name '{0}' does not match indexer with ID '{1}'", name, id.Value);
            }

            return clientById ?? clientByName;
        }

        private IEnumerable<IIndexer> FilterBlockedIndexers(IEnumerable<IIndexer> indexers)
        {
            var blockedIndexers = _indexerStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId, v => v);

            foreach (var indexer in indexers)
            {
                if (blockedIndexers.TryGetValue(indexer.Definition.Id, out var blockedIndexerStatus))
                {
                    _logger.Debug("Temporarily ignoring indexer {0} till {1} due to recent failures.", indexer.Definition.Name, blockedIndexerStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                yield return indexer;
            }
        }

        public override ValidationResult Test(IndexerDefinition definition)
        {
            var result = base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                _indexerStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _indexerStatusService.RecordFailure(definition.Id);
            }

            return result;
        }

        // Sonarr divergence: zero-config first-run UX (see SeededIndexerImplementations
        // comment above). Mirrors MetadataSourceFactory.InitializeProviders idempotent
        // S4-pattern (TranslationProfileService.Handle is the canonical seed precedent).
        // We seed ONLY when the table is empty — a user who deletes a seeded row will
        // not see it re-created on next start. We resolve each provider via the injected
        // IEnumerable<IIndexer> (auto-discovered by ThingiProvider reflection) and call
        // its DefaultDefinitions to source default Settings; we then override the Name
        // to the user-facing IIndexer.Name (vs IndexerBase.DefaultDefinitions's
        // GetType().Name) so the row label matches what the Add-Indexer modal shows.
        protected override void InitializeProviders()
        {
            if (All().Any())
            {
                return;
            }

            foreach (var implementationName in SeededIndexerImplementations)
            {
                var provider = _providers.FirstOrDefault(p => p.GetType().Name == implementationName);

                if (provider == null)
                {
                    _logger.Warn("Skipping seed of {0} — provider implementation not registered (was the binary built without it?).", implementationName);
                    continue;
                }

                var defaultDefinition = provider.DefaultDefinitions
                    .OfType<IndexerDefinition>()
                    .FirstOrDefault();

                if (defaultDefinition == null)
                {
                    _logger.Warn("Skipping seed of {0} — provider exposes no IndexerDefinition in DefaultDefinitions.", implementationName);
                    continue;
                }

                // Use the user-facing friendly name (provider.Name) as the row label —
                // matches the Add-Indexer modal Implementation name shown to users.
                defaultDefinition.Name = provider.Name;

                _logger.Info("Seeding default indexer: {0} ({1})", defaultDefinition.Name, defaultDefinition.Implementation);
                Create(defaultDefinition);
            }
        }
    }
}
