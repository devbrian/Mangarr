namespace NzbDrone.Core.CustomFormats
{
    public class ReleaseGroupSpecification : RegexSpecificationBase
    {
        public override int Order => 9;
        public override string ImplementationName => "Release Group";
        public override string InfoLink => "https://wiki.servarr.com/sonarr/settings#custom-formats-2";

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — input.EpisodeInfo
        // stripped (TV ParsedEpisodeInfo DELETED); ParsedChapterInfo has no ReleaseGroup field.
        // This spec is dead-bound post-trim; AppliesTo (when set in base) hides from manga UI.
        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return false;
        }
    }
}
