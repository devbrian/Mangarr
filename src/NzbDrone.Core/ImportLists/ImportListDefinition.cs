using System;
using Equ;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListDefinition.cs with
    // manga-shape swaps locked by Phase 26 D-07 / D-13 / D-15:
    //   * QualityProfileId  → TranslationProfileId + CustomFormatProfileId (Phase 5 D-04
    //                          + Migration 003 schema).
    //   * SeasonFolder      → DROPPED (TV-only; manga has no Season concept per
    //                          PROJECT.md v1 lock — Migration 001 stripped the column).
    //   * SeriesType        → DROPPED (TV-only; Migration 001 stripped the column).
    //   * SearchForMissingEpisodes / ShouldMonitor / MonitorNewItems — preserved verbatim
    //                          per RESEARCH §Q1 (Sonarr-canonical opt-in semantics
    //                          still apply to manga; ShouldMonitor uses Mangarr's existing
    //                          MonitorTypes enum from Core.Manga).
    //   * EnableAutomaticAdd / RootFolderPath / Tags — preserved verbatim.
    //
    // Equ memberwise equality preserved verbatim; Enable override maps onto
    // EnableAutomaticAdd; runtime-only fields (Status / ListType / MinRefreshInterval)
    // ride [MemberwiseEqualityIgnore] so they don't fail the configuration-equality
    // round-trip the provider factory uses to detect setting changes.
    public class ImportListDefinition : ProviderDefinition, IEquatable<ImportListDefinition>
    {
        private static readonly MemberwiseEqualityComparer<ImportListDefinition> Comparer = MemberwiseEqualityComparer<ImportListDefinition>.ByProperties;

        public bool EnableAutomaticAdd { get; set; }
        public bool SearchForMissingChapters { get; set; }
        public MonitorTypes ShouldMonitor { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
        public int TranslationProfileId { get; set; }
        public int CustomFormatProfileId { get; set; }
        public string RootFolderPath { get; set; }

        [MemberwiseEqualityIgnore]
        public override bool Enable => EnableAutomaticAdd;

        [MemberwiseEqualityIgnore]
        public ImportListStatus Status { get; set; }

        [MemberwiseEqualityIgnore]
        public ImportListType ListType { get; set; }

        [MemberwiseEqualityIgnore]
        public TimeSpan MinRefreshInterval { get; set; }

        public bool Equals(ImportListDefinition other)
        {
            return Comparer.Equals(this, other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ImportListDefinition);
        }

        public override int GetHashCode()
        {
            return Comparer.GetHashCode(this);
        }
    }

    // Sonarr's MonitorTypes / NewItemMonitorTypes live under NzbDrone.Core.Tv (DELETED
    // in Phase 15 D-24). Mangarr's manga-shape peers below carry the same value semantics
    // verbatim per RESEARCH §Q1 — Sonarr-canonical opt-in for ImportList "add new manga
    // monitored?" / "monitor newly-listed items?" UX. Lives in this file (sibling to
    // ImportListDefinition) so the substrate is self-contained; future plans MAY hoist to
    // Core.Manga if more callers materialize.
    public enum MonitorTypes
    {
        None = 0,
        All = 1,
        Existing = 2,
        Latest = 3,
        First = 4
    }

    public enum NewItemMonitorTypes
    {
        None = 0,
        All = 1
    }
}
