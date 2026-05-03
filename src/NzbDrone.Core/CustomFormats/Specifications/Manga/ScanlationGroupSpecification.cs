namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: NEW regex spec per Phase 5 D-09 — see DIVERGENCE.md.
    // Distinct from ReleaseGroupSpecification (TV — reads ParsedEpisodeInfo.ReleaseGroup).
    // Manga authors care about scanlation groups; ScanlationGroup is indexer-supplied per
    // Phase 3 D-Q4 (MangaDex, comix.to). Inherits Regex.Compiled | IgnoreCase + null-safe
    // MatchString from RegexSpecificationBase (T-05-19 mitigation surface lives there).
    // Phase 8 collapse: drops AppliesTo discriminator when Tv/ deletes.
    public class ScanlationGroupSpecification : RegexSpecificationBase
    {
        public override int Order => 12;
        public override string ImplementationName => "Scanlation Group";
        public override MediaType AppliesTo => MediaType.Manga;     // Phase 5 D-10

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            // Pitfall 4 cast — sibling MangaCustomFormatInput per Phase 5 D-09.
            if (input is not MangaCustomFormatInput mangaInput)
            {
                return false;
            }

            return MatchString(mangaInput.Release?.ScanlationGroup);
        }
    }
}
