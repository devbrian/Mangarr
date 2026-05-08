using System;
using Equ;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    public class NotificationDefinition : ProviderDefinition, IEquatable<NotificationDefinition>
    {
        private static readonly MemberwiseEqualityComparer<NotificationDefinition> Comparer = MemberwiseEqualityComparer<NotificationDefinition>.ByProperties;

        public bool OnGrab { get; set; }
        public bool OnDownload { get; set; }
        public bool OnUpgrade { get; set; }
        public bool OnImportComplete { get; set; }
        public bool OnRename { get; set; }
        public bool OnHealthIssue { get; set; }
        public bool IncludeHealthWarnings { get; set; }
        public bool OnHealthRestored { get; set; }
        public bool OnApplicationUpdate { get; set; }
        public bool OnManualInteractionRequired { get; set; }

        // Phase 6 D-18 — user toggle for OnChapterImport fan-out (Settings → Notifications).
        // Defaults to TRUE so newly added Komga/Kavita providers fire OnChapterImport without
        // an extra checkbox click; user can still disable per-provider.
        public bool OnChapterImport { get; set; } = true;

        // Phase 8 Plan 99-08 — user toggles for manga library-state fan-out.
        // Default TRUE so v1.1+ providers fire by default (consistent with OnChapterImport pattern).
        public bool OnMangaAdd { get; set; } = true;
        public bool OnMangaDelete { get; set; } = true;
        public bool OnMangaRename { get; set; } = true;

        [MemberwiseEqualityIgnore]
        public bool SupportsOnGrab { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnDownload { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnUpgrade { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnRename { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnImportComplete { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnHealthIssue { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnHealthRestored { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnApplicationUpdate { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnManualInteractionRequired { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnChapterImport { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnMangaAdd { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnMangaDelete { get; set; }

        [MemberwiseEqualityIgnore]
        public bool SupportsOnMangaRename { get; set; }

        [MemberwiseEqualityIgnore]
        public override bool Enable => OnGrab || OnDownload || (OnDownload && OnUpgrade) || OnImportComplete || OnRename || OnHealthIssue || OnHealthRestored || OnApplicationUpdate || OnManualInteractionRequired || OnChapterImport || OnMangaAdd || OnMangaDelete || OnMangaRename;

        public bool Equals(NotificationDefinition other)
        {
            return Comparer.Equals(this, other);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as NotificationDefinition);
        }

        public override int GetHashCode()
        {
            return Comparer.GetHashCode(this);
        }
    }
}
