using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update
{
    // Phase 29 D-01..D-04 — interface relocated off NoOpUpdatePackageProvider.cs
    // (Phase 15 D-21 placeholder, deleted in same plan) to preserve the contract for
    // the new GitHubReleasesUpdatePackageProvider. DryIoc convention-based auto-discovery
    // picks up the single remaining implementation; relocating the interface to its own
    // file avoids losing the contract when NoOpUpdatePackageProvider.cs is removed.
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }
}
