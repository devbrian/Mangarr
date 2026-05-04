// Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: frontend/src/typings/Blocklist.ts.
//
// Manga sibling preserves: ModelBase + URL-shaped React Query key
// (`['/manga/blocklist']` per Plan 07-02 contract).
//
// Manga sibling diverges from Blocklist:
//   * No quality / languages / protocol / torrentInfoHash (manga has no quality
//     model per Phase 5 D-04; v1 only DownloadProtocol.Http per Phase 4 D-10;
//     D-11 release identity = (sourceKey, releaseGuid, sourceTitle) triple).
//   * Adds chapterIds array (the set of chapters this blocklist row covers).
//   * Field name is `reason` (NOT `message` like Blocklist) — mirrors backend
//     `MangaBlocklistResource.Reason`.
//
// Backend shape source: src/Sonarr.Api.V5/Manga/Blocklist/MangaBlocklistResource.cs
// (Phase 6 Plan 06-09). Field set is exact-mirror at the wire layer.
//
// Phase 8 cleanup: collapse with Blocklist when Tv/ deletes.
import ModelBase from 'App/ModelBase';

export interface MangaBlocklistSubresource {
  id: number;
  title?: string;
}

export interface MangaBlocklist extends ModelBase {
  mangaId: number;
  chapterIds?: number[];
  sourceTitle?: string;
  sourceKey?: string;
  releaseGuid?: string;
  date: string;
  reason?: string;
  source?: string;
  manga?: MangaBlocklistSubresource;
}

export default MangaBlocklist;
