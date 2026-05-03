using System;
using Equ;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public class IndexerDefinition : ProviderDefinition, IEquatable<IndexerDefinition>
    {
        private static readonly MemberwiseEqualityComparer<IndexerDefinition> Comparer = MemberwiseEqualityComparer<IndexerDefinition>.ByProperties;

        public const int DefaultPriority = 25;

        public IndexerDefinition()
        {
            Priority = DefaultPriority;
        }

        [MemberwiseEqualityIgnore]
        public DownloadProtocol Protocol { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsRss { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsSearch { get; set; }

        public bool EnableRss { get; set; }
        public bool EnableAutomaticSearch { get; set; }
        public bool EnableInteractiveSearch { get; set; }
        public int DownloadClientId { get; set; }
        public int Priority { get; set; }
        public int SeasonSearchMaximumSingleEpisodeAge { get; set; }

        // Phase 6 D-07: per-source RSS sync override. 0 = use global IConfigService.MangaRssSyncInterval.
        // Schema delta in 001_mangarr_baseline.cs (Alter.Table Indexers AddColumn SyncInterval default 0).
        public int SyncInterval { get; set; }

        // Phase 6 D-07: timestamp of last successful RSS sync; null = never synced.
        // Mutated by MangaRssSyncService run; equality-ignored to prevent comparison churn.
        [MemberwiseEqualityIgnore]
        public DateTime? LastRssSync { get; set; }

        [MemberwiseEqualityIgnore]
        public override bool Enable => EnableRss || EnableAutomaticSearch || EnableInteractiveSearch;

        [MemberwiseEqualityIgnore]
        public IndexerStatus Status { get; set; }

        public bool Equals(IndexerDefinition other)
        {
            return Comparer.Equals(this, other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as IndexerDefinition);
        }

        public override int GetHashCode()
        {
            return Comparer.GetHashCode(this);
        }
    }
}
