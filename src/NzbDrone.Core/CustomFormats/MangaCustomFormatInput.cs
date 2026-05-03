using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: NEW sibling input class for manga CF specs (Pitfall 4 mitigation per
    // Phase 5 RESEARCH.md Open Question 1 → researcher recommends sibling). Each manga CF spec's
    // IsSatisfiedByWithoutNegate(CustomFormatInput input) casts `input is not MangaCustomFormatInput → return false`.
    // See DIVERGENCE.md per Phase 5 D-09.
    //
    // Derives from CustomFormatInput so the existing ICustomFormatSpecification.IsSatisfiedBy(CustomFormatInput input)
    // entry-point accepts a MangaCustomFormatInput without requiring sibling-overload bookkeeping in the
    // spec dispatcher. Manga specs in Wave 2 use the cast-or-return-false guard:
    //
    //     if (input is not MangaCustomFormatInput mangaInput) return false;
    //
    // Inherits Size, IndexerFlags, Languages, Filename, ReleaseType from the base; adds the
    // manga-specific surface (ChapterInfo, Manga, Release, SourceKey) that Wave 2 specs read.
    // Phase 8 cleanup: collapse with CustomFormatInput when Tv/ deletes.
    public class MangaCustomFormatInput : CustomFormatInput
    {
        public ParsedChapterInfo ChapterInfo { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public ReleaseInfo Release { get; set; }            // carries TranslatedLanguage + ScanlationGroup + IndexerPriority
        public string SourceKey { get; set; }               // Phase 3 D-17 indexer key
    }
}
