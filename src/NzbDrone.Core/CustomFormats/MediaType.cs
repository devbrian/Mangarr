namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: NEW enum per Phase 5 D-10 (discriminator for ICustomFormatSpecification.AppliesTo).
    // See DIVERGENCE.md.
    // Phase 8 cleanup: drop entirely when TV specs delete (only manga remains).
    public enum MediaType
    {
        All = 0,        // reusable specs (ReleaseTitle, ReleaseGroup, IndexerFlag, Size)
        Series = 1,     // TV-only (Resolution, Source, ReleaseType, Language)
        Manga = 2       // manga-only (TranslatedLanguage, ScanlationGroup, SourceKey, ChapterType)
    }
}
