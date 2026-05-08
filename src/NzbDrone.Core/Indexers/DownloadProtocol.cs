namespace NzbDrone.Core.Indexers
{
    public enum DownloadProtocol
    {
        Unknown = 0,

        // Sonarr divergence: Phase 15 D-18 — Usenet=1 + Torrent=2 deleted (TV download clients removed per D-14;
        // gap-tolerated rather than renumber Http=1 because: (a) preserves persisted-int compatibility for any
        // straggler test fixtures + (b) the gap is harmless once enum-int parsers tolerate non-contiguous values.
        // Researcher/CONTEXT.md Discretion lean: keep Http=3 with gap. Matches Phase 14 commit ecb9c0a25 verbatim.
        Http = 3
    }
}
