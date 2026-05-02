using System;
using Equ;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Phase 2 ProviderDefinition for IMetadataSource per D-15. The IsPrimary flag is
    /// per-instance (mirrors <see cref="Indexers.IndexerDefinition.EnableRss"/>). At-most-one
    /// row has <see cref="IsPrimary"/>=true, enforced in <c>MetadataSourceFactory.SetPrimary</c>
    /// (NOT at the DB level — the DB allows the invariant to be broken, but the factory
    /// always restores it).
    /// </summary>
    public class MetadataSourceDefinition : ProviderDefinition, IEquatable<MetadataSourceDefinition>
    {
        private static readonly MemberwiseEqualityComparer<MetadataSourceDefinition> Comparer
            = MemberwiseEqualityComparer<MetadataSourceDefinition>.ByProperties;

        public bool IsPrimary { get; set; }

        // Metadata sources are always enabled when configured — the per-row Enable bool from
        // ProviderDefinition is unused here; promotion/demotion happens via IsPrimary.
        [MemberwiseEqualityIgnore]
        public override bool Enable => true;

        public bool Equals(MetadataSourceDefinition other)
        {
            return Comparer.Equals(this, other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MetadataSourceDefinition);
        }

        public override int GetHashCode()
        {
            return Comparer.GetHashCode(this);
        }
    }
}
