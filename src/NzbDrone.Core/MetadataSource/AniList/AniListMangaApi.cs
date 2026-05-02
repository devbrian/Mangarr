using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MetadataSource.AniList
{
    /// <summary>
    /// Static GraphQL query strings for the AniList manga metadata source per D-25.
    /// NEW file in Phase 2 — does NOT modify the existing anime ImportList GraphQL transport at
    /// <c>src/NzbDrone.Core/ImportLists/AniList/AniListAPI.cs</c> (which queries
    /// <c>type: ANIME</c> + <c>media.episodes</c>). The two siblings may share a transport in
    /// Phase 7+ once v2 manga ImportLists land (REQUIREMENTS IMP-02).
    ///
    /// Query design (per D-25 + 02-RESEARCH §Code Examples Pattern 3):
    ///   - <c>type: MANGA</c> on every Media/media call (NOT <c>ANIME</c>)
    ///   - All four title variants (<c>userPreferred / romaji / native / english</c>) requested
    ///     because the cross-source resolver (Plan 02-09) needs every alt-title for the
    ///     Jaro-Winkler max-similarity computation.
    ///   - <c>media.chapters</c> for the total-chapter-count axis (D-21 multi-axis confirm)
    ///   - <c>idMal</c> requested for the cross-source reverse-lookup path (D-19)
    ///   - <c>staff(perPage: 25){ edges { role node { name { full native } } } }</c> for the
    ///     primary-author axis (D-21) — the resolver picks the edge with <c>role == "Story"</c>.
    ///
    /// Anti-injection invariant (T-INJ-03): query strings are <c>const string</c> literals; ALL
    /// user-supplied values flow through the <c>variables</c> object via
    /// <see cref="BuildBody(string, object)"/>. No string-template interpolation anywhere.
    /// </summary>
    public static class AniListMangaApi
    {
        public const string GraphQlEndpoint = "https://graphql.anilist.co";

        // Per D-25 + RESEARCH §Code Examples Pattern 3: type:MANGA + all four title variants
        // + staff(perPage:25){edges{role node{name{full native}}}} for D-21 primary-author axis.
        public const string MediaByIdQuery = @"
            query ($id: Int) {
                Media(id: $id, type: MANGA) {
                    id
                    idMal
                    title {
                        userPreferred
                        romaji
                        native
                        english
                    }
                    synonyms
                    status
                    chapters
                    volumes
                    startDate { year month day }
                    endDate   { year month day }
                    description(asHtml: false)
                    coverImage { extraLarge large medium }
                    bannerImage
                    averageScore
                    meanScore
                    isAdult
                    genres
                    tags { name rank }
                    staff(perPage: 25) {
                        edges {
                            role
                            node { name { full native } }
                        }
                    }
                    externalLinks { site url }
                }
            }
        ";

        public const string MediaSearchQuery = @"
            query ($search: String, $page: Int) {
                Page(page: $page, perPage: 25) {
                    pageInfo { currentPage lastPage hasNextPage }
                    media(search: $search, type: MANGA) {
                        id
                        idMal
                        title { userPreferred romaji native english }
                        synonyms
                        status
                        chapters
                        startDate { year }
                        coverImage { large }
                        staff(perPage: 5) { edges { role node { name { full } } } }
                    }
                }
            }
        ";

        public const string MediaByIdMalQuery = @"
            query ($idMal: Int) {
                Media(idMal: $idMal, type: MANGA) {
                    id
                    idMal
                    title { userPreferred romaji native english }
                    synonyms
                    status
                    chapters
                    startDate { year }
                    coverImage { large }
                    staff(perPage: 5) { edges { role node { name { full } } } }
                }
            }
        ";

        public static string BuildBody(string query, object variables)
            => Json.ToJson(new { query, variables });
    }
}
