using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public interface IDownloadClientFactory : IProviderFactory<IDownloadClient, DownloadClientDefinition>
    {
        List<IDownloadClient> DownloadHandlingEnabled(bool filterBlockedClients = true);
        DownloadClientDefinition ResolveDownloadClient(int? id, string name);
    }

    public class DownloadClientFactory : ProviderFactory<IDownloadClient, DownloadClientDefinition>, IDownloadClientFactory
    {
        // Sonarr divergence: zero-config first-run UX (PROJECT.md v1 lock).
        // Sonarr does NOT auto-seed download clients — users opt in by adding one. Mangarr
        // DOES, because v1 ships with a bundled in-process downloader (Phase 4 D-04 +
        // DOWNLOAD-01) that has no external dependencies. Without this pre-seeded,
        // /settings/downloadclients is empty on a fresh DB and the user has no signal that
        // a working download client is already bundled. Seed mirrors the
        // MetadataSourceFactory S4-pattern InitializeProviders override (idempotent on
        // restart; sibling-divergence rationale documented in DIVERGENCE.md Phase 15
        // entries).
        private const string SeededDownloadClientImplementation = nameof(InProcessImageDownloadClient);

        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly Logger _logger;

        public DownloadClientFactory(IDownloadClientStatusService downloadClientStatusService,
                                     IDownloadClientRepository providerRepository,
                                     IEnumerable<IDownloadClient> providers,
                                     IServiceProvider container,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _downloadClientStatusService = downloadClientStatusService;
            _logger = logger;
        }

        protected override List<DownloadClientDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IDownloadClient provider, DownloadClientDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.Protocol = provider.Protocol;
        }

        public List<IDownloadClient> DownloadHandlingEnabled(bool filterBlockedClients = true)
        {
            var enabledClients = GetAvailableProviders();

            if (filterBlockedClients)
            {
                return FilterBlockedClients(enabledClients).ToList();
            }

            return enabledClients.ToList();
        }

        public DownloadClientDefinition ResolveDownloadClient(int? id, string name)
        {
            var all = All();
            var clientByName = name.IsNullOrWhiteSpace() ? null : all.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
            var clientById = id is > 0 ? all.FirstOrDefault(c => c.Id == id.Value) : null;

            if (id is > 0 && clientById == null)
            {
                throw new ResolveDownloadClientException("Download client with ID '{0}' could not be found", id.Value);
            }

            if (name.IsNotNullOrWhiteSpace() && clientByName == null)
            {
                throw new ResolveDownloadClientException("Download client with name '{0}' could not be found", name);
            }

            if (clientByName == null && clientById == null)
            {
                return null;
            }

            if (clientByName != null && clientById != null && clientByName.Id != clientById.Id)
            {
                throw new ResolveDownloadClientException("Download client with name '{0}' does not match download client with ID '{1}'", name, id.Value);
            }

            var client = clientById ?? clientByName;

            if (!client.Enable)
            {
                throw new ResolveDownloadClientException("Download client '{0}' ({1}) is not enabled", client.Name, id);
            }

            return client;
        }

        private IEnumerable<IDownloadClient> FilterBlockedClients(IEnumerable<IDownloadClient> clients)
        {
            var blockedClients = _downloadClientStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId, v => v);

            foreach (var client in clients)
            {
                if (blockedClients.TryGetValue(client.Definition.Id, out var downloadClientStatus))
                {
                    _logger.Debug("Temporarily ignoring download client {0} till {1} due to recent failures.", client.Definition.Name, downloadClientStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                yield return client;
            }
        }

        public override ValidationResult Test(DownloadClientDefinition definition)
        {
            var result = base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                _downloadClientStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _downloadClientStatusService.RecordFailure(definition.Id);
            }

            return result;
        }

        // Sonarr divergence: zero-config first-run UX (see SeededDownloadClientImplementation
        // comment above). Mirrors MetadataSourceFactory.InitializeProviders idempotent
        // S4-pattern. We seed ONLY when the table is empty — a user who deletes the seeded
        // row will not see it re-created on next start.
        //
        // DownloadClientBase.DefaultDefinitions returns an empty list (NOT virtual on the
        // base; intentional — Sonarr's other clients ship preset configs but the in-process
        // client has only one shape). So we hand-roll the DownloadClientDefinition with the
        // settings POCO's compile-time defaults (DownloadsPerSource=2, PagesPerChapter=4,
        // RetentionDays=7 per InProcessImageDownloadClientSettings).
        protected override void InitializeProviders()
        {
            if (All().Any())
            {
                return;
            }

            var provider = _providers.FirstOrDefault(p => p.GetType().Name == SeededDownloadClientImplementation);

            if (provider == null)
            {
                _logger.Warn("Skipping seed of {0} — provider implementation not registered (was the binary built without it?).", SeededDownloadClientImplementation);
                return;
            }

            var settings = (IProviderConfig)Activator.CreateInstance(provider.ConfigContract);

            var definition = new DownloadClientDefinition
            {
                // User-facing friendly name (matches IDownloadClient.Name = "Mangarr In-Process Downloader").
                Name = provider.Name,
                Implementation = provider.GetType().Name,
                ConfigContract = provider.ConfigContract.Name,
                Settings = settings,
                Enable = true,
                Priority = 1,
                RemoveCompletedDownloads = true,
                RemoveFailedDownloads = true,
            };

            _logger.Info("Seeding default download client: {0} ({1})", definition.Name, definition.Implementation);
            Create(definition);
        }
    }
}
