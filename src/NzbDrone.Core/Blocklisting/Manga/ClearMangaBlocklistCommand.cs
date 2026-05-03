using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-19 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Blocklisting/ClearBlocklistCommand.cs.
    // UI button "Clear blocklist" wires to IMangaBlocklistService.Execute(this) — purges all rows.
    // Phase 8 cleanup: collapse with ClearBlocklistCommand when Tv/ deletes.
    public class ClearMangaBlocklistCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
