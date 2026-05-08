using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// ThingiProvider factory for <see cref="IMetadataSource"/> per D-14. Inherits the
    /// standard ProviderFactory CRUD + auto-discovery surface and adds the IsPrimary
    /// at-most-one invariant operations per D-15 + threat T-CONFIG-DRIFT-01.
    ///
    /// The DB schema (Migration 002) allows multiple IsPrimary=true rows; the invariant is
    /// enforced ONLY here in the factory. Every promotion goes through
    /// <see cref="SetPrimary(int)"/> which demotes ALL rows then promotes the target in a
    /// single batched <see cref="ProviderFactory{TProvider,TProviderDefinition}.Update(System.Collections.Generic.IEnumerable{TProviderDefinition})"/>
    /// (which delegates to <c>IProviderRepository.UpdateMany</c>).
    /// </summary>
    public class MetadataSourceFactory
        : ProviderFactory<IMetadataSource, MetadataSourceDefinition>, IMetadataSourceFactory
    {
        // BL-06 fix: serialize concurrent SetPrimary callers so the read-modify-write
        // (load all → flip flags → UpdateMany) cannot interleave between two requests.
        // Without this lock, two concurrent POSTs to /metadatasource/{id}/setprimary
        // could both observe the pre-state, both call UpdateMany, and the second writer
        // could leave two rows with IsPrimary=true (each call only flips its own target
        // row to true, and ProviderFactory.Update is row-by-row, not a single SQL
        // UPDATE). The factory is registered as a DryIoc singleton, so an in-process
        // lock suffices for v1 (multi-process deployment isn't a v1 concern). The
        // instance-level lock would also work; static keeps the invariant even if a
        // future test seam constructs a second instance against the same repository.
        private static readonly object _setPrimaryLock = new();

        public MetadataSourceFactory(IMetadataSourceRepository providerRepository,
                                     IEnumerable<IMetadataSource> providers,
                                     IServiceProvider container,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
        }

        public MetadataSourceDefinition GetPrimary()
        {
            var primary = All().SingleOrDefault(d => d.IsPrimary);

            if (primary == null)
            {
                throw new InvalidOperationException("No primary metadata source configured");
            }

            return primary;
        }

        public void SetPrimary(int id)
        {
            lock (_setPrimaryLock)
            {
                var all = All().ToList();
                var target = all.FirstOrDefault(d => d.Id == id);

                if (target == null)
                {
                    throw new InvalidOperationException($"MetadataSource id={id} not found");
                }

                // D-15 + T-CONFIG-DRIFT-01: demote-all + promote-one in a single batched update.
                // ProviderFactory.Update(IEnumerable) routes to IProviderRepository.UpdateMany,
                // so the at-most-one invariant is restored atomically per call.
                foreach (var d in all)
                {
                    d.IsPrimary = d.Id == id;
                }

                Update(all);
            }
        }

        // Indexer / DownloadClient / Notification factories don't auto-seed because those
        // are user-config (the user opts in by adding one). Metadata source is different:
        // without a primary configured, /api/v5/manga/lookup throws 500 and the entire
        // Add Manga UX is broken on a fresh DB. Seed the IsPrimary=true DefaultDefinition
        // so MangaDex (D-16) is the out-of-the-box primary. AniList / MAL stay un-instanced
        // — their schema presets at /api/v5/metadatasource/schema are how users add them,
        // and skipping them dodges the UNIQUE(MetadataSources.Name) collision that
        // ProviderFactory.GetDefaultDefinitions creates by falling back to Name="" for
        // every provider whose DefaultDefinitions[0].Name doesn't equal GetType().Name
        // (MetadataSourceBase sets it to the friendly Name property instead). Mirrors
        // the S4 idempotent-seed pattern in TranslationProfileService.Handle.
        protected override void InitializeProviders()
        {
            if (All().Any())
            {
                return;
            }

            foreach (var provider in _providers)
            {
                var primaryDefault = provider.DefaultDefinitions
                    .OfType<MetadataSourceDefinition>()
                    .FirstOrDefault(d => d.IsPrimary);

                if (primaryDefault != null)
                {
                    Create(primaryDefault);
                }
            }
        }
    }
}
