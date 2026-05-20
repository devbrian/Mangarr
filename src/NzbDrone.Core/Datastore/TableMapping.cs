// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
// using directives stripped:
//   using NzbDrone.Core.AutoTagging.Specifications; ← AutoTagging/ DELETED (TV-only)
//   using NzbDrone.Core.DataAugmentation.Scene; ← DataAugmentation/ DELETED per Plan 15-04
//   using NzbDrone.Core.Download.History; ← Download/History/ DELETED per Plan 15-10
//   using NzbDrone.Core.Extras.{Metadata,Metadata.Files,Others,Subtitles}; ← Extras/ DELETED
//   using NzbDrone.Core.History; ← History/EpisodeHistory.cs DELETED (manga peer: History/Manga/)
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Delay;

// using NzbDrone.Core.Profiles.Qualities; // Sonarr divergence: Phase 15 D-11 — Profiles/Qualities/ subtree deleted by Plan 15-03
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Profiles.Translations;

// using NzbDrone.Core.Qualities; // Sonarr divergence: Phase 15 D-11 — Qualities/ subtree deleted by Plan 15-03
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;
using NzbDrone.Core.ThingiProvider;

// using NzbDrone.Core.Tv; // Sonarr divergence: Phase 15 D-24 — Tv/ subtree deleted by Plan 15-03
using NzbDrone.Core.Update.History;
using static Dapper.SqlMapper;

namespace NzbDrone.Core.Datastore
{
    public static class TableMapping
    {
        static TableMapping()
        {
            Mapper = new TableMapper();
        }

        public static TableMapper Mapper { get; private set; }

        public static void Map()
        {
            // Idempotency guard — Phase 26 close-out. Without this, when subset
            // test runs (e.g. scripts/audit-new-fixtures.sh) execute test fixtures
            // in an ordering where BasicRepositoryRetryFixture / ProviderRepositoryRetryFixture
            // call Map() via their own [OneTimeSetUp] BEFORE DbFactory's cctor fires
            // for a DbTest-derived fixture, the second invocation throws
            // ArgumentException ("Key Config already added") from TableMap.Add at line
            // below. Production cctor only fires once per AppDomain, so this guard
            // is a no-op outside the test harness.
            if (Mapper.TableMap.Count > 0)
            {
                return;
            }

            RegisterMappers();

            Mapper.Entity<Config>("Config").RegisterModel();

            Mapper.Entity<RootFolder>("RootFolders").RegisterModel()
                  .Ignore(r => r.Accessible)
                  .Ignore(r => r.IsEmpty)
                  .Ignore(r => r.FreeSpace)
                  .Ignore(r => r.TotalSpace);

            Mapper.Entity<ScheduledTask>("ScheduledTasks").RegisterModel()
                  .Ignore(i => i.Priority);

            Mapper.Entity<IndexerDefinition>("Indexers").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(i => i.Enable)
                  .Ignore(i => i.Protocol)
                  .Ignore(i => i.SupportsRss)
                  .Ignore(i => i.SupportsSearch);

            // Phase 26 Plan 26-04 (IL-02) — RESTORED. ImportListDefinition entity
            // registration mirrors the Sonarr-canonical Indexers row above (line 79-84):
            // ImplementationName / Enable / ListType / MinRefreshInterval ride
            // [MemberwiseEqualityIgnore] on the POCO and are Ignore'd here so Dapper
            // doesn't try to read non-existent columns. The Settings column is JSON-
            // hydrated by ProviderRepository<T>.Query (CR-02 retry funnel).
            Mapper.Entity<ImportListDefinition>("ImportLists").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(i => i.ListType)
                  .Ignore(i => i.MinRefreshInterval)
                  .Ignore(i => i.Enable);

            // Phase 26 Plan 26-04 (IL-02) — RESTORED. ImportListItemInfo registers to the
            // `ImportListItems` table reshaped by Migration 003 (003_v1_1_importlist_
            // substrate_delayprofile_trim.cs:46-49). `ImportList` is the friendly-name
            // cache populated at fetch time (not persisted).
            Mapper.Entity<ImportListItemInfo>("ImportListItems").RegisterModel()
                  .Ignore(i => i.ImportList);

            Mapper.Entity<NotificationDefinition>("Notifications").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(i => i.SupportsOnGrab)
                  .Ignore(i => i.SupportsOnDownload)
                  .Ignore(i => i.SupportsOnImportComplete)
                  .Ignore(i => i.SupportsOnUpgrade)
                  .Ignore(i => i.SupportsOnRename)
                  .Ignore(i => i.SupportsOnHealthIssue)
                  .Ignore(i => i.SupportsOnHealthRestored)
                  .Ignore(i => i.SupportsOnApplicationUpdate)
                  .Ignore(i => i.SupportsOnManualInteractionRequired)
                  .Ignore(i => i.SupportsOnChapterImport)
                  .Ignore(i => i.SupportsOnMangaAdd)
                  .Ignore(i => i.SupportsOnMangaDelete)
                  .Ignore(i => i.SupportsOnMangaRename)
                  .Ignore(i => i.SupportsOnChapterFileDelete)
                  .Ignore(i => i.SupportsOnChapterFileDeleteForUpgrade);

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // MetadataDefinition entity registration stripped (Extras/Metadata/ deleted).
            //   Mapper.Entity<MetadataDefinition>("Metadata").RegisterModel()
            //         .Ignore(x => x.ImplementationName)
            //         .Ignore(d => d.Tags);

            // Phase 2 (Plan 02-11) — IMetadataSource ProviderDefinition. Distinct from
            // the Sonarr-inherited Metadata IMetadataConsumer table above (Pitfall 2).
            // IsPrimary IS a real DB column (consolidated Migration 001) and MUST round-trip;
            // the at-most-one invariant is enforced in MetadataSourceFactory.SetPrimary,
            // not the DB. Tags rides the existing global EmbeddedDocumentConverter<HashSet<int>>
            // registered in RegisterMappers (line 210) — same pattern as IndexerDefinition.Tags.
            Mapper.Entity<MetadataSourceDefinition>("MetadataSources").RegisterModel()
                  .Ignore(x => x.ImplementationName);

            Mapper.Entity<DownloadClientDefinition>("DownloadClients").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(d => d.Protocol);

            // Sonarr divergence: Phase 15 Plan 15-04 cascade absorption — SceneMapping entity registration stripped (DataAugmentation/Scene/ DELETED).
            //   Mapper.Entity<SceneMapping>("SceneMappings").RegisterModel();

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // EpisodeHistory entity registration stripped (History/EpisodeHistory.cs DELETED).
            // The History schema table is dropped per Phase 15 D-22 (Migration 001 baseline);
            // manga peer ChapterHistory uses a separate ChapterHistory table.
            //   Mapper.Entity<EpisodeHistory>("History").RegisterModel();

            // Sonarr divergence: Phase 15 D-24 schema delete — TV-shape entity registrations stripped
            //   Mapper.Entity<Series>("Series").RegisterModel(); ← deleted (Plan 15-03)
            //   Mapper.Entity<EpisodeFile>("EpisodeFiles").RegisterModel(); ← deleted (Plan 15-03)
            //   Mapper.Entity<Episode>("Episodes").RegisterModel(); ← deleted (Plan 15-03)
            //   Mapper.Entity<QualityDefinition>("QualityDefinitions").RegisterModel(); ← deleted (Plan 15-03 per D-11)
            //   Mapper.Entity<QualityProfile>("QualityProfiles").RegisterModel(); ← deleted (Plan 15-03 per D-11)

            // Phase 2 manga domain (02-02) — Manga aggregate root + flat Chapter list.
            // Manga.MangaDexId is Guid?; Dapper round-trips through the global
            // GuidConverter registered in RegisterMappers (Sonarr precedent reused
            // verbatim, originally added for Users.Identifier).
            Mapper.Entity<Core.Manga.Manga>("Manga").RegisterModel();
            Mapper.Entity<Core.Manga.Chapter>("Chapters").RegisterModel();

            // Sonarr divergence: Phase 15 D-11 schema delete — QualityDefinition entity registration stripped (Plan 15-03)

            Mapper.Entity<CustomFormat>("CustomFormats").RegisterModel();

            // Sonarr divergence: Phase 15 D-11 schema delete — QualityProfile entity registration stripped (Plan 15-03)

            // Phase 5 D-01 — TranslationProfile entity registration. Sibling to QualityProfiles.
            Mapper.Entity<TranslationProfile>("TranslationProfiles").RegisterModel();

            // Phase 5 D-07 — CustomFormatProfile entity registration. Sibling to TranslationProfiles.
            Mapper.Entity<CustomFormatProfile>("CustomFormatProfiles").RegisterModel();

            Mapper.Entity<Log>("Logs").RegisterModel();
            Mapper.Entity<NamingConfig>("NamingConfig").RegisterModel();

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // TV Blocklist entity registration stripped (Blocklisting/{Blocklist,Repository,Service}.cs DELETED).
            // The Blocklist schema table is dropped per Phase 15 D-22 (Migration 001 baseline);
            // manga peer MangaBlocklist registers below at line 229.
            //   Mapper.Entity<Blocklist>("Blocklist").RegisterModel();
            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // Extras/* file entity registrations stripped (Extras/ subtree DELETED).
            //   Mapper.Entity<MetadataFile>("MetadataFiles").RegisterModel();
            //   Mapper.Entity<SubtitleFile>("SubtitleFiles").RegisterModel();
            //   Mapper.Entity<OtherExtraFile>("ExtraFiles").RegisterModel();

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — PendingRelease entity
            // registration stripped (Download/Pending/PendingRelease.cs DELETED). Manga peer
            // MangaPendingRelease registers below.
            //   Mapper.Entity<PendingRelease>("PendingReleases").RegisterModel()
            //         .Ignore(e => e.RemoteEpisode);

            // Phase 9 D-09-06..08 — MangaPendingRelease registration (parallel sibling to
            // PendingRelease registered above). BL-01 mechanical guard: a separate Mapper.Entity
            // binding to "MangaPendingReleases" makes it physically impossible to leak rows from
            // the TV-side PendingReleases table even when MangaId / SeriesId int values collide.
            // Mirrors MangaBlocklist precedent (line 220 below) and ChapterHistory precedent
            // (line 211 below). RemoteChapter is not persisted — populated by service projection
            // (Plan 09-10), mirroring the TV PendingRelease.RemoteEpisode .Ignore above.
            Mapper.Entity<MangaPendingRelease>("MangaPendingReleases").RegisterModel()
                  .Ignore(e => e.RemoteChapter);

            Mapper.Entity<RemotePathMapping>("RemotePathMappings").RegisterModel();
            Mapper.Entity<Tag>("Tags").RegisterModel();
            Mapper.Entity<ReleaseProfile>("ReleaseProfiles").RegisterModel();

            Mapper.Entity<DelayProfile>("DelayProfiles").RegisterModel();
            Mapper.Entity<User>("Users").RegisterModel();
            Mapper.Entity<CommandModel>("Commands").RegisterModel()
                .Ignore(c => c.Message);

            Mapper.Entity<IndexerStatus>("IndexerStatus").RegisterModel();

            // Phase 3 D-17 — per-SourceKey indexer status (sibling to IndexerStatus above).
            Mapper.Entity<IndexerSourceStatus>("IndexerSourceStatus").RegisterModel();

            // Phase 4 D-05 — in-flight chapter download state (own ModelBase; per dev-migration-policy.md).
            Mapper.Entity<ChapterDownloadState>("ChapterDownloadState").RegisterModel();

            // Phase 6 PIPELINE-04 — ChapterFile registration (parallel sibling to EpisodeFile).
            // ChapterHistory + MangaBlocklist registrations live in Plans 06-03 + 06-04 respectively
            // (each plan registers its own type in the same commit that introduces the type — keeps
            // every plan boundary green per Anti-Pattern C compliance).
            Mapper.Entity<ChapterFile>("ChapterFiles").RegisterModel();

            // Phase 6 D-21 (Plan 06-03) — ChapterHistory registration (parallel sibling to
            // EpisodeHistory above at line 129). BL-01 fix: ChapterId column is independent of
            // EpisodeHistory.EpisodeId — see History/Manga/ChapterHistory.cs header.
            Mapper.Entity<ChapterHistory>("ChapterHistory").RegisterModel();

            // Phase 6 D-11 + D-19 (Plan 06-04) — MangaBlocklist registration (parallel sibling
            // to Blocklist registered at line 176). Release-identity = (SourceKey, ReleaseGuid,
            // SourceTitle) triple. The separate registration is the BL-01-style mechanical
            // guarantee — Dapper Query<MangaBlocklist> cannot leak rows from the TV Blocklist
            // table even when MangaId / SeriesId int values collide.
            Mapper.Entity<MangaBlocklist>("MangaBlocklist").RegisterModel();

            Mapper.Entity<DownloadClientStatus>("DownloadClientStatus").RegisterModel();

            // Phase 26 Plan 26-04 (IL-02) — RESTORED. ImportListStatus row backs the
            // ProviderStatusServiceBase escalation/backoff machinery for IMangaImportList
            // providers (D-13 — 1 of 3 separate Dapper repos).
            Mapper.Entity<ImportListStatus>("ImportListStatus").RegisterModel();
            Mapper.Entity<NotificationStatus>("NotificationStatus").RegisterModel();

            Mapper.Entity<CustomFilter>("CustomFilters").RegisterModel();

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // DownloadHistory entity registration stripped (Download/History/ DELETED).
            //   Mapper.Entity<DownloadHistory>("DownloadHistory").RegisterModel();

            Mapper.Entity<UpdateHistory>("UpdateHistory").RegisterModel();

            // Phase 26 Plan 26-04 (IL-06) — RESTORED. ImportListExclusion is the manga-ID
            // triplet exclusion row populated by ImportListExclusionService.Handle on
            // MangaDeletedEvent (D-12 event-driven auto-add). Schema reshaped by
            // Migration 003 (TvdbId → MangaDexId/MalId/AniListId).
            Mapper.Entity<ImportListExclusion>("ImportListExclusions").RegisterModel();

            // Phase 24 v1.1 INSERTED 2026-05-17 — AutoTagging restored (Wave 24-02).
            // The AutoTagging schema table has lived in Migration 001 since the Phase 15
            // baseline; Plan 24-02 re-wires the entity + repository + service substrate.
            Mapper.Entity<AutoTagging.AutoTag>("AutoTagging").RegisterModel();
        }

        private static void RegisterMappers()
        {
            RegisterEmbeddedConverter();
            RegisterProviderSettingConverter();

            SqlMapper.RemoveTypeMap(typeof(DateTime));
            SqlMapper.AddTypeHandler(new DapperUtcConverter());

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // Quality / QualityModel / QualityProfileQualityItem / ParsedEpisodeInfo embedded
            // converters stripped per Plan 15-03 Tv/+Quality DELETE + Plan 15-10 Parser/Model
            // TV DELETE. Manga uses ParsedChapterInfo round-trip (registered below).
            //   SqlMapper.AddTypeHandler(new DapperQualityIntConverter());
            //   SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<QualityProfileQualityItem>>(new QualityIntConverter()));
            //   SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<QualityModel>(new QualityIntConverter()));
            //   SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ParsedEpisodeInfo>(new QualityIntConverter(), new LanguageIntConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileFormatItem>>(new CustomFormatIntConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ICustomFormatSpecification>>(new CustomFormatSpecificationListConverter()));

            // Phase 24 v1.1 INSERTED 2026-05-17 — AutoTagging List<IAutoTaggingSpecification>
            // Dapper round-trip via the polymorphic AutoTaggingSpecificationConverter (Plan 24-02).
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<AutoTagging.Specifications.IAutoTaggingSpecification>>(new AutoTaggingSpecificationConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<Dictionary<string, string>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<IDictionary<string, string>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<KeyValuePair<string, int>>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<KeyValuePair<string, int>>());
            SqlMapper.AddTypeHandler(new DapperLanguageIntConverter());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<Language>>(new LanguageIntConverter()));
            SqlMapper.AddTypeHandler(new StringListConverter<List<string>>());

            // Sonarr divergence: ParsedEpisodeInfo handler stripped above; ParsedChapterInfo handler registered below.

            // Phase 9 D-09-06..08 — ParsedChapterInfo Dapper round-trip handler for
            // MangaPendingReleases.ParsedChapterInfo column. ParsedChapterInfo carries only
            // primitive types (string, decimal[], int?, ChapterType enum) so no extra
            // type-handler args are needed — unlike TV's ParsedEpisodeInfo which embeds
            // QualityModel + Languages. Mirrors the precedent above for the TV analog.
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ParsedChapterInfo>());

            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ReleaseInfo>());

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — PendingReleaseAdditionalInfo
            // converter stripped (Download/Pending/PendingRelease.cs DELETED).
            //   SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<PendingReleaseAdditionalInfo>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<HashSet<int>>());
            SqlMapper.AddTypeHandler(new OsPathConverter());
            SqlMapper.RemoveTypeMap(typeof(Guid));
            SqlMapper.RemoveTypeMap(typeof(Guid?));
            SqlMapper.AddTypeHandler(new GuidConverter());
            SqlMapper.RemoveTypeMap(typeof(TimeSpan));
            SqlMapper.RemoveTypeMap(typeof(TimeSpan?));
            SqlMapper.AddTypeHandler(new TimeSpanConverter());
            SqlMapper.AddTypeHandler(new CommandConverter());
            SqlMapper.AddTypeHandler(new SystemVersionConverter());
        }

        private static void RegisterProviderSettingConverter()
        {
            var settingTypes = typeof(IProviderConfig).Assembly.ImplementationsOf<IProviderConfig>()
                .Where(x => !x.ContainsGenericParameters);

            var providerSettingConverter = new ProviderSettingConverter();
            foreach (var embeddedType in settingTypes)
            {
                SqlMapper.AddTypeHandler(embeddedType, providerSettingConverter);
            }
        }

        private static void RegisterEmbeddedConverter()
        {
            var embeddedTypes = typeof(IEmbeddedDocument).Assembly.ImplementationsOf<IEmbeddedDocument>();

            var embeddedConverterDefinition = typeof(EmbeddedDocumentConverter<>).GetGenericTypeDefinition();
            var genericListDefinition = typeof(List<>).GetGenericTypeDefinition();

            foreach (var embeddedType in embeddedTypes)
            {
                var embeddedListType = genericListDefinition.MakeGenericType(embeddedType);

                RegisterEmbeddedConverter(embeddedType, embeddedConverterDefinition);
                RegisterEmbeddedConverter(embeddedListType, embeddedConverterDefinition);
            }
        }

        private static void RegisterEmbeddedConverter(Type embeddedType, Type embeddedConverterDefinition)
        {
            var embeddedConverterType = embeddedConverterDefinition.MakeGenericType(embeddedType);
            var converter = (ITypeHandler)Activator.CreateInstance(embeddedConverterType);

            SqlMapper.AddTypeHandler(embeddedType, converter);
        }
    }
}
