using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MyAnimeListStack
{
    // Quick task 260608-vf9 — MyAnimeList Interest-Stack ImportList provider Settings POCO.
    //
    // Non-OAuth public scrape (RESEARCH §Feasibility): a MAL stack page
    // (https://myanimelist.net/stacks/85344) is fully server-rendered public HTML — no
    // tokens, no credentials. The ONLY user-visible field is a single StackUrl textbox that
    // accepts either a full stack URL or a bare numeric stack id; both normalize to the
    // canonical https://myanimelist.net/stacks/{id} in the request generator.
    //
    // T-VF9-02 (SSRF) mitigation by absence: BaseUrl is fixed to the canonical MAL host with
    // NO [FieldDefinition] (mirrors MangaDexImportListSettings BaseUrl-by-absence) — the user
    // cannot redirect the outbound fetch at the host level, and the request generator extracts
    // ONLY the numeric stack id before rebuilding against the fixed host.
    public class MyAnimeListStackImportListSettingsValidator : AbstractValidator<MyAnimeListStackImportListSettings>
    {
        public MyAnimeListStackImportListSettingsValidator()
        {
            RuleFor(c => c.StackUrl).NotEmpty();

            // Cheap shape guard (T-VF9-02): the value MUST yield a numeric stack id — either a
            // bare all-digits id OR a myanimelist.net/stacks/<digits> URL. Anything that does not
            // resolve to a numeric id (an internal/file/alternate-host URL, a non-stack MAL page)
            // is rejected on save before any outbound fetch can be coerced.
            RuleFor(c => c.StackUrl)
                .Must(url => MyAnimeListStackImportListRequestGenerator.ExtractStackId(url) != null)
                .WithMessage("Must be a MyAnimeList stack URL (https://myanimelist.net/stacks/85344) or a bare numeric stack ID.")
                .When(c => !string.IsNullOrWhiteSpace(c.StackUrl));
        }
    }

    public class MyAnimeListStackImportListSettings : ImportListSettingsBase<MyAnimeListStackImportListSettings>
    {
        private static readonly MyAnimeListStackImportListSettingsValidator Validator = new();

        public MyAnimeListStackImportListSettings()
        {
            // Fixed canonical host — MAL has no self-hosted instances. Surfaced via NO
            // [FieldDefinition] (T-VF9-02 mitigation by absence, mirrors MangaDexImportListSettings).
            BaseUrl = "https://myanimelist.net";
        }

        // IImportListSettings contract requirement (abstract on ImportListSettingsBase<TSettings>).
        // Fixed at the myanimelist.net host; not user-editable.
        public override string BaseUrl { get; set; }

        [FieldDefinition(0, Label = "ImportListsMyAnimeListStackUrlLabel", HelpText = "ImportListsMyAnimeListStackUrlHelpText", Type = FieldType.Textbox)]
        public string StackUrl { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
