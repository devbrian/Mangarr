namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-02 add-manga orchestrator. Validates → resolves cross-source IDs (D-19..D-22) →
    /// persists via <see cref="IMangaService.AddManga"/> → publishes <see cref="Events.MangaAddedEvent"/>
    /// (via the service) → schedules an initial <see cref="Commands.RefreshMangaCommand"/>.
    /// </summary>
    public interface IAddMangaService
    {
        Manga AddManga(Manga newManga);
    }
}
