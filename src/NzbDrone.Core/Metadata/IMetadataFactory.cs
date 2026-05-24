using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    // Phase 30 Plan 30-04 D-02 — manga-shape ThingiProvider factory contract.
    //
    // Split into its own file (vs co-located inside MetadataFactory.cs like
    // ImportListFactory.cs) per Plan 30-04 frontmatter `files_modified` which
    // explicitly enumerates both `IMetadataFactory.cs` and `MetadataFactory.cs`.
    // Functional shape identical to the in-tree IImportListFactory: extends the
    // generic IProviderFactory + adds the `Enabled()` enumeration that archivers
    // call post Phase 30 D-02 flip.
    public interface IMetadataFactory : IProviderFactory<IMetadata, MetadataDefinition>
    {
        List<IMetadata> Enabled();
    }
}
