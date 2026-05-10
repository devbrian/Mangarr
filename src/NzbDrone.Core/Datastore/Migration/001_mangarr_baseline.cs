using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Phase 1 baseline migration. Replaces Sonarr's 224 inherited TV-shaped migrations
    // with a single fresh manga-shaped baseline.
    //
    // Phase 15 D-22 schema delete (header — see Plan 15-02 SUMMARY for full table+column list)
    // executed by Plan 15-02 — this baseline is now manga-shaped end-to-end:
    //   * TV domain tables (Series / Seasons / Episodes / EpisodeFiles) — DROPPED per D-22.
    //     Sit-empty-at-runtime model retired; Tv/ C# subtree deletion lands at Plan 15-03.
    //   * Quality-keyed tables (QualityDefinitions / QualityProfiles) — DROPPED per D-11/D-22
    //     (Quality cascade; manga has no resolution concept; replaced by TranslationProfiles +
    //     CustomFormatProfiles).
    //   * TV-only side files (MetadataFiles / SubtitleFiles / ExtraFiles / SceneMappings) —
    //     DROPPED per D-19/D-22/D-24 (TV-keyed; SceneMappings is anime-numbering-only).
    //   * TV-only columns on KEEP tables (Notifications.OnSeries* / OnEpisodeFile*,
    //     Indexers.SeasonSearchMaximumSingleEpisodeAge, NamingConfig.*EpisodeFormat /
    //     SeriesFolderFormat / SeasonFolderFormat, ImportLists.SeriesType / SeasonFolder,
    //     ImportListItems.TvdbId, History.SeriesId / EpisodeId, Blocklist.SeriesId /
    //     EpisodeIds) — DROPPED per D-22.
    //   * Manga tables (Manga / Chapters / ChapterFiles / ChapterHistory / MangaBlocklist /
    //     MangaPendingReleases) and ThingiProvider tables (Indexers / DownloadClients /
    //     Notifications / Metadata / MetadataSources / ImportLists / etc.) — KEPT per D-03.
    //   * History.MangaId / ChapterId + Blocklist.MangaId / ChapterId — flipped to
    //     NotNullable() per D-23 (every row IS manga post-Phase-15).
    //   * Five locked composite indexes from Phase 0 D-15 — KEPT.
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
            // ThingiProvider tables (D-03 — verbatim recreation). TV-specific
            // columns previously flagged at this site were stripped by Phase 15
            // Plan 15-02 per D-22 schema delete.
            // ─────────────────────────────────────────────────────────────────────
            // Phase 15 D-22 schema delete — Indexers.SeasonSearchMaximumSingleEpisodeAge column dropped (executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — stripped Indexers.SeasonSearchMaximumSingleEpisodeAge column (TV-only; manga indexers register against this same table per D-03)
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
                .WithColumn("Tags").AsString().Nullable();

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

            // Phase 15 D-22 schema delete (executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — stripped Notifications.OnSeriesAdd / OnSeriesDelete / OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade columns (TV-only).
            // Manga sibling columns added at end: OnMangaAdd / OnMangaDelete / OnMangaRename (Phase 8 Plan 99-08) and OnChapterFileDelete / OnChapterFileDeleteForUpgrade (post-15 surface backfill).
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
                .WithColumn("OnHealthIssue").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("IncludeHealthWarnings").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnApplicationUpdate").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnManualInteractionRequired").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnHealthRestored").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnImportComplete").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnChapterImport").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnMangaAdd").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnMangaDelete").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnMangaRename").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnChapterFileDelete").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnChapterFileDeleteForUpgrade").AsBoolean().NotNullable().WithDefaultValue(true);

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

            // Phase 15 D-22 schema delete (executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — stripped ImportLists.SeriesType / SeasonFolder columns (TV-only). Note: the ImportLists table itself will be dropped by a follow-up Phase 15 plan alongside the D-26 import-lists/ Reference Preservation MOVE — Plan 15-02 only removes the TV columns; the table survives this wave.
            Create.TableForModel("ImportLists")
                .WithColumn("Name").AsString().Unique()
                .WithColumn("Implementation").AsString()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("EnableAutomaticAdd").AsBoolean()
                .WithColumn("RootFolderPath").AsString()
                .WithColumn("ShouldMonitor").AsInt32()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("MonitorNewItems").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("SearchForMissingEpisodes").AsBoolean().NotNullable().WithDefaultValue(true); // migration 197

            // Sonarr divergence: Phase 15 D-22 schema delete — stripped ImportListItems.TvdbId column (D-17 SkyHook delete cascade — TVDB ID lookup retired)
            Create.TableForModel("ImportListItems")
                .WithColumn("ImplementationName").AsString()
                .WithColumn("ImportListId").AsInt32()
                .WithColumn("ServiceProviderId").AsInt32()
                .WithColumn("Title").AsString()
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("ReleaseDate").AsDateTime();

            Create.TableForModel("Tags")
                .WithColumn("Label").AsString().Unique();

            // Phase 15 D-22 schema delete (executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — stripped NamingConfig.MultiEpisodeStyle / StandardEpisodeFormat / DailyEpisodeFormat / SeriesFolderFormat / SeasonFolderFormat / AnimeEpisodeFormat columns (TV-only; manga uses StandardChapterFormat / MangaFolderFormat / RenameChapters)
            // ─────────────────────────────────────────────────────────────────────
            // Phase 5 — NamingConfig manga columns.
            // Per dev-migration-policy.md edit-001 + Phase 5 D-13 (extend existing NamingConfig
            // singleton; DO NOT ship a sibling MangaNamingConfig table — D-04 invariant: ONE
            // singleton, no Library entity). Phase 15 D-22 stripped TV-shaped columns; manga
            // naming columns (StandardChapterFormat / MangaFolderFormat / RenameChapters) carry
            // the file-renaming behavior end-to-end post-cutover.
            // ─────────────────────────────────────────────────────────────────────
            Create.TableForModel("NamingConfig")
                .WithColumn("RenameEpisodes").AsBoolean().Nullable()
                .WithColumn("ReplaceIllegalCharacters").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("SpecialsFolderFormat").AsString().NotNullable().WithDefaultValue("Specials")
                .WithColumn("ColonReplacementFormat").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CustomColonReplacementFormat").AsString().NotNullable().WithDefaultValue(string.Empty)
                .WithColumn("StandardChapterFormat").AsString().Nullable()
                .WithColumn("MangaFolderFormat").AsString().Nullable()
                .WithColumn("RenameChapters").AsBoolean().NotNullable().WithDefaultValue(false);

            // ─────────────────────────────────────────────────────────────────────
            // Phase 15 D-22 schema delete (executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — `Series` table dropped (TV-only; manga uses `Manga` table created below)
            // Sonarr divergence: Phase 15 D-22 schema delete — `Seasons` table dropped (TV-only; manga has no season concept per DOMAIN-02 — flat chapter list)
            // Sonarr divergence: Phase 15 D-22 schema delete — `Episodes` table dropped (TV-only; manga uses `Chapters` table created below)
            // Sonarr divergence: Phase 15 D-22 schema delete — `EpisodeFiles` table dropped (TV-only; manga uses `ChapterFiles` table created below)
            // The Tv/ C# subtree delete (Series.cs / Episode.cs / Season.cs / EpisodeFile.cs et al.)
            // lands at Plan 15-03 Wave 1b — the compile cascade follows the schema delete by one
            // wave per Phase 14 dress-rehearsal Wave 1a finding (commit 79d669c3a; build remained
            // GREEN here because Tv/ types reference C# classes, NOT migration columns).
            // ─────────────────────────────────────────────────────────────────────

            // Sonarr divergence: Phase 15 D-22 schema delete — History.SeriesId / EpisodeId columns dropped (TV-only); History row identity carries through MangaId / ChapterId columns added below (D-23 NotNullable per Phase 15)
            Create.TableForModel("History")
                .WithColumn("SourceTitle").AsString()
                .WithColumn("Quality").AsString()
                .WithColumn("Date").AsDateTime()
                .WithColumn("Data").AsString()
                .WithColumn("EventType").AsInt32().Nullable()
                .WithColumn("DownloadId").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable().WithDefaultValue("[]");

            // Sonarr divergence: Phase 15 D-22 schema delete — Blocklist.SeriesId / EpisodeIds columns dropped (TV-only); Blocklist row identity carries through MangaId / ChapterId columns added below (D-23 NotNullable per Phase 15)
            Create.TableForModel("Blocklist")
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
            // Sonarr divergence: Phase 15 D-22 schema delete — `QualityDefinitions` table dropped (TV-only resolution tiers; manga has no resolution concept per Phase 0 D-11 Quality-cascade)
            // Sonarr divergence: Phase 15 D-22 schema delete — `QualityProfiles` table dropped (TV-only; manga uses `TranslationProfiles` (ordinal language preference) + `CustomFormatProfiles` (CF score thresholds) per Phase 5 D-01 + D-07)
            // ─────────────────────────────────────────────────────────────────────

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
                .WithColumn("HttpDelay").AsInt32().NotNullable().WithDefaultValue(0)
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

            // Phase 9 D-09-06..08 INSERT
            // Manga sibling per Phase 9 D-09-06..08 (RESEARCH §A1 sibling-table preference).
            // Mirror MangaBlocklist / ChapterHistory sibling-table precedent already in this file.
            // Per Open Q §5: MangaId NOT NULL day one (mirror Phase 14 D-23 intent).
            // Pre-v1 dev-migration-policy: edit-001-in-place; fresh DB required to pick up.
            Create.TableForModel("MangaPendingReleases")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("Added").AsDateTime().NotNullable()
                .WithColumn("ParsedChapterInfo").AsString().NotNullable()
                .WithColumn("Release").AsString().NotNullable()
                .WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0);

                // No AdditionalInfo column — TV's PendingReleaseAdditionalInfo holds SeriesMatchType +
                // ReleaseSource; manga has no analog (per D-09-06 manga-shape). See PATTERNS §7 POCO note.

            // END Phase 9 INSERT

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

            // Sonarr divergence: 2026-05-08 fix-forward — `UpdateHistory` table
            // creation moved to LogDbUpgrade() below. The repository
            // (UpdateHistoryRepository) is constructed against ILogDatabase, so the
            // table must live in logs.db, not the main DB. Previously the table was
            // (incorrectly) created here in MainDbUpgrade() — see debug note
            // .planning/debug/update-history-table-missing.md.

            Create.TableForModel("ImportListExclusions")
                .WithColumn("TvdbId").AsInt32().Unique()
                .WithColumn("Title").AsString().NotNullable();

            // Phase 15 D-22 schema delete — MetadataFiles + SubtitleFiles + ExtraFiles tables dropped (TV-only per D-24; executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — `MetadataFiles` table dropped (TV-keyed via SeriesId/EpisodeFileId/EpisodeId; manga has no Kodi/Roksbox/Wdtv consumer pipeline; v2 may add manga peer if reader-metadata pull surfaces)
            // Sonarr divergence: Phase 15 D-22 schema delete — `SubtitleFiles` table dropped (TV-only per D-24; manga has no subtitle concept)
            // Sonarr divergence: Phase 15 D-22 schema delete — `ExtraFiles` table dropped (TV-only per D-24; manga has no extra-files concept)

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

            // Phase 15 D-19 retagged — SceneMappings table dropped (TV anime-numbering; executed by Plan 15-02)
            // Sonarr divergence: Phase 15 D-22 schema delete — `SceneMappings` table dropped (D-19; TheXem anime-TV scene-numbering only — manga uses scanlation groups; no scene-number concept)

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
                .WithColumn("TitleSlug").AsString().Nullable()              // Phase 8 audit gap-04 — mirrors Series.TitleSlug at line 194; populated via StringExtensions.ToUrlSlug() in AddMangaService.PrepareForAdd; frontend /manga/:titleSlug route consumer.
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
                .WithColumn("CustomFormatProfileId").AsInt32().Nullable()   // Phase 5 D-07 — null = fall back to Config.DefaultCustomFormatProfileId
                .WithColumn("MonitorNewItems").AsInt32().NotNullable().WithDefaultValue(0) // Issue #28 — MangaMonitorNewItems enum (All=0, None=1); per-Manga "auto-monitor new items on subsequent refresh / RSS" flag; mirrors Sonarr migration 200 + Series.MonitorNewItems
                .WithColumn("AddOptions").AsString().Nullable();            // Phase 8 audit gap-03 — JSON column (AddMangaOptions IEmbeddedDocument); mirrors Series.AddOptions shape at line 219.

            Create.TableForModel("Chapters")
                .WithColumn("MangaId").AsInt32().NotNullable()
                .WithColumn("ChapterNumber").AsDecimal(10, 3).NotNullable()         // D-12 applied at baseline (was DECIMAL(10,2))
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("FirstReleaseDate").AsDateTime().Nullable()                           // Phase 16 D-02 (kept Phase 16.1 — Sonarr-mirror of Episode.AirDateUtc; chapter-publish date)
                .WithColumn("Monitored").AsBoolean().NotNullable()
                .WithColumn("ExternalId").AsString().Nullable()
                .WithColumn("ChapterType").AsString().NotNullable().WithDefaultValue("Regular")  // folded from 002 (D-11)
                .WithColumn("VolumeNumber").AsInt32().Nullable()                                  // folded from 002 (D-11)
                .WithColumn("AbsoluteChapterNumber").AsDecimal(10, 3).Nullable()                  // folded from 002 (D-11)
                .WithColumn("LastSearchTime").AsDateTime().Nullable();                            // Phase 8 audit gap-07 — search-history per chapter (sibling of Episodes.LastSearchTime)

            // ─────────────────────────────────────────────────────────────────────
            // History/Blocklist manga columns (D-07 added; D-23 flipped to NotNullable per Phase 15).
            // Sonarr divergence: Phase 15 D-23 — History.MangaId / History.ChapterId / Blocklist.MangaId / Blocklist.ChapterId flipped to NotNullable() (every row IS manga post-Phase-15; NULL becomes meaningless. Pre-v1 fresh-DB rule means no existing data can violate; cascade-delete on Manga delete matches Sonarr's pattern.)
            // ─────────────────────────────────────────────────────────────────────
            Alter.Table("History")
                .AddColumn("MangaId").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("ChapterId").AsInt32().NotNullable().WithDefaultValue(0);

            Alter.Table("Blocklist")
                .AddColumn("MangaId").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("ChapterId").AsInt32().NotNullable().WithDefaultValue(0);

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

            // 4. Phase 16 STRUCT-01: composite UNIQUE on canonical Chapter grain — replaces the
            //    language-keyed indexes #4 + #5 from the pre-Phase-16 shape. First composite-UNIQUE
            //    in the codebase (FluentMigrator DSL verified against SQLite + PostgreSQL per
            //    RESEARCH Pitfall 6 / Assumption A1).
            Create.Index("IX_Chapters_MangaId_ChapterNumber").OnTable("Chapters")
                .OnColumn("MangaId").Ascending()
                .OnColumn("ChapterNumber").Ascending()
                .WithOptions().Unique();

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
                .WithColumn("ScanlationGroup").AsString().Nullable();

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

            // UpdateHistory lives in logs.db because UpdateHistoryRepository
            // takes ILogDatabase. Sonarr's upstream baseline creates it here
            // (Sonarr's 200_log_database migration); the Phase 15 schema
            // consolidation initially placed it in MainDbUpgrade by mistake —
            // restored to LogDbUpgrade 2026-05-08. See debug note
            // .planning/debug/update-history-table-missing.md.
            Create.TableForModel("UpdateHistory")
                .WithColumn("Date").AsDateTime().NotNullable()
                .WithColumn("Version").AsString().NotNullable()
                .WithColumn("EventType").AsInt32().NotNullable();
        }
    }
}
