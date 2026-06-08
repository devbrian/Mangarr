namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: NEW enum per Phase 5 D-10 (discriminator for ICustomFormatSpecification.AppliesTo).
    // See DIVERGENCE.md.
    // quick-260608-gmm: the Series value (1) was removed — the custom-format subsystem is manga-only.
    // All remains for reusable specs (ReleaseTitle/ReleaseGroup/IndexerFlag/Size). Numeric values are
    // NOT renumbered (AppliesTo is computed at runtime, not persisted, so renumbering Manga is needless churn).
    public enum MediaType
    {
        All = 0,        // reusable specs (ReleaseTitle, ReleaseGroup, IndexerFlag, Size)
        Manga = 2       // manga-only (TranslatedLanguage, ScanlationGroup, SourceKey, ChapterType)
    }
}
