# DecisionEngine/Manga

## Purpose
Parallel manga decision-engine pipeline per Phase 5 D-05 — sibling to the TV `DecisionEngine/` orchestrator. Contains `IMangaDecisionEngineSpecification`, `MangaDownloadDecisionMaker`, `MangaDownloadDecisionComparer`, `MangaDownloadDecision` DTO, plus the 15-spec auto-discovered set under `Specifications/` (Phase 5 D-06 shipped 11; Phase 8 backfilled DeletedChapterFile + Manga + SingleChapterSearchMatch; debug `rss-regrab-existing-chapter` added UpgradeDisk).


## Key Files
| File | Purpose |
|------|---------|
| `IMangaDecisionEngineSpecification.cs` | Manga-side spec interface taking `RemoteChapter` (compile-error-driven additive contract) |
| `IMakeMangaDownloadDecision.cs` | Orchestrator interface (Phase 6 dispatchers consume) |
| `MangaDownloadDecision.cs` | DTO with `RemoteChapter` + `Rejections` |
| `MangaDownloadDecisionMaker.cs` | IMakeMangaDownloadDecision impl — priority-grouped spec exec + CF augmentation |
| `MangaDownloadDecisionComparer.cs` | IComparer<MangaDownloadDecision> with D-08 ordering |
| `Specifications/` | 15 auto-discovered specs (5 core + 1 language + 1 CF + 4 operational + 4 backfilled: DeletedChapterFile, Manga, SingleChapterSearchMatch, **UpgradeDisk**). `UpgradeDiskSpecification` (debug `rss-regrab-existing-chapter`, 2026-06-15) is the `Priority=Disk` decision-side peer of import-side `UpgradeSpecification` — rejects re-grabbing an already-imported chapter (`DiskUpgradesNotAllowed` / `DiskNotUpgrade`), closing the gap left by the Phase 5 D-04 quality-model drop. |

## Patterns / Conventions
- **Compile-error-driven additive contract** (Phase 3 LEARNINGS pattern): `IMangaDecisionEngineSpecification(RemoteChapter)` is a NEW interface, not a method on `IDownloadDecisionEngineSpecification`. TV specs cannot accidentally fire on manga.
- **DryIoc IEnumerable<TPlugin> auto-discovery** (Phase 4 LEARNINGS pattern S1): `MangaDownloadDecisionMaker(IEnumerable<IMangaDecisionEngineSpecification>)` constructor auto-resolves all impls — no manual DI registration.
- **Priority-grouped spec execution** (mirrors `DownloadDecisionMaker.cs:180-197` verbatim): `_specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key)` — Database-priority specs short-circuit before Default-priority specs.
- **Pitfall 6 mitigation**: manga specs implement `IMangaDecisionEngineSpecification` ONLY. Plan acceptance grep checks for accidental dual-interface impls.
- **D-08 comparer ordering**: Language rank → CF score → Indexer priority → **Votes** → Age → Size. Indexer priority promoted above age/size per user direction (source-stability matters more than freshness at human-library scale). Votes (gateway per-release vote count, plumbed quick-260607-bnf) is a tiebreaker after indexer priority and before age — higher votes wins (quick-260607-cto, user direction). The comparer follows the **OrderByDescending** convention ("better" → higher compare value); `ProcessMangaDownloadDecisions` consumes it via `OrderByDescending(d => d, _comparer)` and grabs the first acceptable candidate per chapter. **This was `OrderBy` (ascending) until quick-260607-cto** — that consumed the descending-convention comparer backwards, silently grabbing the WORST qualified candidate among multi-source same-chapter results; the fix realigned the consumer and `ProcessMangaDownloadDecisionsFixture` now guards the live direction.

## Manga Adaptation Notes
This is a NEW manga-side directory mirroring `DecisionEngine/` (TV). Mangarr's TV spec set takes `RemoteEpisode`; manga specs take `RemoteChapter`. The 15-spec set covers all release-evaluation concerns:
- 5 core gates: Monitored Manga / Chapter, ChapterRequested, AlreadyImportedChapter, Blocklist
- 1 language gate (NEW — no TV analog): LanguageInTranslationProfile (TPROFILE outer enforcer per cf-only-walkthrough.md verdict)
- 1 CF gate: CustomFormatMinimumScore (CF inner enforcer — reads MinFormatScore + MaxFormatScore from CustomFormatProfile)
- 4 operational gates: MinimumAge, AcceptableSize, MaximumSize, QueueDuplicate
- 4 backfilled (post-D-06): DeletedChapterFile, Manga, SingleChapterSearchMatch, and UpgradeDisk (disk-aware reject — decision-side peer of import-side UpgradeSpecification; stops re-grabbing an already-imported chapter)

## Phase 8 Collapse
When `Tv/` deletes in Phase 8, this directory collapses into the canonical `DecisionEngine/` namespace. The dual-pipeline structure exists ONLY to keep TV decision logic isolated from manga during the transition.

## Cross-References
- [Phase 5 CONTEXT](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-05, D-06, D-08
- [Phase 5 RESEARCH](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-RESEARCH.md) — Pitfall 5 (F-01-class regression mitigation), Pitfall 6 (cross-injection guard)
- [Phase 5 PATTERNS-MAP](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-PATTERNS.md) — Adaptation Hotspots 1, 3, 6
- [cf-only-walkthrough.md](../../../../.planning/decisions/cf-only-walkthrough.md) — verdict signoff 2026-05-01
- [Mangarr DownloadDecisionMaker](../DownloadDecisionMaker.cs)
- [DIVERGENCE.md](../../../../../DIVERGENCE.md) — Phase 5 D-05 entry (added in Wave 4 plan 05-07)
