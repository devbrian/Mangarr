# NzbDrone.Core/DecisionEngine

## Purpose

The **decision engine** decides whether a release should be downloaded. It is implemented as a chain of independent **specifications** (Specification pattern), each of which inspects a `RemoteEpisode` and either approves or rejects it. The final decision aggregates all specs.

This pattern is reused throughout the codebase (CustomFormat specs, Import specs, AutoTag specs).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\DecisionEngine\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `IMakeDownloadDecision.cs` / `DownloadDecisionMaker.cs` | Orchestrator. Public methods `GetRssDecision(reports, pushedRelease)` and `GetSearchDecision(reports, criteria)`. |
| `DownloadDecision.cs` | Result wrapper: `RemoteEpisode` + `IList<Rejection>` + `IList<DownloadSpecDecision>` |
| `DownloadSpecDecision.cs` | Single-spec result. Static factories: `Accept()`, `Reject(reason, message)`. |
| `DownloadDecisionComparer.cs` | `IComparer<DownloadDecision>` — ranks approved decisions for selection (quality, custom-format score, age, peers, size). |
| `Rejection.cs` | Captured rejection (message + type) |
| `RejectionType.cs` | enum `Permanent` / `Temporary` |
| `DownloadRejectionReason.cs` | enum of typed reasons |
| `IDownloadDecisionEngineSpecification.cs` | Spec contract |
| `SpecificationPriority.cs` | enum `Default` / `Disk` / `Database` (controls run order) |

## Specifications/

The directory contains ~32 specifications. Each is a self-contained class implementing `IDownloadDecisionEngineSpecification`:

```csharp
public interface IDownloadDecisionEngineSpecification
{
    SpecificationPriority Priority { get; }
    RejectionType Type { get; }
    DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria);
}
```

### Common Specifications (alphabetical)

| Spec | Permanent? | Description |
|------|-----------|-------------|
| `AcceptableSizeSpecification` | Permanent | Quality definition min/max size check |
| `AnimeVersionUpgradeSpecification` | Permanent | Anime v2/v3 release upgrade |
| `BlocklistSpecification` | Permanent | Recently blocklisted? |
| `CustomFormatAllowedByProfileSpecification` | Permanent | Custom format meets profile minimum |
| `CutoffSpecification` | Permanent | Quality already meets cutoff (no upgrade needed) |
| `DelaySpecification` | Temporary | Wait for delay profile / preferred protocol |
| `EpisodeRequestedSpecification` | Permanent | Was this episode part of the search? |
| `FullSeasonReleaseSpecification` | Permanent | Don't grab season pack if singles already grabbed |
| `HistorySpecification` | Permanent | Recent failed grab from same indexer? |
| `LanguageSpecification` | Permanent | Language matches profile |
| `MaximumSizeSpecification` | Permanent | Hard size cap |
| `MinimumAgeSpecification` | Temporary | Release is at least N hours old (avoid uploads still propagating) |
| `MonitoredEpisodeSpecification` | Permanent | Episode (and series) is monitored |
| `OnlyNewEpisodeSpecification` | Permanent | RSS — only newer episodes |
| `ProperSpecification` | Permanent | Allow propers/repacks |
| `QualityAllowedByProfileSpecification` | Permanent | Quality permitted by profile |
| `RawDiskSpecification` | Permanent | Reject torrents with raw disk extensions |
| `RepackSpecification` | Permanent | Repack handling |
| `SameEpisodesGrabSpecification` | Temporary | Same episodes already in queue |
| `SeasonPackOnlySpecification` | Permanent | Some indexers configured for packs only |
| `SeriesSpecification` | Permanent | Sanity: series matched |
| `SingleEpisodeSearchMatchSpecification` | Permanent | Search-time: matches requested episode |
| `TorrentSeedingSpecification` | Permanent | Min seeders requirement |
| `UpgradableSpecification` | Permanent | Compare proposed vs existing (Quality + Languages + CustomFormatScore) |
| `UpgradeAllowedSpecification` | Permanent | Profile permits upgrades |
| `UpgradeDiskSpecification` | Permanent | Disk version is upgrade-target |

### Subdirectories
- `Specifications/Search/` — Specs that only run during user-initiated search (`SearchCriteria` is not null)
- `Specifications/RssSync/` — Specs that only run during scheduled RSS sync

## Run Order

```
DownloadDecisionMaker.GetRssDecision(reports)
    │
    │ for each ReleaseInfo report:
    │   1. Parse title via ParsingService.Map → RemoteEpisode (or skip if unparseable)
    │   2. Run Specifications in order (Database first, then Disk, then Default)
    │   3. Collect all Rejections
    │
    └─ Returns List<DownloadDecision>
       │  - Approved if Rejections is empty
       │  - else Rejected with all reasons captured
```

Approved decisions are then sorted by `DownloadDecisionComparer` (best first) and the top one is sent to the download client.

## DownloadDecisionComparer (Ranking)

Approved decisions are ranked by (in order of importance):
1. Custom format score (higher = better)
2. Preferred protocol (per delay profile)
3. Quality (using QualityProfile order)
4. Languages (matching profile order)
5. Indexer priority
6. Peers (torrents) — more is better
7. Age — newer slightly preferred
8. Size — closer to optimal size in QualityDefinition

## Adding a New Specification

1. Create `Specifications/MyNewSpec.cs` implementing `IDownloadDecisionEngineSpecification`.
2. Set `Priority` (Default unless it does disk or DB I/O — use Disk/Database to run later).
3. Set `Type` (`Permanent` if always invalid, `Temporary` if might pass on retry).
4. Return `DownloadSpecDecision.Accept()` or `Reject(DownloadRejectionReason.X, "user-facing message")`.
5. **No DI registration needed** — auto-discovered by reflection.
6. Add unit tests in `src/NzbDrone.Core.Test/DecisionEngineTests/`.

Example:

```csharp
public class MyNewSpec : IDownloadDecisionEngineSpecification
{
    public SpecificationPriority Priority => SpecificationPriority.Default;
    public RejectionType Type => RejectionType.Permanent;

    public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
    {
        if (someBadCondition(subject))
            return DownloadSpecDecision.Reject(
                DownloadRejectionReason.NotMonitored,
                $"Reason for {subject.ParsedEpisodeInfo.ReleaseTitle}");

        return DownloadSpecDecision.Accept();
    }
}
```

## Manga Adaptation Notes

The decision-engine **architecture is fully reusable** — but several specs are TV/anime-aware:

| Spec | Migration Action |
|------|------------------|
| `AnimeVersionUpgradeSpecification` | Generalize to "version upgrade" (manga has v2 scans too) |
| `FullSeasonReleaseSpecification` | Rename to `FullVolumeReleaseSpecification` once volumes exist |
| `OnlyNewEpisodeSpecification` | Rename to `OnlyNewChapterSpecification` |
| `EpisodeRequestedSpecification` | Rename to `ChapterRequestedSpecification` |
| `SameEpisodesGrabSpecification` | Rename to `SameChaptersGrabSpecification` |
| `SeasonPackOnlySpecification` | Rename to `VolumePackOnlySpecification` |
| `SingleEpisodeSearchMatchSpecification` | Rename to `SingleChapterSearchMatchSpecification` |
| `MonitoredEpisodeSpecification` | Adapt to monitored chapter concept |

Other specs (Blocklist, Cutoff, Quality, Language, History, MinimumAge, AcceptableSize, Upgradable, CustomFormat, TorrentSeeding) **transfer cleanly**.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Produces `RemoteEpisode`
- [../Profiles/CLAUDE.md](../Profiles/CLAUDE.md) — Quality/Delay profiles consumed
- [../CustomFormats/CLAUDE.md](../CustomFormats/CLAUDE.md) — Same spec pattern, different scope
- [../MediaFiles/CLAUDE.md](../MediaFiles/CLAUDE.md) — Import has its own specs
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Approved decisions sent to download clients
