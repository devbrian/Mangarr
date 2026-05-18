namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: Phase 24 v1.1 introduces a peer ENUM mirror to the existing
    // string-constant static class MangaStatusType. Required for StatusSpecification
    // (FieldType.Select needs an enum-typed SelectOptions; the original
    // MangaStatusType is a static class of string constants -- can't be used as an
    // enum SelectOptions backing). Values match the strings in MangaStatusType
    // (Ongoing/Completed/Hiatus/Cancelled). The spec's IsSatisfiedByWithoutNegate
    // maps the stored Manga.Status string -> enum at evaluation time.
    //
    // Manga.Status itself stays as a STRING on the entity (already-shipped per
    // Phase 6) -- this enum is FE-facing only for the AutoTagging spec dropdown.
    // Numeric values start at 1 so accidental int defaults are non-meaningful.
    //
    // Pitfall 1 reshape (see 24-RESEARCH.md): the Sonarr-canonical
    // `series.Status == (SeriesStatusType)Status` body cannot compile against
    // Manga.Status (string). Option A (chosen) = sentinel enum mirror.
    public enum MangaStatus
    {
        Ongoing = 1,
        Completed = 2,
        Hiatus = 3,
        Cancelled = 4
    }
}
