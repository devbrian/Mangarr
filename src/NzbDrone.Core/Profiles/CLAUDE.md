# NzbDrone.Core/Profiles

## Purpose

User-configurable **profiles** that control which releases are accepted, when to wait for better, and what terms to prefer. Four profile types live here (Sonarr's `Qualities/` quality-profile vertical was dropped in Phase 5 D-04 — the manga peer is `TranslationProfile` (ordinal language preference) + `CustomFormatProfile` (CF scoring)):

- **Translation profiles** (`Translations/`) — ordered translation-language preference (the manga quality peer)
- **Custom Format profiles** (`CustomFormats/`) — min/max CF score thresholds + per-CF score overrides
- **Delay profiles** (`Delay/`) — how long to wait for a preferred release before grabbing the available one
- **Release profiles** (`Releases/`) — preferred / required / ignored term lists

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Profiles\`

## Subdirectories

### `Translations/` — Translation Profiles (manga quality peer)

The ordinal language-preference gate. See [Translations/CLAUDE.md](./Translations/CLAUDE.md). Entity: `TranslationProfile.cs` (Name + `Languages : List<string>` BCP-47 ordered + `AllowLanguagesNotInProfile`) + Service/Repository/InUseException/UpdatedEvent.

### `CustomFormats/` — Custom Format Profiles

The CF-scoring layer. See [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md). Entity: `CustomFormatProfile.cs` (Name + `MinFormatScore` + nullable `MaxFormatScore` + `FormatItems`) + Service/Repository/InUseException/UpdatedEvent.

### `Delay/` — Delay Profiles

| File | Purpose |
|------|---------|
| `DelayProfile.cs` | Per-tag profile. Fields: `PreferredProtocol`, `HttpDelay`, `Order`, `BypassIfHighestQuality`, `BypassIfAboveCustomFormatScore`, `MinimumCustomFormatScore`, `Tags`. Phase 26 Plan 26-03 (DP-01 + DP-02) dropped `EnableUsenet`/`EnableTorrent`/`UsenetDelay`/`TorrentDelay` atomic with Migration 003 — only `Http=3` and `Unknown=0` remain on `DownloadProtocol` post Phase 15 D-18, so `HttpDelay` is the only live protocol-delay column. |
| `DelayProfileService.cs` | CRUD |
| `DelayProfileRepository.cs` | Dapper repo |

### `Releases/` — Release Profiles

| File | Purpose |
|------|---------|
| `ReleaseProfile.cs` | Per-tag preferred/required/ignored term list with weights |
| `ReleaseProfileService.cs` | CRUD |
| `ReleaseProfileRepository.cs` | Dapper repo |

## Delay Profile Logic

- Indexer returns a release
- Compute whether this release meets the translation/CF cutoff or above
- If yes, grab immediately
- Otherwise, hold (in `PendingReleases`) for `HttpDelay` minutes (Phase 26 Plan 26-03: `UsenetDelay`/`TorrentDelay` columns dropped — `Http` is the only live protocol post Phase 15 D-18)
- After delay, re-evaluate; grab the best available
- `BypassIfHighestQuality` — skip delay if release is the maximum-allowed tier
- `BypassIfAboveCustomFormatScore` — skip delay if score exceeds threshold

## Release Profile Term Logic

Per profile (matched to series via tags):
- **Required**: Release must contain ≥1 required term (regex)
- **Ignored**: Release must contain 0 ignored terms (regex)
- **Preferred**: Each preferred term has a weight; matching adds to score (passed to DecisionEngine)

Used by `RequiredTermsSpecification`, `IgnoredTermsSpecification` and similar. (The TV-region CF language spec previously referenced here was deleted Phase 32 — CORR-04; manga-canonical language authority lives at `LanguageInTranslationProfileSpecification` under `DecisionEngine/Manga/Specifications/`.)

## Default Profiles

A new install gets default Translation, Custom Format, Delay, and Release profiles seeded via each service's `IHandle<ApplicationStartedEvent>` handler (`TranslationProfileService` / `CustomFormatProfileService` / etc.).

## Manga Adaptation Notes

The TV quality-profile vertical is gone (Phase 5 D-04). Release preference is now a two-layer model:

- **TranslationProfile** (outer gate) — ordered BCP-47 language list; index = preference rank; `AllowLanguagesNotInProfile` fallback control. See [Translations/CLAUDE.md](./Translations/CLAUDE.md).
- **CustomFormatProfile** (inner scoring) — min/max CF score thresholds + per-CF `FormatItems` overrides. See [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md).

Delay and Release profiles are reused from Sonarr largely as-is (manga term lists prefer "Color", "v2", "HD"; ignore "Sample", "Preview", "Raw").

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [./Translations/CLAUDE.md](./Translations/CLAUDE.md) — TranslationProfile (manga quality peer)
- [./CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md) — CustomFormatProfile (CF scoring)
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Profiles consumed by specs
- [../CustomFormats/CLAUDE.md](../CustomFormats/CLAUDE.md) — Custom format rules scored by CustomFormatProfile
- [../Tags/](../Tags/) — Tags used to scope profiles to specific manga
