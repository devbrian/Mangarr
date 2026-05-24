using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Metadata
{
    // Phase 30 Plan 30-04 D-02 — POCO inheriting ProviderDefinition (manga-shape).
    //
    // ProviderDefinition base already carries Enable + Name + Implementation +
    // ConfigContract + Settings (+ Tags via HashSet). Phase 30 D-04 single-toggle
    // UX locks zero extra fields — no per-MetadataDefinition advanced settings UI
    // in v1.2. v1.3+ adds when a 2nd writer ships (Kodi NFO / Komga series.json /
    // Kavita-flavored).
    //
    // Closest substrate analog: src/NzbDrone.Core/ImportLists/ImportListDefinition.cs
    // (Phase 26 D-13) — but unlike ImportListDefinition this carries no FK fields
    // (no RootFolderPath / TranslationProfileId / etc.) so we use the bare
    // ProviderDefinition contract directly. The corresponding V5 MetadataController
    // also has no SharedValidator FK rules per Phase 30 PATTERNS.md §Plan 30-04.
    public class MetadataDefinition : ProviderDefinition
    {
    }
}
