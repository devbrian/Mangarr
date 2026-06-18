# NzbDrone.Core/Datastore

## Purpose

Database layer — connection management, generic repository, ORM mapping, and **schema migrations** (fresh manga baseline `001_mangarr_baseline.cs` from the Phase 1 reset — the inherited Mangarr 224 TV migrations were replaced per Phase 0 D-14 fresh-schema decision — plus sequential migrations through **`014_v1_3_unique_mangabaka_id.cs`, the current head** (PR #373 — `IX_Manga_MangaBakaId` UNIQUE index)).

This directory is **media-agnostic** infrastructure and reusable as-is. Migrations specific to manga schema additions/renames will be added on top.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Datastore\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `ModelBase.cs` | Tiny abstract base — just `{ public int Id { get; set; } }` |
| `IEmbeddedDocument.cs` | Marker interface — class is serialized as JSON column inside parent table |
| `IBasicRepository.cs` / `BasicRepository.cs` | Generic CRUD repository over Dapper |
| `Database.cs` / `IDatabase.cs` | Connection wrapper |
| `DbFactory.cs` | Factory for the main DB connection |
| `LogDatabase.cs` | Separate DB connection for log table (so logs don't lock main) |
| `ConnectionStringFactory.cs` | Build SQLite or Postgres connection string from config |
| `IConnectionStringFactory.cs` | Interface |
| `MainDatabase.cs` | Main DB connection wrapper |
| `MappingExtensions.cs` | Dapper-specific extensions |
| `PagingSpec.cs` | Server-side pagination spec |
| `LazyLoaded.cs` | Generic lazy-load wrapper for navigation properties |
| `MarrDataMapper.cs` (vestigial) | Earlier ORM bits |

## Subdirectories

### `Migration/` — Schema Migrations

Manga baseline `001_mangarr_baseline.cs`; subsequent Phase migrations stack sequentially on top (`002_*.cs` … through `014_v1_3_unique_mangabaka_id.cs`, the current head), implemented with **FluentMigrator**.

Naming convention: `NNN_short_description_in_snake_case.cs` where `NNN` is sequential.

**Migration 014 (PR #373)** is the head — adds the `IX_Manga_MangaBakaId` UNIQUE index so the DB enforces first-wins on concurrent POSTs with the same MangaBakaId (parity with the baseline `IX_Manga_{MangaDexId,MalId,AniListId}` indexes from issue #213, now that MangaBakaId is a valid standalone add anchor). Because MangaBakaId was populated WITHOUT a uniqueness guarantee from Migration 012, the migration defensively nulls duplicate values (keeping the lowest-Id row per value) BEFORE creating the index, so it can't fail at startup on an existing DB; no-op on a clean DB. Pinned by `MangaRepositoryUniqueConstraintFixture` (duplicate-rejection + null-distinct, full migration chain on real SQLite).

**Migration 010 (Phase 39 RETIRE-03)** — the one-shot on-upgrade orphan-state cleanup for the retired in-process codepath (no longer the head, but the most structurally significant mid-chain migration): it `DELETE`s the orphan `DownloadClients` row (`Implementation = 'InProcessImageDownloadClient'`), `DELETE`s the orphan `Indexers` rows (`Implementation IN ('MangaDexIndexer','ComixIndexer')`), resets the two stale `IndexerSourceStatus` rows scoped to the retired source keys (`'mangadex'`,`'comix.to'`) so live gateway status survives, and `DROP`s the now-empty `ChapterDownloadState` staging table. Pure DELETE+DROP (seeds zero rows — Anti-Pattern C floor); pinned by `Migration010Fixture` (5/5 on SQLite).

```csharp
// Next sequential number after the current head (014) — illustrative.
[Migration(15)]
public class my_change : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("Manga").AddColumn("MyField").AsString().Nullable();
    }

    protected override void LogDbUpgrade() { /* if affecting logs.db */ }
}
```

Migrations run **automatically on startup**, before service registration completes. They operate against both SQLite and Postgres (FluentMigrator abstracts dialect).

#### Migration Subdirectories
- `Framework/` — Custom FluentMigrator extensions (`NzbDroneMigrationBase`, table builders, etc.)
- `Resources/` — Embedded SQL or data files used by migrations

#### The Migration Chain (sequential — manga baseline + post-v1.0 appends)
The inherited Mangarr 224 TV migrations were replaced by a single fresh manga baseline
(Phase 0 D-14 fresh-schema reset). The full chain on disk:
- `001_mangarr_baseline.cs` — the fresh manga baseline (Manga / Chapter / ChapterFile schema + all
  Phase 1 tables). Replaces the entire inherited Sonarr `001_initial_setup.cs … 223_…` chain.
- `002_…` through `009_v1_3_manga_download_history.cs` — sequential post-baseline appends (Phase
  4/5/6/30 schema additions + the v1.2/v1.3 metadata/datetime/history evolutions).
- `010_v1_3_retire_in_process_cleanup.cs` — Phase 39 RETIRE-03 one-shot (see the "Migration 010"
  subsection above).
- `011_v1_3_normalize_manga_profile_ids.cs` — issue #320 one-shot (0-sentinel profile FK → NULL).
- `012_v1_3_add_mangabaka_metadata_source.cs` + `013_v1_3_add_mangabaka_cross_source_ids.cs` —
  Phase 41 / quick-260608-l2e MangaBaka column adds (`MangaBakaId` + the 5 exotic cross-source ids).
- `014_v1_3_unique_mangabaka_id.cs` — PR #373; `IX_Manga_MangaBakaId` UNIQUE;
  see the "Migration 014" subsection above.
- `015_v1_3_add_user_alternative_titles.cs` — **the current head** (quick-260618-eqz; appends the
  nullable `UserAlternativeTitles` JSON-string column for the user-owned alt-title set — the
  Sonarr-divergent inverse of metadata `AlternativeTitles`; see DIVERGENCE.md quick-260618-eqz).

No `Series`/`Episode`/`Season` tables exist in the chain — `Tv/` was deleted in Phase 15.

### `Converters/` — Dapper / JSON Converters

| File | Purpose |
|------|---------|
| `EmbeddedDocumentConverter.cs` | Serialize embedded docs (Season, Quality, etc.) as JSON column |
| `QualityIntConverter.cs` | Quality enum stored as int |
| `LanguageIntConverter.cs` | Language enum stored as int |
| `OsPathConverter.cs` | `OsPath` ↔ string |
| `TimeSpanConverter.cs` | TimeSpan ↔ TEXT |
| etc. |

These are registered via Dapper `SqlMapper.AddTypeHandler` at startup.

### `Extensions/`
Mapping extensions, query helpers (`ToSqlBuilder`, `WhereInOrEmpty`, etc.).

### `Events/`
| Event | Triggered |
|-------|-----------|
| `ModelEvent<T>` | After insert / update / delete on a `BasicRepository` |
| Used heavily by handlers to react to state changes |

## BasicRepository — Public API

```csharp
public interface IBasicRepository<TModel> where TModel : ModelBase
{
    IEnumerable<TModel> All();
    int Count();
    TModel Find(int id);
    TModel Get(int id);
    IEnumerable<TModel> Get(IEnumerable<int> ids);
    bool HasItems();

    TModel Insert(TModel model);
    void InsertMany(IList<TModel> models);
    TModel Update(TModel model);
    void UpdateMany(IList<TModel> models);
    TModel Upsert(TModel model);

    void Delete(int id);
    void Delete(TModel model);
    void DeleteMany(IEnumerable<int> ids);
    void DeleteMany(List<TModel> models);

    TModel SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
    void SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties);

    PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec);
    void Purge(bool vacuum = false);

    TModel Single();
    TModel SingleOrDefault();
}
```

Most domain repositories extend this:

```csharp
public interface ISeriesRepository : IBasicRepository<Series>
{
    Series FindByTitle(string cleanTitle);
    Series FindByTvdbId(int tvdbId);
    // … domain-specific finders
}

public class SeriesRepository : BasicRepository<Series>, ISeriesRepository
{
    public SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
        : base(database, eventAggregator) { }

    public Series FindByTitle(string cleanTitle) =>
        Query(s => s.CleanTitle == cleanTitle).FirstOrDefault();
}
```

## Database Files

### Main DB
- **SQLite (default)**: `<datafolder>/mangarr.db` (renamed from `sonarr.db` per Phase 15 D-08 F-B carry-forward; legacy file detected + renamed in-place by `AppFolderFactory.MigrateAppDataFolder()`)
- **Postgres**: configurable via `<Mangarr><Postgres><Host>` etc. in `config.xml`

### Log DB
- Always SQLite (or separate Postgres database): `<datafolder>/logs.db`
- Kept separate so logging doesn't contend with main DB writes.

## Embedded Documents

Some entities are stored **inside parent rows** as JSON columns:

```csharp
public class Series : ModelBase
{
    public List<Season> Seasons { get; set; }   // ← JSON column
    public List<MediaCover> Images { get; set; }// ← JSON column
    public Ratings Ratings { get; set; }        // ← JSON column
}

public class Season : IEmbeddedDocument { … }
public class MediaCover : IEmbeddedDocument { … }
```

The `EmbeddedDocumentConverter` (in `Converters/`) handles round-tripping.

## Adding a Migration

1. Find the highest existing migration number in `Migration/`.
2. Create `Migration/NNN_my_change.cs` (next number).
3. Inherit from `NzbDroneMigrationBase`. Override `MainDbUpgrade()` (and `LogDbUpgrade()` if needed).
4. Use FluentMigrator API: `Create.Table`, `Alter.Table`, `Execute.Sql`, etc.
5. Test by running the app — migration runs automatically on first startup.
6. **Never modify a previously released migration** — add a new one.

## SQLite vs Postgres

Most code is dialect-agnostic via FluentMigrator. Differences:
- Some Postgres-specific code paths exist for performance-critical queries
- Run tests with `--filter Category=Postgres` after setting Postgres connection vars
- See `postgres.runsettings` for Postgres test config

## Manga Adaptation Notes

This directory is **infrastructure** and reusable. The manga-schema migration work is **done**:
- The fresh manga baseline `001_mangarr_baseline.cs` (Phase 1) already defines the canonical
  `Manga` / `Chapter` / `ChapterFile` tables with manga columns (`MangaDexId`, `Author`, `Artist`,
  `ScanlationGroup`, etc.) — there is no separate "add manga columns to TV tables" step; the TV
  tables never shipped in the manga baseline.
- Subsequent manga-schema additions append a NEW sequential migration on top of the head
  (`014_…` currently) per the post-v1.0.0 append-only policy — never edit a released migration.
- The `Series → Manga` / `Episodes → Chapters` rename is N/A: `Tv/` was deleted wholesale in
  Phase 15 Plan 15-03, so the baseline was authored manga-shape from the start (no table rename
  migration was ever needed).

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — `MangaRepository` extends `BasicRepository` (the Sonarr `Tv/` `SeriesRepository` analog was deleted in Phase 15)
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — `ModelEvent<T>` published from BasicRepository
- [Phase 1 Foundation Plans](../../../.planning/phases/01-foundation/)
