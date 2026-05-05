using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Manga
{
    // Mirrors NzbDrone.Core.Tv.AddSeriesValidator (FluentValidation aggregator) per Phase 8
    // audit gap-single (no-sibling/AddSeriesValidator.md). Composes the path-level rules into
    // one validator the AddMangaService will invoke before persistence.
    //
    // Cluster-05 carry-forward: manga-specific peers are NOT yet wired here because the
    // peer validator classes do not exist on the manga side today. Plans will land them:
    //   - MangaPathValidator       (mirror SeriesPathValidator)        — cluster-05
    //   - MangaAncestorValidator   (mirror SeriesAncestorValidator)    — cluster-05
    //   - MangaTitleSlugValidator  (mirror SeriesTitleSlugValidator)   — cluster-05
    //   - MangaExistsValidator     (mirror SeriesExistsValidator)      — cluster-05
    // Once those land, add the SetValidator(...) calls + the TitleSlug RuleFor below to
    // restore full TV parity. Until then this validator wires only the shared RootFolderValidator
    // and the path-shape IsValidPath() check, both of which are domain-agnostic and exist today.
    //
    // AddMangaService consumer wiring is deferred to Plan 03-07 (gap-04).
    public interface IAddMangaValidator
    {
        ValidationResult Validate(Manga instance);
    }

    public class AddMangaValidator : AbstractValidator<Manga>, IAddMangaValidator
    {
        public AddMangaValidator(RootFolderValidator rootFolderValidator)
        {
            RuleFor(c => c.Path).Cascade(CascadeMode.Stop)
                .IsValidPath()
                .SetValidator(rootFolderValidator);

            // Cluster-05 will add:
            //     .SetValidator(mangaPathValidator)
            //     .SetValidator(mangaAncestorValidator);
            // and:
            //     RuleFor(c => c.TitleSlug).SetValidator(mangaTitleSlugValidator);
            // (TitleSlug is also a Phase 8 manga-domain gap; see Series-vs-Manga.md gap-04.)
        }
    }
}
