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
