using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    // Phase 30 Plan 30-04 D-02 — ThingiProvider factory for IMetadata implementations.
    // Ports src/NzbDrone.Core/ImportLists/ImportListFactory.cs:20-111 with Metadata
    // type substitution + a Sonarr-canonical InitializeProviders override per
    // RESEARCH §2.
    //
    // D-11 atomicity invariant: InitializeProviders is invoked via the base class's
    // IHandle<ApplicationStartedEvent> handler during app boot. It calls All() which
    // queries the Metadata table. If Migration 004 has not yet run, the query
    // throws "SQLite no such table" and the app crashes. Plan 30-05 ships
    // Migration 004 first per D-11; this plan's `depends_on: [30-05]` enforces
    // the merge order on the same branch.
    //
    // R-10 RESOLVED: the Metadata table EXISTS in 001_mangarr_baseline.cs:129-134
    // (Sonarr-canonical IMetadataConsumer shape preserved per Phase 15 D-02/D-03).
    // Migration 004 (Plan 30-05) only ALTERs the ChapterFile row + conditionally
    // inserts the ComicInfoMetadata seed row when Config.MetadataFormats contained
    // "comicinfo" (D-03 user-state preservation).
    public class MetadataFactory : ProviderFactory<IMetadata, MetadataDefinition>, IMetadataFactory
    {
        private readonly IMetadataRepository _providerRepository;
        private readonly Logger _logger;

        public MetadataFactory(IMetadataRepository providerRepository,
                               IEnumerable<IMetadata> providers,
                               IServiceProvider container,
                               IEventAggregator eventAggregator,
                               Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _providerRepository = providerRepository;
            _logger = logger;
        }

        // Sonarr-canonical InitializeProviders override per RESEARCH §2.
        // Defense-in-depth alongside Migration 004's conditional seed row (Plan 30-05
        // D-03): if no Metadata row exists for a discovered IMetadata implementation,
        // InsertMany inserts a disabled default. When Migration 004 has already seeded
        // ComicInfoMetadata (the v1.2 default state), this method observes the existing
        // row via Implementation match and DOES NOT re-insert — preserving D-03 user
        // state (Enable=true or Enable=false per the original Config.MetadataFormats
        // value).
        protected override void InitializeProviders()
        {
            var definitions = new List<MetadataDefinition>();
            var newProviders = new List<MetadataDefinition>();
            var existing = All();

            foreach (var provider in _providers)
            {
                var def = new MetadataDefinition
                {
                    Enable = false,
                    Name = provider.GetType().Name,
                    Implementation = provider.GetType().Name,
                    Settings = (IProviderConfig)Activator.CreateInstance(provider.ConfigContract)
                };

                definitions.Add(def);

                if (!existing.Any(c => c.Implementation == def.Implementation))
                {
                    newProviders.Add(def);
                }
            }

            if (newProviders.Any())
            {
                _logger.Debug("Auto-seeding {0} new MetadataDefinition row(s).", newProviders.Count);
                _providerRepository.InsertMany(newProviders);
            }
        }

        // Phase 30 D-02 — archivers call this in place of the previous DryIoc-auto-discovery
        // `IEnumerable<IMetadataWriter>` enumeration. The Enable toggle on MetadataDefinition
        // IS the canonical signal; Config.MetadataFormats is no longer read by archivers
        // post-flip (Plan 30-04 Task 5).
        public List<IMetadata> Enabled()
        {
            return GetAvailableProviders()
                .Where(n => ((MetadataDefinition)n.Definition).Enable)
                .ToList();
        }
    }
}
