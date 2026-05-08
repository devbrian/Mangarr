using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 ARCHIVE-03 — strategy factory selecting <see cref="IChapterArchiver"/>
    /// by <c>Config.OutputFormat</c> (D-13). DryIoc auto-discovers all
    /// <see cref="IChapterArchiver"/> impls and injects them as <see cref="IEnumerable{T}"/>
    /// — same convention Sonarr uses for
    /// <c>MediaFiles.EpisodeImport.IImportDecisionEngineSpecification</c> (DELETED Phase 15)
    /// in <c>MediaFiles.EpisodeImport.ImportDecisionMaker</c> (DELETED Phase 15).
    ///
    /// v1 ships <c>CbzChapterArchiver</c> (FormatKey="cbz", default — ARCHIVE-01) +
    /// <c>FolderImagesChapterArchiver</c> (FormatKey="folder" — ARCHIVE-02). v2 adds CBR /
    /// EPUB / raw-image archivers by adding new <see cref="IChapterArchiver"/> impls — ZERO
    /// edits to this factory or to <c>ChapterDownloadService</c> (plan 04-03). ARCHIVE-05
    /// plugin contract honored literally.
    ///
    /// T-04-18 mitigation: a misconfigured <c>Config.OutputFormat</c> (unknown / null /
    /// empty) does NOT break downloads — the factory falls back to the "cbz" archiver
    /// (ARCHIVE-01 default) and emits a Warn-level log. Phase 7's React Settings form
    /// will validate the value against known FormatKeys before save, but the runtime
    /// fallback is the defense-in-depth backstop.
    /// </summary>
    public class ChapterArchiverFactory : IChapterArchiverFactory
    {
        private readonly IEnumerable<IChapterArchiver> _archivers;
        private readonly Logger _logger;

        public ChapterArchiverFactory(IEnumerable<IChapterArchiver> archivers, Logger logger)
        {
            _archivers = archivers;
            _logger = logger;
        }

        public IChapterArchiver Resolve(string formatKey)
        {
            var archiver = _archivers.FirstOrDefault(a =>
                string.Equals(a.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase));

            if (archiver == null)
            {
                _logger.Warn("No IChapterArchiver registered for FormatKey '{0}'; falling back to 'cbz'", formatKey);
                return _archivers.First(a => string.Equals(a.FormatKey, "cbz", StringComparison.OrdinalIgnoreCase));
            }

            return archiver;
        }
    }
}
