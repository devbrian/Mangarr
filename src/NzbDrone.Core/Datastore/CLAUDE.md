# NzbDrone.Core/Datastore

## Purpose

Database layer — connection management, generic repository, ORM mapping, and **schema migrations** (**1 of them — fresh manga baseline** (Phase 1 reset; the inherited Mangarr 224 were replaced by `001_mangarr_baseline.cs` per Phase 0 D-14 fresh-schema decision)).

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

**1 baseline migration** (`001_mangarr_baseline.cs`); future Phase migrations stack sequentially on top (`002_*.cs`, `003_*.cs`, …), implemented with **FluentMigrator**.

Naming convention: `NNN_short_description_in_snake_case.cs` where `NNN` is sequential.

```csharp
[Migration(214)]
public class my_change : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("Series").AddColumn("MyField").AsString().Nullable();
    }

    protected override void LogDbUpgrade() { /* if affecting logs.db */ }
}
```

Migrations run **automatically on startup**, before service registration completes. They operate against both SQLite and Postgres (FluentMigrator abstracts dialect).

#### Migration Subdirectories
- `Framework/` — Custom FluentMigrator extensions (`NzbDroneMigrationBase`, table builders, etc.)
- `Resources/` — Embedded SQL or data files used by migrations

#### Notable Migrations (samples)
- `001_initial_setup.cs` — Initial schema
- `010_add_monitored.cs` — Series.Monitored column
- `013_add_air_date_utc.cs`, `015_add_air_date_as_string.cs` — AirDate evolution
- `124_add_skyhook_episodes_metadata.cs` — Adds tvdbId / tmdbId style metadata
- `223_…` — Latest at time of writing

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

This directory is **infrastructure** and reusable. Migration plan:
- Phase 1: Add manga columns to existing tables (`MangaDexId`, `Author`, `Artist`, etc.) via `224_…` migration
- Phase 2: Add new tables if needed (`ScanlationGroups`, …)
- Phase 3 (renaming): Rename tables (`Series` → `Manga`, `Episodes` → `Chapters`) via a migration. C# class renames must be coordinated with this.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — `MangaRepository` extends `BasicRepository` (the Sonarr `Tv/` `SeriesRepository` analog was deleted in Phase 15)
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — `ModelEvent<T>` published from BasicRepository
- [Phase 1 Foundation Plans](../../../.planning/phases/01-foundation/)
