using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    // Phase 30 Plan 30-04 D-02 — abstract base for IMetadata ThingiProvider
    // implementations. Ports src/NzbDrone.Core/ImportLists/ImportListBase.cs:45-100
    // with manga-shape swaps per RESEARCH §2:
    //   * IImportList / ImportListFetchResult → IMetadata / chapter-archive WriteAsync
    //   * IImportListSettings → IProviderConfig (no manga-specific settings base)
    //   * No HttpImportListBase fan-out / no Test() fail-tracking — Metadata providers
    //     are pure file writers; default Test() always-valid.
    //
    // Closest in-tree analog: ImportListBase + NotificationBase (single-instance
    // KomgaNotification pattern). Phase 30 D-04 limits Settings to enable-only;
    // v1.3+ will add the rich-Settings shape when a 2nd writer ships.
    public abstract class MetadataBase<TSettings> : IMetadata
        where TSettings : IProviderConfig, new()
    {
        public abstract string Name { get; }
        public abstract string FormatKey { get; }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                var config = (IProviderConfig)new TSettings();

                yield return new MetadataDefinition
                {
                    Name = GetType().Name,
                    Enable = config.Validate().IsValid,
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

        public virtual ValidationResult Test()
        {
            // Phase 30 D-04 — single-toggle UX; no per-Settings Test surface in v1.2
            // (ComicInfoMetadata has no network IO to probe). v1.3+ may override per
            // provider (e.g. Komga series.json writer testing scan-trigger endpoint).
            return new ValidationResult();
        }

        public abstract bool AppliesTo(ChapterArchiveRequest request);

        public abstract Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct);

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
