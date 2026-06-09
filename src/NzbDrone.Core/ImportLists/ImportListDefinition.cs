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
    //   * MonitorNewItems — #356: REMOVED from the V5 resource + every user-facing surface, but
    //                          KEPT as an INTERNAL field here. The ImportLists.MonitorNewItems DB
    //                          column is NotNullable() (001_mangarr_baseline.cs) and Mangarr's
    //                          BasicRepository builds its INSERT column list from the POCO's mapped
    //                          properties (BasicRepository.GetInsertSql) — dropping the property
    //                          would NOT omit-and-default the column, it trips the NOT NULL
    //                          constraint on every insert. So the property stays (typed as the
    //                          still-existing MangaMonitorNewItems, default All=0), is never set by
    //                          the resource mapper or read by the sync service, and persists the DB
    //                          default. New-chapter monitoring is derived from ShouldMonitor via
    //                          MangaMonitorExtensions.DeriveMonitorNewItems — this field is a
    //                          no-migration column placeholder only.
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

        // #356: internal-only no-migration column placeholder (see header note). Never surfaced
        // on the V5 resource or read by ImportListSyncService — it persists the NotNullable DB
        // column's default (All=0) so BasicRepository's POCO-driven INSERT stays valid.
        public MangaMonitorNewItems MonitorNewItems { get; set; }

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
