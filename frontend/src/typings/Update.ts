export interface Changes {
  new: string[];
  fixed: string[];
}

interface Update {
  version: string;
  branch: string;
  releaseDate: string;
  fileName: string;
  url: string;
  installed: boolean;
  installedOn: string;
  installable: boolean;
  latest: boolean;
  changes: Changes | null;
  hash: string;
  // Phase 29 D-04: GitHub Release HTML URL surfaced by GitHubReleasesUpdatePackageProvider
  // for the Updates page "View GitHub Release" link. Optional — legacy/no-op providers
  // leave this null.
  htmlUrl?: string | null;
}

export default Update;
