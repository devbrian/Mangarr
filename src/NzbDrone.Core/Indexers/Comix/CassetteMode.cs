namespace NzbDrone.Core.Indexers.Comix
{
    // Sonarr divergence: no Sonarr peer. Phase 33 (COMIX2-01) Plan 33-02 introduces a
    // Core-side mirror of TestKit/CassetteHandler.cs:14's CassetteMode enum so the DryIoc
    // registration in NzbDrone.Host/Startup.cs can parse the MANGARR_TEST_CASSETTE_MODE
    // env-var string into a CassetteMode value WITHOUT taking a hard dep on the
    // Mangarr.Automation.Test assembly (parallel to the reflection-load shape
    // ManagedHttpDispatcher.cs:181-185 uses at the HTTP layer, but here Mangarr.Core IS
    // the assembly that defines the consuming signer so we can take a direct typeof()).
    //
    // Per CONTEXT.md D-03 (single env-var pair, both layers obey) — value names + ordering
    // MUST match TestKit/CassetteHandler.cs:14 verbatim so the same env-var string parses
    // identically at the HTTP-layer CassetteHandler AND the signer-layer
    // CassettingComixSigner. Drift between the two enum definitions would silently break
    // the per-layer parity guarantee.
    //
    // Mangarr-only seam; Pattern S2 / sonarr-consistency-audit Pattern ι allowlist coverage.

    /// <summary>
    /// Mirror of <c>NzbDrone.Automation.Test.TestKit.CassetteMode</c>
    /// (TestKit/CassetteHandler.cs:14). Drives both the HTTP-layer
    /// <c>CassetteHandler</c> and the signer-layer <see cref="CassettingComixSigner"/>
    /// from a single <c>MANGARR_TEST_CASSETTE_MODE</c> env-var value per Phase 33
    /// D-03.
    /// </summary>
    public enum CassetteMode
    {
        /// <summary>Read cassette from disk; throw <see cref="System.InvalidOperationException"/> on miss (CI default).</summary>
        Replay,

        /// <summary>Always delegate to the inner signer (live comix.to); record the returned body to disk.</summary>
        Record,

        /// <summary>Read cassette if exists; on miss, delegate to inner + record (orchestrator-driven LIVE recording flow per D-07).</summary>
        ReplayOrRecord
    }
}
