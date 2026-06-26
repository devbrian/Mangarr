# NzbDrone.Core/DecisionEngine

## Purpose

The **decision engine** decides whether a release should be downloaded, as a chain of independent
**specifications** (Specification pattern) that each approve or reject a candidate; the final decision
aggregates all specs.

At HEAD this directory holds only the **shared decision/rejection infrastructure**. The live manga
pipeline — orchestrator (`MangaDownloadDecisionMaker`), comparer (`MangaDownloadDecisionComparer`),
DTO (`MangaDownloadDecision`), spec interface (`IMangaDecisionEngineSpecification`), and the 15-spec
set — lives under [`Manga/`](./Manga/CLAUDE.md). The Sonarr TV orchestrator (`DownloadDecisionMaker`,
`DownloadDecision`, `IDownloadDecisionEngineSpecification`) and its `Specifications/` catalog (`Monitored*`,
`Quality*`, `Upgradable`, `SeasonPack*`, `Anime*`, etc.) were deleted in the Phase 15 `Tv/` cutover.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\DecisionEngine\`

## Top-Level Files (shared infra — verified at HEAD)

| File | Purpose |
|------|---------|
| `DownloadSpecDecision.cs` | Single-spec result. Static factories `Accept()` / `Reject(reason, message, args)`. |
| `DownloadRejection.cs` / `Rejection.cs` | Captured rejection (reason + type). |
| `DownloadRejectionReason.cs` / `RejectionType.cs` (`Permanent` / `Temporary`) / `UpgradeableRejectReason.cs` | Typed reason + rejection-type enums. |
| `SpecificationPriority.cs` | enum controlling spec run order (Database-priority specs short-circuit first). |
| `IRejectWithReason.cs` | Reason-carrier contract. |
| `ReleaseDecisionInformation.cs` | Decision-context carrier. |

## Subdirectory

### `Manga/` — the live decision pipeline
`IMangaDecisionEngineSpecification` (takes `RemoteChapter`), `MangaDownloadDecisionMaker` (priority-grouped
auto-discovered spec exec + CF augmentation), `MangaDownloadDecisionComparer` (D-08 ordering), and the
`Specifications/` set (15 specs). See [Manga/CLAUDE.md](./Manga/CLAUDE.md).

## Adding a New (manga) Specification

1. Create `Manga/Specifications/MyNewSpec.cs` implementing `IMangaDecisionEngineSpecification` (takes `RemoteChapter`).
2. Set `Priority` (Default unless it does disk/DB I/O — use Disk/Database to run later) and `Type` (`Permanent` / `Temporary`).
3. Return `DownloadSpecDecision.Accept()` or `Reject(DownloadRejectionReason.X, "user-facing message")`.
4. **No DI registration needed** — auto-discovered via `IEnumerable<IMangaDecisionEngineSpecification>` ctor injection.
5. Add unit tests in `src/NzbDrone.Core.Test/DecisionEngineTests/`. (Pitfall 6: implement `IMangaDecisionEngineSpecification` ONLY — never the deleted TV interface.)

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [./Manga/CLAUDE.md](./Manga/CLAUDE.md) — the live manga decision pipeline + 15-spec set
- [../Parser/Manga/CLAUDE.md](../Parser/Manga/CLAUDE.md) — produces `RemoteChapter`
- [../Profiles/CLAUDE.md](../Profiles/CLAUDE.md) — TranslationProfile / CustomFormatProfile consumed
- [../CustomFormats/CLAUDE.md](../CustomFormats/CLAUDE.md) — same spec pattern, different scope
- [../MediaFiles/MangaImport/CLAUDE.md](../MediaFiles/MangaImport/CLAUDE.md) — import has its own spec set
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — approved decisions sent to the gateway download client
