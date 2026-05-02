using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Manga.Commands
{
    /// <summary>
    /// Verbatim manga-side mirror of <see cref="NzbDrone.Core.Tv.Commands.RefreshSeriesCommand"/>
    /// per D-18. Submitting an empty <see cref="MangaIds"/> list refreshes ALL manga;
    /// submitting one or more IDs refreshes only those targets.
    /// </summary>
    public class RefreshMangaCommand : Command
    {
        // WR-18: this property is INTENTIONALLY a setter-only alias (the getter
        // returns 0 by design and the [JsonIgnore(WhenWritingDefault)] suppresses
        // it on serialization). It mirrors the Sonarr RefreshSeriesCommand.SeriesId
        // pattern verbatim per the project's \"preserve Sonarr's shape\" design
        // philosophy (CLAUDE.md). Behavior:
        //   * Inbound JSON `{ "MangaId": 5 }` → adds 5 to MangaIds (only when the
        //     MangaIds list arrives empty — first-write-wins so a body with both
        //     MangaId and MangaIds prefers MangaIds when it has values).
        //   * Outbound serialization always omits MangaId (getter returns 0 +
        //     WhenWritingDefault suppresses it).
        // Inbound bodies sending BOTH `MangaId` and `MangaIds` get JSON-property-
        // order-dependent behavior — the setter fires once, in arrival order, and
        // appends only when MangaIds is still empty. Same caveat as Sonarr's
        // SeriesId pattern; downstream consumers should send one or the other,
        // not both.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int MangaId
        {
            get => 0;
            set
            {
                if (MangaIds.Empty())
                {
                    MangaIds.Add(value);
                }
            }
        }

        public List<int> MangaIds { get; set; }
        public bool IsNewManga { get; set; }

        public RefreshMangaCommand()
        {
            MangaIds = new List<int>();
        }

        public RefreshMangaCommand(List<int> mangaIds, bool isNewManga = false)
        {
            MangaIds = mangaIds;
            IsNewManga = isNewManga;
        }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => MangaIds.Empty();

        public override bool IsLongRunning => true;

        public override string CompletionMessage => "Completed";
    }
}
