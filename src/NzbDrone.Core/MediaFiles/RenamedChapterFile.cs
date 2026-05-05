namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 8 parity-audit (gap: no-sibling/RenamedEpisodeFile).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/RenamedEpisodeFile.cs (TV).
    // Post-rename diff payload returned from RenameChapterFileService.
    // Phase 8 cleanup: collapse with RenamedEpisodeFile when Tv/ deletes.
    public class RenamedChapterFile
    {
        public ChapterFile ChapterFile { get; set; }
        public string PreviousPath { get; set; }
        public string PreviousRelativePath { get; set; }
    }
}
