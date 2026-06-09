using System;
using Equ;
using NzbDrone.Core.Manga;
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
    //   * SearchForMissingEpisodes — preserved verbatim per RESEARCH §Q1.
    //   * ShouldMonitor — #357: retyped from the Sonarr-inherited MonitorTypes enum to the
    //                          single canonical 7-value MangaMonitor (Core.Manga). The persisted
    //                          AsInt32() ShouldMonitor column keeps its meaning because MangaMonitor
    //                          declares explicit ordinals matching the old MonitorTypes values.
    //   * MonitorNewItems — #356: REMOVED from the definition (and the V5 resource). The
    //                          ImportLists.MonitorNewItems DB column is intentionally left orphaned
    //                          (D-1, no migration) — it is NotNullable().WithDefaultValue(0), so
    //                          inserts that no longer map the property fall back to the DB default;
    //                          the column is simply never read/written. New-chapter monitoring is
    //                          derived from ShouldMonitor via MangaMonitorExtensions.DeriveMonitorNewItems.
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
        public MangaMonitor ShouldMonitor { get; set; }
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
}
