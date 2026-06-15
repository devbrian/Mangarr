// quick-260615-edl: derive the per-release source key from a gateway release guid.
//
// Gateway release guids follow the stable shape `source:mangaId:ch:lang:releaseId`
// (e.g. `mangadot:1340:ch-24:en:104168`), so the leading colon-segment IS the
// per-release source (mangadot / mangafire / mangaball / …).
//
// Why guid-prefix and not the `sourceKey` field on History/Blocklist: since
// Phase 39 made GatewayIndexer the sole indexer, those records' `sourceKey`
// holds the CONSTANT indexer name ("Mangarr Gateway"), not the underlying
// source — useless for distinguishing releases. The Queue surface is the
// exception: its `sourceKey` (= ReleaseInfo.Source) already carries the real
// per-source token, so Queue renders that field directly and does NOT use this.
//
// Returns '' when the guid is absent or empty (legacy blocklist rows persisted
// before the guid-population fix, and imported-history events which never carry
// a release guid) so the cell renders blank rather than a misleading value.
export default function parseSourceFromGuid(guid?: string): string {
  if (!guid) {
    return '';
  }

  const source = guid.split(':', 1)[0];
  return source ?? '';
}
