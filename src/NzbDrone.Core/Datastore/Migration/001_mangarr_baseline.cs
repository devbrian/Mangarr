using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 1 baseline migration. Replaces Sonarr's 224 inherited TV-shaped migrations
    // with a single fresh manga-shaped baseline:
    //   * Recreates the Tv-shaped tables (Series, Seasons, Episodes, EpisodeFiles) so
    //     the inherited Tv/ C# code compiles (D-02). These tables sit empty at runtime;
    //     Phase 8 drops them when Tv/ is removed.
    //   * Recreates ThingiProvider + supporting tables verbatim (D-03). TV-specific
    //     columns are flagged with `// TODO: Phase 8` comments for the rename-last cutover.
    //   * Adds new manga tables: Manga and Chapters (D-01 / D-06 / D-08 / D-09).
    //   * Adds nullable MangaId/ChapterId columns to History/Blocklist (D-07).
    //   * Creates the five locked composite indexes from Phase 0 D-15.
    //
    // No Down() method — NzbDroneMigrationBase.Down() throws NotImplementedException
    // by convention.
    //
    // BusyTimeout × Polly retry interaction: BusyTimeout=5000ms × MaxRetryAttempts=3
    // = ~15s worst-case wait under heavy contention. Acceptable for v1 single-user
    // concurrency profile. Phase 4 load testing may revisit.
    //
    // Phase 2 deltas folded in 2026-05-02 per dev-migration-policy.md — Migration 002
    // (chapter_extensions_and_precision) was authored under the old append-only rule,
    // then folded back into this baseline and deleted. Manga.Status retyped from
    // AsInt32().NotNullable() to AsString().Nullable() in the same pass to match the
    // C# string Status property (Manga.cs:39). Five missing Manga columns (SortTitle,
    // Overview, RootFolderPath, ContentRating, Genres) added per
    // 02-VERIFICATION.md gap #1.
    [Migration(1)]
    public class mangarr_baseline : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // ─────────────────────────────────────────────────────────────────────
            // Configuration / global tables
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("Config")
                .WithColumn("Key").AsString().Unique()
                .WithColumn("Value").AsString();

            Create.TableForModel("RootFolders")
                .WithColumn("Path").AsString().Unique();

            Create.TableForModel("ScheduledTasks")
                .WithColumn("TypeName").AsString().Unique()
                .WithColumn("Interval").AsDouble()
                .WithColumn("LastExecution").AsDateTime()
                .WithColumn("LastStartTime").AsDateTime().Nullable();

            // ─────────────────────────────────────────────────────────────────────
            // ThingiProvider tables (D-03 — verbatim recreation; TV-specific
            // columns flagged for Phase 8 cleanup).
            // ─────────────────────────────────────────────────────────────────────
            // TODO: Phase 8 — review TV-specific provider columns when Tv/ is removed.
            Create.TableForModel("Indexers")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Implementation").AsString()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("EnableRss").AsBoolean().Nullable()
                .WithColumn("EnableSearch").AsBoolean().Nullable()
                .WithColumn("EnableAutomaticSearch").AsBoolean().Nullable()
                .WithColumn("EnableInteractiveSearch").AsBoolean().Nullable()
                .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(25)
                .WithColumn("DownloadClientId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("SeasonSearchMaximumSingleEpisodeAge").AsInt32().NotNullable().WithDefaultValue(0); // migration 172

            Create.TableForModel("DownloadClients")
                .WithColumn("Enable").AsBoolean()
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Implementation").AsString()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("RemoveCompletedDownloads").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("RemoveFailedDownloads").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("Tags").AsString().Nullable();

            // TODO: Phase 8 — strip OnSeries* / OnEpisodeFile* event columns when Tv/ removed.
            // Phase 6 D-18 — OnChapterImport added at end (default TRUE so newly-added
            // Komga/Kavita providers fire OnChapterImport without an extra checkbox click;
            // user can still disable per-provider). See DIVERGENCE.md + dev-migration-policy.md.
            Create.TableForModel("Notifications")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("OnGrab").AsBoolean()
                .WithColumn("OnDownload").AsBoolean()
                .WithColumn("Settings").AsString()
                .WithColumn("Implementation").AsString()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("OnUpgrade").AsBoolean().Nullable()
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("OnRename").AsBoolean()
                .WithColumn("OnSeriesAdd").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnSeriesDelete").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnEpisodeFileDelete").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnEpisodeFileDeleteForUpgrade").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnHealthIssue").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("IncludeHealthWarnings").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnApplicationUpdate").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnManualInteractionRequired").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnHealthRestored").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnImportComplete").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnChapterImport").AsBoolean().NotNullable().WithDefaultValue(true);

            // Sonarr's Metadata table is the ThingiProvider for IMetadataConsumer
            // (Kodi/Roksbox/Wdtv). CONTEXT.md D-01 'MetadataSources' refers to this
            // same table — no separate MetadataSources table exists in Sonarr's schema.
            // Verbatim recreation per D-02/D-03.
            Create.TableForModel("Metadata")
                .WithColumn("Enable").AsBoolean().NotNullable()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("Implementation").AsString().NotNullable()
                .WithColumn("Settings").AsString().NotNullable()
                .WithColumn("ConfigContract").AsString().NotNullable();

            // Phase 2 (folded from Migration 002 per dev-migration-policy.md, 2026-05-02).
            // MetadataSources is the NEW ThingiProvider table for IMetadataSource (D-14, D-15) —
            // distinct from the existing 'Metadata' IMetadataConsumer table (Pitfall 2). Ships
            // with IsPrimary defaulted false; at-most-one invariant enforced in
            // MetadataSourceFactory.SetPrimary (Plan 02-05), not the DB.
            Create.TableForModel("MetadataSources")
                .WithColumn("Name").AsString().NotNullable().Unique()
                .WithColumn("Implementation").AsString().NotNullable()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Tags").AsString().Nullable();

            // TODO: Phase 8 — drop SeriesType / SeasonFolder columns when Tv/ removed.
            Create.TableForModel("ImportLists")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Implementation").AsString()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("EnableAutomaticAdd").AsBoolean()
                .WithColumn("RootFolderPath").AsString()
                .WithColumn("ShouldMonitor").AsInt32()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("SeriesType").AsInt32()
                .WithColumn("SeasonFolder").AsBoolean()
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("MonitorNewItems").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("SearchForMissingEpisodes").AsBoolean().NotNullable().WithDefaultValue(true); // migration 197

            Create.TableForModel("ImportListItems")
                .WithColumn("ImplementationName").AsString()
                .WithColumn("ImportListId").AsInt32()
                .WithColumn("ServiceProviderId").AsInt32()
                .WithColumn("Title").AsString()
                .WithColumn("TvdbId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("ReleaseDate").AsDateTime();

            Create.TableForModel("Tags")
                .WithColumn("Label").AsString().Unique();

            // TODO: Phase 8 — drop TV-specific naming format columns when Tv/ removed.
            // ─────────────────────────────────────────────────────────────────────
            // Phase 5 — NamingConfig manga columns.
            // Per dev-migration-policy.md edit-001 + Phase 5 D-13 (extend existing NamingConfig
            // singleton; DO NOT ship a sibling MangaNamingConfig table — D-04 invariant: ONE
            // singleton, no Library entity). Phase 8 cleanup: drop TV-shaped columns when Tv/ deletes.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("NamingConfig")
                .WithColumn("MultiEpisodeStyle").AsInt32()
                .WithColumn("RenameEpisodes").AsBoolean().Nullable()
                .WithColumn("StandardEpisodeFormat").AsString().Nullable()
                .WithColumn("DailyEpisodeFormat").AsString().Nullable()
                .WithColumn("SeriesFolderFormat").AsString().Nullable()
                .WithColumn("SeasonFolderFormat").AsString().Nullable()
                .WithColumn("AnimeEpisodeFormat").AsString().Nullable()
                .WithColumn("ReplaceIllegalCharacters").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("SpecialsFolderFormat").AsString().NotNullable().WithDefaultValue("Specials")
                .WithColumn("ColonReplacementFormat").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CustomColonReplacementFormat").AsString().NotNullable().WithDefaultValue(string.Empty)
                .WithColumn("StandardChapterFormat").AsString().Nullable()
                .WithColumn("MangaFolderFormat").AsString().Nullable()
                .WithColumn("RenameChapters").AsBoolean().NotNullable().WithDefaultValue(false);

            // ─────────────────────────────────────────────────────────────────────
            // TV domain tables (D-02 — verbatim recreation; sit empty at runtime;
            // deleted in Phase 8 when Tv/ namespace is removed).
            // ─────────────────────────────────────────────────────────────────────
            // TODO: Phase 8 — drop entire Series/Seasons/Episodes/EpisodeFiles tables.
            Create.TableForModel("Series")
                .WithColumn("TvdbId").AsInt32().Unique()
                .WithColumn("TvRageId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("Title").AsString()
                .WithColumn("TitleSlug").AsString().Nullable()
                .WithColumn("CleanTitle").AsString()
                .WithColumn("SortTitle").AsString().Nullable()
                .WithColumn("Status").AsInt32()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("AirTime").AsString().Nullable()
                .WithColumn("Images").AsString()
                .WithColumn("Path").AsString()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("SeasonFolder").AsBoolean()
                .WithColumn("LastInfoSync").AsDateTime().Nullable()
                .WithColumn("LastDiskSync").AsDateTime().Nullable()
                .WithColumn("Runtime").AsInt32()
                .WithColumn("SeriesType").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Network").AsString().Nullable()
                .WithColumn("FirstAired").AsDateTime().Nullable()
                .WithColumn("NextAiring").AsDateTime().Nullable()
                .WithColumn("Year").AsInt32().Nullable()
                .WithColumn("Seasons").AsString().Nullable()         // migration 020 — JSON list of Season
                .WithColumn("Genres").AsString().Nullable()
                .WithColumn("Ratings").AsString().Nullable()
                .WithColumn("Actors").AsString().Nullable()
                .WithColumn("Certification").AsString().Nullable()
                .WithColumn("UseSceneNumbering").AsBoolean()
                .WithColumn("Added").AsDateTime().Nullable()
                .WithColumn("AddOptions").AsString().Nullable()
                .WithColumn("LanguageProfileId").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("TvMazeId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TmdbId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("MonitorNewItems").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("OriginalLanguage").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("OriginalCountry").AsString().Nullable()
                .WithColumn("LastAired").AsDateTime().Nullable()
                .WithColumn("MalIds").AsString().Nullable()      // Sonarr partial migration 217
                .WithColumn("AniListIds").AsString().Nullable(); // Sonarr partial migration 217

            Create.TableForModel("Seasons")
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("SeasonNumber").AsInt32()
                .WithColumn("Monitored").AsBoolean();

            Create.TableForModel("Episodes")
                .WithColumn("TvDbEpisodeId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("SeasonNumber").AsInt32()
                .WithColumn("EpisodeNumber").AsInt32()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("Ratings").AsString().Nullable()
                .WithColumn("Images").AsString().Nullable()
                .WithColumn("AirDate").AsString().Nullable()
                .WithColumn("AirDateUtc").AsDateTime().Nullable()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("AbsoluteEpisodeNumber").AsInt32().Nullable()
                .WithColumn("SceneAbsoluteEpisodeNumber").AsInt32().Nullable()
                .WithColumn("SceneSeasonNumber").AsInt32().Nullable()
                .WithColumn("SceneEpisodeNumber").AsInt32().Nullable()
                .WithColumn("UnverifiedSceneNumbering").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Runtime").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("EpisodeFileId").AsInt32().Nullable()
                .WithColumn("LastSearchTime").AsDateTime().Nullable()
                .WithColumn("FinaleType").AsString().Nullable()
                .WithColumn("TvdbId").AsInt32().Nullable()                    // migration 167
                .WithColumn("AiredAfterSeasonNumber").AsInt32().Nullable()    // migration 137
                .WithColumn("AiredBeforeSeasonNumber").AsInt32().Nullable()   // migration 137
                .WithColumn("AiredBeforeEpisodeNumber").AsInt32().Nullable(); // migration 137

            Create.TableForModel("EpisodeFiles")
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("Quality").AsString()
                .WithColumn("Size").AsInt64()
                .WithColumn("DateAdded").AsDateTime()
                .WithColumn("SeasonNumber").AsInt32()
                .WithColumn("RelativePath").AsString().Nullable()
                .WithColumn("Language").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("ReleaseGroup").AsString().Nullable()
                .WithColumn("SceneName").AsString().Nullable()
                .WithColumn("MediaInfo").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable().WithDefaultValue("[]")
                .WithColumn("OriginalFilePath").AsString().Nullable()
                .WithColumn("CustomFormats").AsString().Nullable()
                .WithColumn("CustomFormatScore").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ReleaseHash").AsString().Nullable()
                .WithColumn("ReleaseType").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("IndexerFlags").AsInt32().NotNullable().WithDefaultValue(0); // migration 202

            // History and Blocklist hold both TV and manga events. The TV-shaped
            // SeriesId/EpisodeIds columns sit empty at Mangarr runtime; the new
            // MangaId/ChapterId columns (added below) carry manga events.
            Create.TableForModel("History")
                .WithColumn("EpisodeId").AsInt32()
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("SourceTitle").AsString()
                .WithColumn("Quality").AsString()
                .WithColumn("Date").AsDateTime()
                .WithColumn("Data").AsString()
                .WithColumn("EventType").AsInt32().Nullable()
                .WithColumn("DownloadId").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable().WithDefaultValue("[]");

            Create.TableForModel("Blocklist")
                .WithColumn("SeriesId").AsInt32()
                .WithColumn("EpisodeIds").AsString()
                .WithColumn("SourceTitle").AsString()
                .WithColumn("Quality").AsString()
                .WithColumn("Date").AsDateTime()
                .WithColumn("PublishedDate").AsDateTime().Nullable()    // migration 047
                .WithColumn("Size").AsInt64().Nullable()
                .WithColumn("Protocol").AsInt32().Nullable()
                .WithColumn("Indexer").AsString().Nullable()
                .WithColumn("Message").AsString().Nullable()
                .WithColumn("TorrentInfoHash").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable().WithDefaultValue("[]")
                .WithColumn("IndexerFlags").AsInt32().NotNullable().WithDefaultValue(0)  // migration 202
                .WithColumn("ReleaseType").AsInt32().NotNullable().WithDefaultValue(0)   // migration 203
                .WithColumn("Source").AsString().Nullable();                              // migration 223

            // ─────────────────────────────────────────────────────────────────────
            // Quality / Profile tables (Phase 5 reuses; TV-shaped today).
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("QualityDefinitions")
                .WithColumn("Quality").AsInt32().Unique()
                .WithColumn("Title").AsString().Unique()
                .WithColumn("MinSize").AsDouble().Nullable()
                .WithColumn("MaxSize").AsDouble().Nullable()
                .WithColumn("PreferredSize").AsDouble().Nullable();

            Create.TableForModel("QualityProfiles")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Cutoff").AsInt32()
                .WithColumn("Items").AsString().NotNullable()
                .WithColumn("UpgradeAllowed").AsBoolean().Nullable()
                .WithColumn("MinFormatScore").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CutoffFormatScore").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("FormatItems").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("MinUpgradeFormatScore").AsInt32().NotNullable().WithDefaultValue(1);

            // ─────────────────────────────────────────────────────────────────────
            // Phase 5 — TranslationProfiles table.
            // Per dev-migration-policy.md edit-001 + Phase 5 D-01 + D-03 (cf-only-walkthrough.md verdict).
            // Sibling to QualityProfiles (above). Languages persists as JSON column via the
            // EmbeddedDocumentConverter<List<string>> (StringListConverter<List<string>>) already
            // registered at TableMapping.cs:224 — no new converter needed (PATTERNS-MAP Adaptation Hotspot 8).
            // Phase 8 cleanup: collapse Profiles/Translations into canonical Profiles/ namespace.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("TranslationProfiles")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Languages").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("AllowLanguagesNotInProfile").AsBoolean().NotNullable().WithDefaultValue(false);

            // ─────────────────────────────────────────────────────────────────────
            // Phase 5 — CustomFormatProfiles table.
            // Per dev-migration-policy.md edit-001 + Phase 5 D-07 (CF score thresholds live on a
            // SEPARATE entity from TranslationProfile — language preference + CF scoring are
            // orthogonal user concerns). MaxFormatScore is NULLABLE (divergence from QualityProfile
            // which has no max-score concept). FormatItems uses EmbeddedDocumentConverter
            // <List<ProfileFormatItem>> already registered at TableMapping.cs:213.
            // Phase 8 cleanup: collapse Profiles/CustomFormats into canonical Profiles/ namespace.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("CustomFormatProfiles")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("MinFormatScore").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("MaxFormatScore").AsInt32().Nullable()
                .WithColumn("FormatItems").AsString().NotNullable().WithDefaultValue("[]");

            Create.TableForModel("CustomFormats")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Specifications").AsString().NotNullable()
                .WithColumn("IncludeCustomFormatWhenRenaming").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.TableForModel("CustomFilters")
                .WithColumn("Type").AsString()
                .WithColumn("Label").AsString()
                .WithColumn("Filters").AsString();

            Create.TableForModel("DelayProfiles")
                .WithColumn("EnableUsenet").AsBoolean()
                .WithColumn("EnableTorrent").AsBoolean()
                .WithColumn("PreferredProtocol").AsInt32()
                .WithColumn("UsenetDelay").AsInt32()
                .WithColumn("TorrentDelay").AsInt32()
                .WithColumn("Order").AsInt32()
                .WithColumn("Tags").AsString()
                .WithColumn("BypassIfHighestQuality").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("BypassIfAboveCustomFormatScore").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("MinimumCustomFormatScore").AsInt32().NotNullable().WithDefaultValue(0);

            Create.TableForModel("ReleaseProfiles")
                .WithColumn("Required").AsString().Nullable()
                .WithColumn("Ignored").AsString().Nullable()
                .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("IndexerId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("ExcludedTags").AsString().NotNullable().WithDefaultValue("[]") // migration 221
                .WithColumn("IndexerIds").AsString().NotNullable().WithDefaultValue("[]")    // migration 224
                .WithColumn("AirDateRestriction").AsBoolean().NotNullable().WithDefaultValue(false) // migration 226
                .WithColumn("AirDateGracePeriod").AsInt32().NotNullable().WithDefaultValue(0);      // migration 226

            Create.TableForModel("AutoTagging")
                .WithColumn("Name").AsString().NotNullable().Unique()
                .WithColumn("Specifications").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]")
                .WithColumn("RemoveTagsAutomatically").AsBoolean().NotNullable().WithDefaultValue(false);

            // ─────────────────────────────────────────────────────────────────────
            // Health / Status tables.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("IndexerStatus")
                .WithColumn("ProviderId").AsInt32().Unique()
                .WithColumn("InitialFailure").AsDateTime().Nullable()
                .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                .WithColumn("EscalationLevel").AsInt32().NotNullable()
                .WithColumn("DisabledTill").AsDateTime().Nullable()
                .WithColumn("LastRssSyncReleaseInfo").AsString().Nullable()
                .WithColumn("CookiesExpirationDate").AsDateTime().Nullable()
                .WithColumn("Cookies").AsString().Nullable();

            // ─────────────────────────────────────────────────────────────────────
            // Phase 3 D-17 — per-SourceKey indexer status (HttpAggregatorBase descendants
            // only; e.g. two MangaDex instances share disable state). TV indexers (Newznab/
            // Nyaa/Torznab) continue to use the IndexerStatus table above keyed on ProviderId.
            // Q-2 Option A: NEW sibling table for clean Phase 8 DROP when Tv/ deletes.
            // Per dev-migration-policy.md: edits 001 directly until v1.0.0 freeze.
            // Cookies columns omitted — manga aggregators do not use Sonarr's cookie-jar
            // pattern; if a future port needs cookies, add as a per-port settings field.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("IndexerSourceStatus")
                .WithColumn("SourceKey").AsString().NotNullable().Unique()
                .WithColumn("InitialFailure").AsDateTime().Nullable()
                .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                .WithColumn("EscalationLevel").AsInt32().NotNullable()
                .WithColumn("DisabledTill").AsDateTime().Nullable()
                .WithColumn("LastRssSyncReleaseInfo").AsString().Nullable();

            // ─────────────────────────────────────────────────────────────────────
            // Phase 4 D-05 — ChapterDownloadState (in-flight chapter download lifecycle).
            // Hybrid resumable-state model: this row is the single source of truth for
            // "is this chapter in flight"; per-page bytes live in
            // <Config.DownloadScratchPath>/<Id>/<NNNN>.<ext>.
            // Phase 6 reads Status=Completed rows for ImportApprovedChapters dispatch.
            // Phase 8 cleanup: stays as-is (manga-shaped from Day 1).
            //
            // Per dev-migration-policy.md: edits 001 directly until v1.0.0 freeze.
            // No unique index on (MangaId, ChapterId) per RESEARCH.md Q-1 Option (b) —
            // app-level dedup via FindByMangaAndChapter so retention-window retries
            // (Failed row + new in-flight row) coexist without partial-index portability.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("ChapterDownloadState")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterId").AsInt32().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("RemoteChapterJson").AsString().NotNullable()
                .WithColumn("ManifestJson").AsString().Nullable()
                .WithColumn("ManifestExpiresAt").AsDateTime().Nullable()
                .WithColumn("TotalPages").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CompletedPages").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("EstimatedSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("ScratchDir").AsString().NotNullable()
                .WithColumn("StagingPath").AsString().Nullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("FailureReason").AsString().Nullable()
                .WithColumn("RetentionUntil").AsDateTime().Nullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable()
                .WithColumn("UpdatedAt").AsDateTime().NotNullable();

            // Covering index on (Status, UpdatedAt) for the GetItems() poll path
            // (plan 04-03 — InProcessImageDownloadClient.GetItems projects from this).
            Create.Index().OnTable("ChapterDownloadState")
                .OnColumn("Status").Ascending()
                .OnColumn("UpdatedAt").Ascending();

            Create.TableForModel("DownloadClientStatus")
                .WithColumn("ProviderId").AsInt32().Unique()
                .WithColumn("InitialFailure").AsDateTime().Nullable()
                .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                .WithColumn("EscalationLevel").AsInt32().NotNullable()
                .WithColumn("DisabledTill").AsDateTime().Nullable();

            Create.TableForModel("ImportListStatus")
                .WithColumn("ProviderId").AsInt32().Unique()
                .WithColumn("InitialFailure").AsDateTime().Nullable()
                .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                .WithColumn("EscalationLevel").AsInt32().NotNullable()
                .WithColumn("DisabledTill").AsDateTime().Nullable()
                .WithColumn("LastSyncListInfo").AsString().Nullable()
                .WithColumn("LastInfoSync").AsDateTime().Nullable()
                .WithColumn("HasRemovedItemSinceLastClean").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.TableForModel("NotificationStatus")
                .WithColumn("ProviderId").AsInt32().NotNullable().Unique()
                .WithColumn("InitialFailure").AsDateTime().Nullable()
                .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                .WithColumn("EscalationLevel").AsInt32().NotNullable()
                .WithColumn("DisabledTill").AsDateTime().Nullable();

            // ─────────────────────────────────────────────────────────────────────
            // Pipeline / state tables (TV-shaped today; Phase 4-5 manga adapt).
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("PendingReleases")
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("Added").AsDateTime().NotNullable()
                .WithColumn("ParsedEpisodeInfo").AsString().NotNullable()
                .WithColumn("Release").AsString().NotNullable()
                .WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("AdditionalInfo").AsString().Nullable();

            Create.TableForModel("RemotePathMappings")
                .WithColumn("Host").AsString()
                .WithColumn("RemotePath").AsString()
                .WithColumn("LocalPath").AsString();

            Create.TableForModel("Commands")
                .WithColumn("Name").AsString()
                .WithColumn("Body").AsString()
                .WithColumn("Priority").AsInt32()
                .WithColumn("Status").AsInt32()
                .WithColumn("QueuedAt").AsDateTime()
                .WithColumn("StartedAt").AsDateTime().Nullable()
                .WithColumn("EndedAt").AsDateTime().Nullable()
                .WithColumn("Duration").AsString().Nullable()
                .WithColumn("Exception").AsString().Nullable()
                .WithColumn("Trigger").AsInt32()
                .WithColumn("Result").AsInt32().NotNullable().WithDefaultValue(1); // migration 186

            Create.TableForModel("DownloadHistory")
                .WithColumn("EventType").AsInt32().NotNullable()
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("DownloadId").AsString().NotNullable()
                .WithColumn("SourceTitle").AsString().NotNullable()
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("Protocol").AsInt32().Nullable()
                .WithColumn("IndexerId").AsInt32().Nullable()
                .WithColumn("DownloadClientId").AsInt32().Nullable()
                .WithColumn("Release").AsString().Nullable()
                .WithColumn("Data").AsString().Nullable();

            Create.TableForModel("UpdateHistory")
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("Version").AsString().NotNullable()
                .WithColumn("EventType").AsInt32().NotNullable();

            Create.TableForModel("ImportListExclusions")
                .WithColumn("TvdbId").AsInt32().Unique()
                .WithColumn("Title").AsString().NotNullable();

            // TODO: Phase 8 — Metadata/Subtitle/Extra files are currently TV-keyed.
            // Phase 4 will add manga equivalents; Phase 8 drops the SeriesId/EpisodeFile
            // foreign keys when Tv/ is removed.
            Create.TableForModel("MetadataFiles")
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("Consumer").AsString().NotNullable()
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("RelativePath").AsString().NotNullable()
                .WithColumn("LastUpdated").AsDateTime().NotNullable()
                .WithColumn("SeasonNumber").AsInt32().Nullable()
                .WithColumn("EpisodeFileId").AsInt32().Nullable()
                .WithColumn("EpisodeId").AsInt32().Nullable()
                .WithColumn("Added").AsDateTime().Nullable()
                .WithColumn("Extension").AsString().NotNullable()
                .WithColumn("Hash").AsString().Nullable();

            Create.TableForModel("SubtitleFiles")
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("SeasonNumber").AsInt32().NotNullable()
                .WithColumn("EpisodeFileId").AsInt32().NotNullable()
                .WithColumn("RelativePath").AsString().NotNullable()
                .WithColumn("Added").AsDateTime()
                .WithColumn("LastUpdated").AsDateTime().Nullable()
                .WithColumn("Extension").AsString().Nullable()
                .WithColumn("Language").AsInt32().NotNullable()
                .WithColumn("LanguageTags").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Copy").AsInt32().NotNullable().WithDefaultValue(0); // migration 198

            Create.TableForModel("ExtraFiles")
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("SeasonNumber").AsInt32().NotNullable()
                .WithColumn("EpisodeFileId").AsInt32().NotNullable()
                .WithColumn("RelativePath").AsString().NotNullable()
                .WithColumn("Extension").AsString().NotNullable()
                .WithColumn("Added").AsDateTime().NotNullable()
                .WithColumn("LastUpdated").AsDateTime().NotNullable();

            // ─────────────────────────────────────────────────────────────────────
            // Auth. Identifier stored AsString (not AsGuid) to match Sonarr's
            // original migration 76; the model Identifier property is `Guid` and
            // Dapper handles the string<->Guid conversion via GuidConverter.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("Users")
                .WithColumn("Identifier").AsString().NotNullable().Unique()
                .WithColumn("Username").AsString().NotNullable().Unique()
                .WithColumn("Password").AsString().NotNullable()
                .WithColumn("Salt").AsString().Nullable()
                .WithColumn("Iterations").AsInt32().Nullable();

            // TODO: Phase 8 — remove SceneMappings table when Tv/ is deleted.
            Create.TableForModel("SceneMappings")
                .WithColumn("TvdbId").AsInt32().NotNullable()
                .WithColumn("SeasonNumber").AsInt32().Nullable()
                .WithColumn("SearchTerm").AsString().NotNullable()
                .WithColumn("ParseTerm").AsString().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("Type").AsString().NotNullable()
                .WithColumn("FilterRegex").AsString().Nullable()
                .WithColumn("MappingId").AsString().Nullable(); // migration 230

            // ═════════════════════════════════════════════════════════════════════
            // NEW MANGA TABLES (D-01 / D-06 / D-08 / D-09).
            // ═════════════════════════════════════════════════════════════════════
            // ─────────────────────────────────────────────────────────────────────
            // Phase 5 — Manga FK columns (TranslationProfileId + CustomFormatProfileId).
            // Per dev-migration-policy.md edit-001 + Phase 5 D-01 + D-07.
            // Both nullable int FK; null = fall back to Config.DefaultTranslationProfileId
            // or Config.DefaultCustomFormatProfileId. Mirrors Sonarr's Series.QualityProfileId
            // per-Series-FK pattern verbatim.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("Manga")
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("CleanTitle").AsString().NotNullable()
                .WithColumn("SortTitle").AsString().Nullable()              // gap #1 fix (Manga.cs:35)
                .WithColumn("Overview").AsString().Nullable()               // gap #1 fix (Manga.cs:38)
                .WithColumn("MangaDexId").AsString().Nullable()
                .WithColumn("MalId").AsInt32().Nullable()                   // folded from 002 (singular per CONTEXT specifics)
                .WithColumn("AniListId").AsInt32().Nullable()               // folded from 002 (singular per CONTEXT specifics)
                .WithColumn("Path").AsString().NotNullable()
                .WithColumn("RootFolderPath").AsString().Nullable()         // gap #1 fix (Manga.cs:47)
                .WithColumn("ContentRating").AsString().Nullable()          // gap #1 fix (Manga.cs:40)
                .WithColumn("Monitored").AsBoolean().NotNullable()
                .WithColumn("Status").AsString().Nullable()                 // BLOCKER 1 fix: was AsInt32().NotNullable(); Manga.cs:39 declares string Status accepting metadata sentinels (ongoing/completed/hiatus/cancelled)
                .WithColumn("Added").AsDateTime().NotNullable()
                .WithColumn("LastInfoSync").AsDateTime().Nullable()
                .WithColumn("Images").AsString().Nullable()                 // JSON list
                .WithColumn("Genres").AsString().Nullable()                 // gap #1 fix (Manga.cs:43 — JSON list via existing StringListConverter<List<string>>() at TableMapping.cs:206)
                .WithColumn("Tags").AsString().Nullable()                   // JSON list
                .WithColumn("TotalChapterCount").AsInt32().Nullable()       // folded from 002 (D-17 synthesis fallback)
                .WithColumn("PublicationYear").AsInt32().Nullable()         // folded from 002 (D-21 multi-axis confirm)
                .WithColumn("PrimaryAuthor").AsString().Nullable()          // folded from 002 (D-21 multi-axis confirm)
                .WithColumn("TranslationProfileId").AsInt32().Nullable()    // Phase 5 D-01 — null = fall back to Config.DefaultTranslationProfileId
                .WithColumn("CustomFormatProfileId").AsInt32().Nullable();  // Phase 5 D-07 — null = fall back to Config.DefaultCustomFormatProfileId

            Create.TableForModel("Chapters")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterNumber").AsDecimal(10, 3).NotNullable()         // D-12 applied at baseline (was DECIMAL(10,2))
                .WithColumn("TranslatedLanguage").AsString().NotNullable()           // BCP-47
                .WithColumn("ScanlationGroup").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("ReleaseDate").AsDateTime().Nullable()
                .WithColumn("Monitored").AsBoolean().NotNullable()
                .WithColumn("ExternalId").AsString().Nullable()
                .WithColumn("ChapterType").AsString().NotNullable().WithDefaultValue("Regular")  // folded from 002 (D-11)
                .WithColumn("VolumeNumber").AsInt32().Nullable()                                  // folded from 002 (D-11)
                .WithColumn("AbsoluteChapterNumber").AsDecimal(10, 3).Nullable()                  // folded from 002 (D-11)
                .WithColumn("IsSynthetic").AsBoolean().NotNullable().WithDefaultValue(false);     // folded from 002 (D-17 marker)

            // ─────────────────────────────────────────────────────────────────────
            // History/Blocklist nullable manga columns (D-07).
            // ─────────────────────────────────────────────────────────────────────
            Alter.Table("History")
                .AddColumn("MangaId").AsInt32().Nullable()
                .AddColumn("ChapterId").AsInt32().Nullable();

            Alter.Table("Blocklist")
                .AddColumn("MangaId").AsInt32().Nullable();

            // ─────────────────────────────────────────────────────────────────────
            // Composite indexes — Phase 0 D-15 baseline floor.
            // ─────────────────────────────────────────────────────────────────────
            // 1. (History.MangaId, Date DESC) — primary listing query path.
            Create.Index().OnTable("History")
                .OnColumn("MangaId").Ascending()
                .OnColumn("Date").Descending();

            // 2. (History.MangaId, ChapterId, Date DESC) — per-chapter history.
            Create.Index().OnTable("History")
                .OnColumn("MangaId").Ascending()
                .OnColumn("ChapterId").Ascending()
                .OnColumn("Date").Descending();

            // 3. (Blocklist.MangaId, Date DESC) — Blocklist views.
            Create.Index().OnTable("Blocklist")
                .OnColumn("MangaId").Ascending()
                .OnColumn("Date").Descending();

            // 4. (Chapters.MangaId, ChapterNumber, TranslatedLanguage) —
            //    Phase 5 selection path.
            Create.Index().OnTable("Chapters")
                .OnColumn("MangaId").Ascending()
                .OnColumn("ChapterNumber").Ascending()
                .OnColumn("TranslatedLanguage").Ascending();

            // 5. (Chapters.MangaId, TranslatedLanguage) — LANG-01 wanted-list filter.
            //    NOTE: leftmost-prefix from index #4 cannot cover this (TranslatedLanguage
            //    is third col, second col ChapterNumber blocks the prefix match). Two
            //    indexes are required. See Pitfall 3 in 01-RESEARCH.md.
            Create.Index().OnTable("Chapters")
                .OnColumn("MangaId").Ascending()
                .OnColumn("TranslatedLanguage").Ascending();

            // No seed data. The RefreshMangaCommand ScheduledTasks row is registered by
            // TaskManager.Handle(ApplicationStartedEvent) at runtime (Sonarr's canonical
            // pattern — see TaskManager.cs:65-166). Migrations create schema only.
            //
            // Phase 2 (folded 2026-05-02) originally inserted the row here per a misread
            // of D-18; the seed was deleted by TaskManager on every startup because
            // RefreshMangaCommand wasn't in TaskManager.defaultTasks. Quick task
            // 260502-3ip surfaced the divergence; corrected here + in TaskManager.

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 — ChapterHistory table (HISTORY-01..03; D-21 BL-01 fix).
            // Per dev-migration-policy.md edit-001 + Phase 6 D-21.
            // Parallel sibling to EpisodeHistory: ChapterId column is INDEPENDENT
            // of EpisodeHistory.EpisodeId (closes Phase 5 BL-01 cross-domain ID
            // collision). Phase 8 cleanup: collapse with EpisodeHistory when Tv/ deletes.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("ChapterHistory")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterId").AsInt32().NotNullable()
                .WithColumn("EventType").AsInt32().NotNullable()
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("SourceTitle").AsString().Nullable()
                .WithColumn("DownloadId").AsString().Nullable()
                .WithColumn("TranslatedLanguage").AsString().Nullable()
                .WithColumn("ScanlationGroup").AsString().Nullable()
                .WithColumn("SourceKey").AsString().Nullable()
                .WithColumn("ReleaseGuid").AsString().Nullable()
                .WithColumn("Data").AsString().Nullable()
                .WithColumn("Successful").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index("IX_ChapterHistory_ChapterId").OnTable("ChapterHistory").OnColumn("ChapterId");
            Create.Index("IX_ChapterHistory_MangaId_Date").OnTable("ChapterHistory")
                .OnColumn("MangaId").Ascending().OnColumn("Date").Descending();
            Create.Index("IX_ChapterHistory_DownloadId").OnTable("ChapterHistory").OnColumn("DownloadId");

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 — MangaBlocklist table (BLOCK-01..02; D-11 release identity).
            // Per dev-migration-policy.md edit-001 + Phase 6 D-11.
            // Release-level identity = (SourceKey, ReleaseGuid, Title) triple.
            // Phase 8 cleanup: collapse with Blocklist when Tv/ deletes.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("MangaBlocklist")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterIds").AsString().Nullable()
                .WithColumn("SourceTitle").AsString().NotNullable()
                .WithColumn("SourceKey").AsString().Nullable()
                .WithColumn("ReleaseGuid").AsString().Nullable()
                .WithColumn("ReleaseInfoJson").AsString().Nullable()
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("Reason").AsString().Nullable()
                .WithColumn("Source").AsString().Nullable();

            Create.Index("IX_MangaBlocklist_MangaId").OnTable("MangaBlocklist").OnColumn("MangaId");
            Create.Index("IX_MangaBlocklist_ReleaseGuid").OnTable("MangaBlocklist").OnColumn("ReleaseGuid");

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 — ChapterFile table (PIPELINE-04 import artifact).
            // Per dev-migration-policy.md edit-001 + Phase 6 PIPELINE-04.
            // Parallel sibling to EpisodeFile. Phase 8 cleanup: collapse with EpisodeFile.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("ChapterFiles")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterId").AsInt32().NotNullable()
                .WithColumn("RelativePath").AsString().NotNullable()
                .WithColumn("Path").AsString().NotNullable()
                .WithColumn("Size").AsInt64().NotNullable()
                .WithColumn("DateAdded").AsDateTime().NotNullable()
                .WithColumn("OriginalFilePath").AsString().Nullable()
                .WithColumn("TranslatedLanguage").AsString().Nullable()
                .WithColumn("ScanlationGroup").AsString().Nullable()
                .WithColumn("ReleaseGroup").AsString().Nullable();

            Create.Index("IX_ChapterFile_MangaId").OnTable("ChapterFiles").OnColumn("MangaId");
            Create.Index("IX_ChapterFile_ChapterId").OnTable("ChapterFiles").OnColumn("ChapterId");

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 D-10 — UpgradeAllowed flags + per-Manga override.
            // Per dev-migration-policy.md edit-001 + Phase 6 D-10.
            // Three-state semantics: Manga.UpgradeAllowedOverride NULL → fall back to
            // per-Profile UpgradeAllowed (TranslationProfile default TRUE — language rank
            // ordered, *arr promise; CustomFormatProfile default FALSE — manga CF scores
            // subjective, auto-churn risky).
            // ─────────────────────────────────────────────────────────────────────
            Alter.Table("Manga").AddColumn("UpgradeAllowedOverride").AsBoolean().Nullable();
            Alter.Table("TranslationProfiles").AddColumn("UpgradeAllowed").AsBoolean().NotNullable().WithDefaultValue(true);
            Alter.Table("CustomFormatProfiles").AddColumn("UpgradeAllowed").AsBoolean().NotNullable().WithDefaultValue(false);

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 PIPELINE-04 — Chapter ↔ ChapterFile FK (nullable; null = no file imported yet).
            // Sibling to Episodes.EpisodeFileId. Plan 06-07 UpgradeSpec + Plan 06-09 Wanted query consume.
            // ─────────────────────────────────────────────────────────────────────
            Alter.Table("Chapters").AddColumn("ChapterFileId").AsInt32().Nullable();
            Create.Index("IX_Chapter_ChapterFileId").OnTable("Chapters").OnColumn("ChapterFileId");

            // ─────────────────────────────────────────────────────────────────────
            // Phase 6 D-07 — per-IndexerDefinition SyncInterval override + LastRssSync timestamp.
            // SyncInterval default 0 means "use global Config.MangaRssSyncInterval".
            // Honored by Plan 06-06 MangaRssSyncService.
            // ─────────────────────────────────────────────────────────────────────
            Alter.Table("Indexers").AddColumn("SyncInterval").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("Indexers").AddColumn("LastRssSync").AsDateTime().Nullable();
        }

        protected override void LogDbUpgrade()
        {
            Create.TableForModel("Logs")
                .WithColumn("Message").AsString()
                .WithColumn("Time").AsDateTime()
                .WithColumn("Logger").AsString()
                .WithColumn("Method").AsString().Nullable()
                .WithColumn("Exception").AsString().Nullable()
                .WithColumn("ExceptionType").AsString().Nullable()
                .WithColumn("Level").AsString();
        }
    }
}
