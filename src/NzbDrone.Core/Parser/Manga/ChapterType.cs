namespace NzbDrone.Core.Parser.Manga
{
    // ChapterType taxonomy per Phase 2 02-CONTEXT.md D-09. Persisted as TEXT in the
    // Chapters table (FluentMigrator .AsString()) with "Regular" as the schema
    // default. Parser/Manga sub-parsers map filename markers (e.g., "Extra",
    // "Side Story", "Oneshot") to these values.
    public enum ChapterType
    {
        Regular,
        Extra,
        Bonus,
        SideStory,
        Oneshot,
        Prologue,
        Epilogue,
        Special
    }
}
