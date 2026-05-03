namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportSpecDecision.cs.
    //
    // Per-spec accept/reject result. Mirrors TV ImportSpecDecision verbatim with
    // a manga-shaped ImportRejectionReason enum — no quality/sample/season concepts.
    // Phase 8 cleanup: collapse with TV ImportSpecDecision when Tv/ deletes.
    public class MangaImportSpecDecision
    {
        public bool Accepted { get; private set; }
        public ImportRejectionReason Reason { get; private set; }
        public string Message { get; private set; }

        private static readonly MangaImportSpecDecision AcceptDecision = new() { Accepted = true };

        private MangaImportSpecDecision()
        {
        }

        public static MangaImportSpecDecision Accept() => AcceptDecision;

        public static MangaImportSpecDecision Reject(ImportRejectionReason reason, string message, params object[] args)
        {
            return Reject(reason, args == null || args.Length == 0 ? message : string.Format(message, args));
        }

        public static MangaImportSpecDecision Reject(ImportRejectionReason reason, string message)
        {
            return new MangaImportSpecDecision
            {
                Accepted = false,
                Reason = reason,
                Message = message
            };
        }
    }

    // Phase 6 PIPELINE-04 — manga-shaped reject reasons. DROPS TV-only Sample / SeasonExtra /
    // PartialSeason / NotQualityUpgrade / NotRevisionUpgrade. Adds NotUpgradeAllowed (D-10)
    // and ChapterAlreadyImported / ChapterFileExists / EmptyArchive / ChapterNotFoundInRelease.
    public enum ImportRejectionReason
    {
        Unknown = 0,
        ChapterFileExists = 1,
        EmptyArchive = 2,
        ChapterNotFoundInRelease = 3,
        MinimumFreeSpace = 4,
        NotUpgrade = 5,
        NotUpgradeAllowed = 6,
        NotCustomFormatUpgrade = 7,
        ChapterAlreadyImported = 8,
        RootFolderMissing = 9,
        DecisionError = 10
    }
}
