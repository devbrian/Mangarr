namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 D-13 — resolves the active <see cref="IChapterArchiver"/> for the
    /// configured <c>Config.OutputFormat</c>. Implementation in plan 04-06 reads
    /// <c>IConfigService.OutputFormat</c> and falls back to <c>FormatKey="cbz"</c>
    /// if the configured key has no registered archiver.
    /// </summary>
    public interface IChapterArchiverFactory
    {
        IChapterArchiver Resolve(string formatKey);
    }
}
