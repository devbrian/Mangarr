using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedChapters : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedChapters(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""Chapters""
                                     WHERE ""Id"" IN (
                                     SELECT ""Chapters"".""Id"" FROM ""Chapters""
                                     LEFT OUTER JOIN ""Manga""
                                     ON ""Chapters"".""MangaId"" = ""Manga"".""Id""
                                     WHERE ""Manga"".""Id"" IS NULL)");
        }
    }
}
