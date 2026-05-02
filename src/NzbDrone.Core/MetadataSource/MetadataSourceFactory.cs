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
                d.IsPrimary = (d.Id == id);
            }

            Update(all);
        }
    }
}
