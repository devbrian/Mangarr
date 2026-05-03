namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: NEW regex spec per Phase 5 D-09 — see DIVERGENCE.md.
    // Reads MangaCustomFormatInput.SourceKey (Phase 3 D-17 — `mangadex` / `comix.to` /
    // future v2 sources). Lets users score "always prefer MangaDex over comix.to" as
    // +50/-50 CFs without using opaque IndexerFlag tricks. Inherits Regex.Compiled |
    // IgnoreCase + null-safe MatchString from RegexSpecificationBase.
    // Phase 8 collapse: drops AppliesTo discriminator when Tv/ deletes.
    public class SourceKeySpecification : RegexSpecificationBase
    {
        public override int Order => 14;
        public override string ImplementationName => "Source Key";
        public override MediaType AppliesTo => MediaType.Manga;     // Phase 5 D-10

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            // Pitfall 4 cast — sibling MangaCustomFormatInput per Phase 5 D-09.
            if (input is not MangaCustomFormatInput mangaInput)
            {
                return false;
            }

            return MatchString(mangaInput.SourceKey);
        }
    }
}
