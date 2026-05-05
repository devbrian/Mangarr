using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedChapterFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedChapterFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ChapterFiles""
                                     WHERE ""Id"" IN (
                                     SELECT ""ChapterFiles"".""Id"" FROM ""ChapterFiles""
                                     LEFT OUTER JOIN ""Chapters""
                                     ON ""ChapterFiles"".""Id"" = ""Chapters"".""ChapterFileId""
                                     WHERE ""Chapters"".""Id"" IS NULL)");
        }
    }
}
