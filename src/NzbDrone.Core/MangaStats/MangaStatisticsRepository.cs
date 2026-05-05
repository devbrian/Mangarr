using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MangaStats
{
    public interface IMangaStatisticsRepository
    {
        List<MangaStatistics> MangaStatistics();
        List<MangaStatistics> MangaStatistics(int mangaId);
    }

    // Audit no-sibling/SeriesStatisticsRepository (Phase 8 11-02). Mirrors TV
    // SeriesStatisticsRepository.cs: Dapper SqlBuilder over Chapters + ChapterFiles,
    // aggregated by MangaId only (no Seasons join per D-13). Two passes (chapter counts
    // + file rollups) merged in MapResults so the GROUP_CONCAT aggregations are split
    // into typed List<> properties before returning to callers.
    public class MangaStatisticsRepository : IMangaStatisticsRepository
    {
        private const string _selectChaptersTemplate = "SELECT /**select**/ FROM \"Chapters\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";
        private const string _selectChapterFilesTemplate = "SELECT /**select**/ FROM \"ChapterFiles\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";

        private readonly IMainDatabase _database;

        public MangaStatisticsRepository(IMainDatabase database)
        {
            _database = database;
        }

        public List<MangaStatistics> MangaStatistics()
        {
            var time = DateTime.UtcNow;
            return MapResults(Query<MangaStatistics>(ChaptersBuilder(time), _selectChaptersTemplate),
                Query<ChapterFileRollup>(ChapterFilesBuilder(), _selectChapterFilesTemplate));
        }

        public List<MangaStatistics> MangaStatistics(int mangaId)
        {
            var time = DateTime.UtcNow;

            return MapResults(Query<MangaStatistics>(ChaptersBuilder(time).Where<Chapter>(x => x.MangaId == mangaId), _selectChaptersTemplate),
                Query<ChapterFileRollup>(ChapterFilesBuilder().Where<ChapterFile>(x => x.MangaId == mangaId), _selectChapterFilesTemplate));
        }

        private List<MangaStatistics> MapResults(List<MangaStatistics> chaptersResult, List<ChapterFileRollup> filesResult)
        {
            chaptersResult.ForEach(c =>
            {
                var file = filesResult.SingleOrDefault(f => f.MangaId == c.MangaId);

                c.SizeOnDisk = file?.SizeOnDisk ?? 0;
                c.ScanlationGroups = SplitScanlationGroups(file?.ScanlationGroupsString);
                c.TranslatedLanguages = SplitTranslatedLanguages(file?.TranslatedLanguagesString);
            });

            return chaptersResult;
        }

        private static List<string> SplitScanlationGroups(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            return raw
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
        }

        private static List<Language> SplitTranslatedLanguages(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return new List<Language>();
            }

            return raw
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .Select(IsoLanguages.Find)
                .Where(iso => iso != null)
                .Select(iso => iso.Language)
                .ToList();
        }

        private List<T> Query<T>(SqlBuilder builder, string template)
        {
            var sql = builder.AddTemplate(template).LogQuery();

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<T>(sql.RawSql, sql.Parameters).ToList();
            }
        }

        private SqlBuilder ChaptersBuilder(DateTime currentDate)
        {
            var parameters = new DynamicParameters();
            parameters.Add("currentDate", currentDate, null);

            var trueIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "true" : "1";
            var falseIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "false" : "0";

            return new SqlBuilder(_database.DatabaseType)
                .Select($@"""Chapters"".""MangaId"" AS MangaId,
                             COUNT(*) AS TotalChapterCount,
                             SUM(CASE WHEN (""Monitored"" = {trueIndicator} AND ""ReleaseDate"" <= @currentDate) OR ""ChapterFileId"" > 0 THEN 1 ELSE 0 END) AS ChapterCount,
                             SUM(CASE WHEN ""ChapterFileId"" > 0 THEN 1 ELSE 0 END) AS ChapterFileCount,
                             SUM(CASE WHEN ""Monitored"" = {trueIndicator} THEN 1 ELSE 0 END) AS MonitoredChapterCount,
                             MIN(CASE WHEN ""ReleaseDate"" < @currentDate OR ""Monitored"" = {falseIndicator} THEN NULL ELSE ""ReleaseDate"" END) AS NextChapterDate,
                             MAX(CASE WHEN ""ReleaseDate"" >= @currentDate OR ""Monitored"" = {falseIndicator} THEN NULL ELSE ""ReleaseDate"" END) AS PreviousChapterDate,
                             MAX(""ReleaseDate"") AS LastChapterDate",
                    parameters)
                .GroupBy<Chapter>(x => x.MangaId);
        }

        private SqlBuilder ChapterFilesBuilder()
        {
            if (_database.DatabaseType == DatabaseType.SQLite)
            {
                return new SqlBuilder(_database.DatabaseType)
                    .Select(@"""MangaId"",
                            SUM(COALESCE(""Size"", 0)) AS SizeOnDisk,
                            GROUP_CONCAT(""ScanlationGroup"", '|') AS ScanlationGroupsString,
                            GROUP_CONCAT(""TranslatedLanguage"", '|') AS TranslatedLanguagesString")
                    .GroupBy<ChapterFile>(x => x.MangaId);
            }

            return new SqlBuilder(_database.DatabaseType)
                .Select(@"""MangaId"",
                            SUM(COALESCE(""Size"", 0)) AS SizeOnDisk,
                            string_agg(DISTINCT ""ScanlationGroup"", '|') AS ScanlationGroupsString,
                            string_agg(DISTINCT ""TranslatedLanguage"", '|') AS TranslatedLanguagesString")
                .GroupBy<ChapterFile>(x => x.MangaId);
        }

        // Internal Dapper-mapped POCO for the ChapterFiles aggregation pass. Lives
        // alongside the repository (single-file constraint per Phase 8 backfill rules)
        // because it has no callers outside this class.
        private class ChapterFileRollup
        {
            public int MangaId { get; set; }
            public long SizeOnDisk { get; set; }
            public string ScanlationGroupsString { get; set; }
            public string TranslatedLanguagesString { get; set; }
        }
    }
}
