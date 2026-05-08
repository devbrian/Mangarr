namespace NzbDrone.Core.CustomFormats
{
    public class ReleaseTitleSpecification : RegexSpecificationBase
    {
        public override int Order => 1;
        public override string ImplementationName => "Release Title";
        public override string InfoLink => "https://wiki.servarr.com/sonarr/settings#custom-formats-2";

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — input.EpisodeInfo
        // (TV ParsedEpisodeInfo) DELETED. Manga peer reads ParsedChapterInfo.ReleaseTitle.
        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            if (input is MangaCustomFormatInput mangaInput && mangaInput.ChapterInfo != null)
            {
                return MatchString(mangaInput.ChapterInfo.ReleaseTitle) || MatchString(input.Filename);
            }

            return MatchString(input.Filename);
        }
    }
}
