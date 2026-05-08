using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Thrown by <see cref="IProvideMangaInfo.GetMangaInfo(string)"/> implementations when the
    /// upstream metadata source returns 404 (or equivalent "not found" signal) for the given
    /// source ID. Mirrors Mangarr's <c>SeriesNotFoundException</c> shape. Carries the original
    /// source ID so callers can log and surface the missing reference.
    /// </summary>
    public class MangaNotFoundException : NzbDroneException
    {
        public string SourceId { get; }

        public MangaNotFoundException(string sourceId)
            : base($"Manga with source ID {sourceId} was not found")
        {
            SourceId = sourceId;
        }

        public MangaNotFoundException(string sourceId, string message)
            : base(message)
        {
            SourceId = sourceId;
        }
    }
}
