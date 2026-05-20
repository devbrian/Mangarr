using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 (IL-03) — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListSyncCommand.cs.
    //
    // D-04 ENFORCED: this is the ONLY manual-trigger surface for ImportList sync
    // (POST /api/v5/command {name:"ImportListSync"}). No per-list
    // POST /importlist/{id}/sync endpoint ships in Phase 26 — users trigger via
    // System → Tasks UI or the global Command API directly.
    //
    // UpdateScheduledTask returns true only when DefinitionId is null (full-sweep)
    // so the per-list refresh path doesn't reset the global 24h schedule.
    public class ImportListSyncCommand : Command
    {
        public int? DefinitionId { get; set; }

        public ImportListSyncCommand()
        {
        }

        public ImportListSyncCommand(int? definition)
        {
            DefinitionId = definition;
        }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => !DefinitionId.HasValue;
    }
}
