using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.Authentication;
// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
// AutoTagging/ subtree DELETED (TV-only feature; auto-tagging may be rebuilt
// against manga shape in v1.x; not part of v1 scope per Phase 5 Out-of-scope).
//   using NzbDrone.Core.AutoTagging.Specifications; ← deleted
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
// Extras/ subtree DELETED (TV-only feature; manga has no Extras/Subtitles/Metadata-file concept).
//   using NzbDrone.Core.Extras.Metadata; ← deleted
//   using NzbDrone.Core.Extras.Metadata.Files; ← deleted
//   using NzbDrone.Core.Extras.Others; ← deleted
//   using NzbDrone.Core.Extras.Subtitles; ← deleted
using NzbDrone.Core.History;
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

            Mapper.Entity<ImportListDefinition>("ImportLists").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(i => i.ListType)
                  .Ignore(i => i.MinRefreshInterval)
                  .Ignore(i => i.Enable);

            Mapper.Entity<ImportListItemInfo>("ImportListItems").RegisterModel()
                   .Ignore(i => i.ImportList)
                   .Ignore(i => i.Seasons);

            Mapper.Entity<NotificationDefinition>("Notifications").RegisterModel()
                  .Ignore(x => x.ImplementationName)
                  .Ignore(i => i.SupportsOnGrab)
                  .Ignore(i => i.SupportsOnDownload)
                  .Ignore(i => i.SupportsOnImportComplete)
                  .Ignore(i => i.SupportsOnUpgrade)
                  .Ignore(i => i.SupportsOnRename)
                  .Ignore(i => i.SupportsOnSeriesAdd)
                  .Ignore(i => i.SupportsOnSeriesDelete)
                  .Ignore(i => i.SupportsOnEpisodeFileDelete)
                  .Ignore(i => i.SupportsOnEpisodeFileDeleteForUpgrade)
                  .Ignore(i => i.SupportsOnHealthIssue)
                  .Ignore(i => i.SupportsOnHealthRestored)
                  .Ignore(i => i.SupportsOnApplicationUpdate)
                  .Ignore(i => i.SupportsOnManualInteractionRequired)
                  .Ignore(i => i.SupportsOnChapterImport)
                  .Ignore(i => i.SupportsOnMangaAdd)
                  .Ignore(i => i.SupportsOnMangaDelete)
                  .Ignore(i => i.SupportsOnMangaRename);

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

            Mapper.Entity<SceneMapping>("SceneMappings").RegisterModel();

            Mapper.Entity<EpisodeHistory>("History").RegisterModel();

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
            Mapper.Entity<Blocklist>("Blocklist").RegisterModel();
            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // Extras/* file entity registrations stripped (Extras/ subtree DELETED).
            //   Mapper.Entity<MetadataFile>("MetadataFiles").RegisterModel();
            //   Mapper.Entity<SubtitleFile>("SubtitleFiles").RegisterModel();
            //   Mapper.Entity<OtherExtraFile>("ExtraFiles").RegisterModel();

            Mapper.Entity<PendingRelease>("PendingReleases").RegisterModel()
                  .Ignore(e => e.RemoteEpisode);

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
            Mapper.Entity<ImportListStatus>("ImportListStatus").RegisterModel();
            Mapper.Entity<NotificationStatus>("NotificationStatus").RegisterModel();

            Mapper.Entity<CustomFilter>("CustomFilters").RegisterModel();

            Mapper.Entity<DownloadHistory>("DownloadHistory").RegisterModel();

            Mapper.Entity<UpdateHistory>("UpdateHistory").RegisterModel();
            Mapper.Entity<ImportListExclusion>("ImportListExclusions").RegisterModel();

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // AutoTagging entity registration stripped (AutoTagging/ subtree DELETED).
            // The AutoTagging schema table remains in Migration 001 but is unmapped;
            // v1.x rebuild may rewire if/when manga auto-tagging lands.
            //   Mapper.Entity<AutoTagging.AutoTag>("AutoTagging").RegisterModel();
        }

        private static void RegisterMappers()
        {
            RegisterEmbeddedConverter();
            RegisterProviderSettingConverter();

            SqlMapper.RemoveTypeMap(typeof(DateTime));
            SqlMapper.AddTypeHandler(new DapperUtcConverter());
            SqlMapper.AddTypeHandler(new DapperQualityIntConverter());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<QualityProfileQualityItem>>(new QualityIntConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileFormatItem>>(new CustomFormatIntConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ICustomFormatSpecification>>(new CustomFormatSpecificationListConverter()));
            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
            // IAutoTaggingSpecification embedded converter stripped (AutoTagging/ DELETED).
            //   SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<IAutoTaggingSpecification>>(new AutoTaggingSpecificationConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<QualityModel>(new QualityIntConverter()));
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<Dictionary<string, string>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<IDictionary<string, string>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<KeyValuePair<string, int>>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<KeyValuePair<string, int>>());
            SqlMapper.AddTypeHandler(new DapperLanguageIntConverter());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<Language>>(new LanguageIntConverter()));
            SqlMapper.AddTypeHandler(new StringListConverter<List<string>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ParsedEpisodeInfo>(new QualityIntConverter(), new LanguageIntConverter()));

            // Phase 9 D-09-06..08 — ParsedChapterInfo Dapper round-trip handler for
            // MangaPendingReleases.ParsedChapterInfo column. ParsedChapterInfo carries only
            // primitive types (string, decimal[], int?, ChapterType enum) so no extra
            // type-handler args are needed — unlike TV's ParsedEpisodeInfo which embeds
            // QualityModel + Languages. Mirrors the precedent above for the TV analog.
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ParsedChapterInfo>());

            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<ReleaseInfo>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<PendingReleaseAdditionalInfo>());
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
