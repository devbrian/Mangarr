# ImportLists/MyAnimeListStack (Quick task 260608-vf9)

## Purpose

Imports manga from a **public MyAnimeList Interest-Stack** page
(e.g. `https://myanimelist.net/stacks/85344`). No authentication — the stack pages are
fully server-rendered public HTML. The provider scrapes each "add to list" item card with
regex (no new dependency), projects each card to an `ImportListItemInfo` carrying `MalId`
(+ a best-effort slug `Title`), and feeds it through the Phase 26 substrate's dedup +
exclusion + add-manga cascade. The downstream cross-source resolver
(`ImportListSyncService.ProcessListItems`) promotes the MalId-only rows to full manga via
`MyAnimeListMetadataSource`.

**Distinct from `ImportLists/MyAnimeList/`** — that is the OAuth follows-list importer
against `api.myanimelist.net`; this is an unauthenticated scrape of the `myanimelist.net`
website's stack pages (different host, different rate-limit budget).

**Path**: `src/NzbDrone.Core/ImportLists/MyAnimeListStack/`

## Key Files

| File | Purpose |
|------|---------|
| `MyAnimeListStackImportListSettings.cs` | `ImportListSettingsBase<T>` POCO. Single `StackUrl` field (full URL or bare numeric id). Nested `AbstractValidator` — `NotEmpty` + a shape gate that the value resolves to a numeric stack id (via the shared `ExtractStackId`). `BaseUrl` fixed to `https://myanimelist.net` with no `[FieldDefinition]` (SSRF mitigation by absence). |
| `MyAnimeListStackImportListRequestGenerator.cs` | `IImportListRequestGenerator`. Single-page request (RESEARCH #4 — no pager). Hosts the shared `static ExtractStackId(string)` helper. SSRF (T-VF9-02): NEVER fetches the raw input — extracts the numeric id and rebuilds against the fixed host `https://myanimelist.net/stacks/{id}`. Honest `Mangarr/{version}` UA + dedicated `RateLimitKey = "myanimelist-stack"`. |
| `MyAnimeListStackImportListParser.cs` | `IParseImportListResponse`. Regex-extracts every `ownlist/manga/add?selected_manga_id=<digits>` add-button → one `ImportListItemInfo` per distinct MalId, Title from the matching `/manga/<id>/<Slug>` link (underscores → spaces). Exposes `static CountAnimeItems(string)` (anime add-button counter — the guard signal). |
| `MyAnimeListStackImportList.cs` | `HttpImportListBase<T>` provider. `Name = "MyAnimeList Stack"`, `ListType = MyAnimeListStack`, 24h `MinRefreshInterval`. Overrides `Test()` with the anime-stack guard. |

## Patterns / Conventions

- **Non-OAuth public scrape** — extends `HttpImportListBase` (NOT `OAuthAwareImportListBase`).
  Constructor takes only the base ctor params; no proxy, no repo, no tokens.
- **SSRF id-rebuild (T-VF9-02)** — `ExtractStackId` is the single source of truth (used by
  both the Settings validator and the request generator). The outbound URL is always rebuilt
  against the fixed `myanimelist.net` host; a pasted internal/file/alternate-host URL cannot be
  coerced into an outbound fetch.
- **Anime-stack guard (user's hard requirement)** — `Test()` rejects when `mangaCount == 0 &&
  animeCount >= 1` ("Anime stack, not a Manga stack") and when `mangaCount == 0 && animeCount
  == 0` ("no manga found"). The guard anchors on the `ownlist/(manga|anime)/add` add-buttons,
  NOT raw `/manga/` `/anime/` links, to avoid the sidebar-recommendation false positive
  (RESEARCH line 40).
- **Dedicated SourceKey** `"myanimelist-stack"` — the website pages are a different host/budget
  than the `api.myanimelist.net` OAuth import list; a shared `"myanimelist"` key would
  mis-budget the website fetch.
- **No new NuGet dependency** — pure regex (RESEARCH #1), consistent with the regex-heavy parser
  conventions elsewhere in the codebase.

## Known Limitations

- **Single-page scrape only** (RESEARCH #4) — the test stack renders all items on one page with
  no pager. Extend to pagination if a future large stack reveals a `?page=N` / load-more pattern.

## Cross-References

- **Research**: `.planning/quick/260608-vf9-add-myanimelist-stack-import-list-type/260608-vf9-RESEARCH.md`
- **Sibling vertical (closest non-OAuth analog)**: `src/NzbDrone.Core/ImportLists/MangaDex/MangaDexImportListRequestGenerator.cs` + `MangaDexImportListParser.cs`
- **Substrate base**: `src/NzbDrone.Core/ImportLists/HttpImportListBase.cs` + `ImportListBase.cs`
- **Cross-source resolver**: `ImportLists/ImportListSyncService.ProcessListItems` (promotes MalId-only rows)
- **Metadata lookup**: `src/NzbDrone.Core/MetadataSource/MyAnimeList/`
- **Unit fixture**: `src/NzbDrone.Core.Test/ImportListTests/MyAnimeListStack/MyAnimeListStackImportListFixture.cs`
- **Stack search entry point**: <https://myanimelist.net/stacks/search?type=manga>
