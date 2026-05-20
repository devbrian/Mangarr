# NzbDrone.Core/Profiles

## Purpose

User-configurable **profiles** that control which releases are accepted, when to wait for better, and what terms to prefer. Three profile types live here:

- **Quality profiles** — which qualities are allowed, cutoff (target), upgradability, custom-format minimums
- **Delay profiles** — how long to wait for a preferred protocol/quality before grabbing the available one
- **Release profiles** — preferred / required / ignored term lists

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Profiles\`

## Subdirectories

### `Qualities/` — Quality Profiles

| File | Purpose |
|------|---------|
| `QualityProfile.cs` | The profile entity. Has `Id`, `Name`, `Cutoff`, `Items` (ordered list of allowed qualities/groups), `MinFormatScore`, `CutoffFormatScore`, `MinUpgradeFormatScore`, `FormatItems` |
| `QualityProfileItem.cs` | Embedded — a quality (or group of qualities) and its enabled/disabled flag |
| `QualityProfileQualityItem.cs` | Embedded — individual quality reference |
| `QualityProfileFormatItem.cs` | Embedded — custom format reference + score |
| `QualityProfileService.cs` | CRUD + default-profile creation |
| `QualityProfileRepository.cs` | Dapper repo |

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

## Quality Profile Anatomy

```csharp
public class QualityProfile : ModelBase
{
    public string Name { get; set; }
    public int Cutoff { get; set; }                                 // target quality ID
    public int MinFormatScore { get; set; }                         // minimum custom format score
    public int CutoffFormatScore { get; set; }                      // score to consider "met"
    public int MinUpgradeFormatScore { get; set; }                  // minimum gain to upgrade
    public List<QualityProfileQualityItem> Items { get; set; }      // allowed qualities (ordered)
    public List<ProfileFormatItem> FormatItems { get; set; }        // custom format scores
    public bool UpgradeAllowed { get; set; }
    public Language Language { get; set; }
    public List<int> Tags { get; set; }
}
```

## Delay Profile Logic

- Indexer protocol returns a release
- Compute "is this release `qualityProfile.Cutoff`-met or above"
- If yes, grab immediately
- Otherwise, hold (in `PendingReleases`) for `HttpDelay` minutes (Phase 26 Plan 26-03: `UsenetDelay`/`TorrentDelay` columns dropped — `Http` is the only live protocol post Phase 15 D-18)
- After delay, re-evaluate; grab the best available
- `BypassIfHighestQuality` — skip delay if release is the maximum-allowed quality
- `BypassIfAboveCustomFormatScore` — skip delay if score exceeds threshold

## Release Profile Term Logic

Per profile (matched to series via tags):
- **Required**: Release must contain ≥1 required term (regex)
- **Ignored**: Release must contain 0 ignored terms (regex)
- **Preferred**: Each preferred term has a weight; matching adds to score (passed to DecisionEngine)

Used by `LanguageSpecification`, `RequiredTermsSpecification`, `IgnoredTermsSpecification` and similar.

## Default Profiles

A new install gets default Quality, Delay, and Release profiles seeded via `QualityProfileService.OnApplicationStartedHandler`. The Mangarr team should override these defaults for manga.

## Manga Adaptation Notes

### Quality Profile
Architecture transfers cleanly. The **`Items` list** changes content (manga quality tiers vs TV resolutions). Quality enum lives in `../Qualities/Quality.cs` and is the right place to start.

Recommended manga quality tiers (ordered):
1. `Raw` — untranslated source
2. `Sample` — preview pages
3. `Translated_LQ` — low-quality scanlation
4. `Translated_HQ` — high-quality scanlation
5. `Official_HQ` — official translation, high res
6. `Official_PRO` — publisher-direct (e.g., Manga Plus)

### Delay Profile
Reusable as-is. The protocols (`Usenet`/`Torrent`) extend naturally if a `Direct` (image scraper) protocol is added.

### Release Profile
Reusable as-is. Term lists for manga: prefer "Color", "v2", "HD"; ignore "Sample", "Preview", "Raw"; etc.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Qualities/](../Qualities/) — Quality enum / definitions consumed here
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Profiles consumed by specs
- [../CustomFormats/CLAUDE.md](../CustomFormats/CLAUDE.md) — Format scores attached to profiles
- [../Tags/](../Tags/) — Tags used to scope profiles to specific series
