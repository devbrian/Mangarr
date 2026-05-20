using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListFactory.cs.
    // Generic type swap: IImportList → IMangaImportList (Mangarr peer contract).
    //
    // PHASE-26 D-08: zero production providers ship in this phase; Phase 27 owns
    // provider seeding. The IndexerFactory.InitializeProviders SeededIndexerImplementations
    // pattern is INTENTIONALLY NOT carried over — production reflection-scan returns 0
    // IMangaImportList implementations (Pitfall 2 anti-prod-leak verified by
    // ImportListFactoryFixture.factory_returns_zero_providers_on_empty_di_bag).
    public interface IImportListFactory : IProviderFactory<IMangaImportList, ImportListDefinition>
    {
        List<IMangaImportList> AutomaticAddEnabled(bool filterBlockedImportLists = true);
        ImportListDefinition FindByName(string name);
    }

    public class ImportListFactory : ProviderFactory<IMangaImportList, ImportListDefinition>, IImportListFactory
    {
        private readonly IImportListRepository _importListRepository;
        private readonly IImportListStatusService _importListStatusService;
        private readonly Logger _logger;

        public ImportListFactory(IImportListStatusService importListStatusService,
                              IImportListRepository providerRepository,
                              IEnumerable<IMangaImportList> providers,
                              IServiceProvider container,
                              IEventAggregator eventAggregator,
                              Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _importListRepository = providerRepository;
            _importListStatusService = importListStatusService;
            _logger = logger;
        }

        protected override List<ImportListDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IMangaImportList provider, ImportListDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.ListType = provider.ListType;
            definition.MinRefreshInterval = provider.MinRefreshInterval;
        }

        public List<IMangaImportList> AutomaticAddEnabled(bool filterBlockedImportLists = true)
        {
            var enabledImportLists = GetAvailableProviders().Where(n => ((ImportListDefinition)n.Definition).EnableAutomaticAdd);

            if (filterBlockedImportLists)
            {
                return FilterBlockedImportLists(enabledImportLists).ToList();
            }

            return enabledImportLists.ToList();
        }

        public ImportListDefinition FindByName(string name)
        {
            return _importListRepository.FindByName(name);
        }

        private IEnumerable<IMangaImportList> FilterBlockedImportLists(IEnumerable<IMangaImportList> importLists)
        {
            var blockedImportLists = _importListStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId, v => v);

            foreach (var importList in importLists)
            {
                if (blockedImportLists.TryGetValue(importList.Definition.Id, out var blockedImportListStatus))
                {
                    _logger.Debug("Temporarily ignoring import list {0} till {1} due to recent failures.", importList.Definition.Name, blockedImportListStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                yield return importList;
            }
        }

        public override ValidationResult Test(ImportListDefinition definition)
        {
            var result = base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                _importListStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _importListStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
