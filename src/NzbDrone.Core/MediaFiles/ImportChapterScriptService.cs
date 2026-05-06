using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 8 Plan 99-09 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/ScriptImportDecider.cs (TV).
    //
    // Reuses the media-agnostic ScriptImportDecision enum + ScriptImportInfo struct +
    // ScriptImportException + the OutputRegex + ProcessOutput method shape verbatim.
    // Reuses IConfigService.UseScriptImport + IConfigService.ScriptImportPath — a single
    // user script can branch on Mangarr_* vs Sonarr_* env-var prefix.
    //
    // OMITS the MediaInfo env-var block (manga has no video codec / audio channels;
    // Phase 9 D-09-04 deferred ChapterFile.MediaInfo to v1.1).
    //
    // OMITS the OldFiles env-var block (manga upgrade path uses UpgradeChapterFileService
    // separately per Plan 09-07 — LocalChapter has no OldFiles list).
    //
    // OMITS PossibleExtraFiles / ShouldImportExtras output handling — manga sidecars
    // ship as part of v1.1 Extras subtree (per .planning/v1.1-roadmap.md § v1.1-01).
    // Script can still emit [MediaFile] / [MoveStatus] / [PreventExtraImport] directives;
    // the [PreventExtraImport] / [ExtraFile] directives are accepted for parser parity but
    // their effect is no-op until the v1.1 Extras subtree lands.
    //
    // Phase 14 cleanup: collapse with ScriptImportDecider.cs when Tv/ deletes.
    public interface IImportChapterScript
    {
        ScriptImportDecision TryImport(string sourcePath, string destinationFilePath, LocalChapter localChapter, ChapterFile chapterFile, TransferMode mode);
    }

    public class ImportChapterScriptService : IImportChapterScript
    {
        private readonly IProcessProvider _processProvider;
        private readonly IConfigService _configService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly ITagRepository _tagRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ImportChapterScriptService(IProcessProvider processProvider,
                                          IConfigService configService,
                                          IConfigFileProvider configFileProvider,
                                          ITagRepository tagRepository,
                                          IDiskProvider diskProvider,
                                          Logger logger)
        {
            _processProvider = processProvider;
            _configService = configService;
            _configFileProvider = configFileProvider;
            _tagRepository = tagRepository;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        // Mirrors ScriptImportDecider.OutputRegex verbatim — directives are media-agnostic.
        private static readonly Regex OutputRegex = new Regex(@"^(?:\[(?:(?<mediaFile>MediaFile)|(?<extraFile>ExtraFile))\]\s?(?<fileName>.+)|(?<preventExtraImport>\[PreventExtraImport\])|\[MoveStatus\]\s?(?:(?<deferMove>DeferMove)|(?<moveComplete>MoveComplete)|(?<renameRequested>RenameRequested)))$", RegexOptions.Compiled);

        private ScriptImportInfo ProcessOutput(List<ProcessOutputLine> processOutputLines)
        {
            var possibleExtraFiles = new List<string>();
            string mediaFile = null;
            var decision = ScriptImportDecision.MoveComplete;
            var importExtraFiles = true;

            foreach (var line in processOutputLines)
            {
                var match = OutputRegex.Match(line.Content);

                if (match.Groups["mediaFile"].Success)
                {
                    if (mediaFile is not null)
                    {
                        throw new ScriptImportException("Script output contains multiple media files. Only one media file can be returned.");
                    }

                    mediaFile = match.Groups["fileName"].Value;

                    if (!MangaFileExtensions.Extensions.Contains(Path.GetExtension(mediaFile)))
                    {
                        throw new ScriptImportException("Script output contains invalid media file: {0}", mediaFile);
                    }
                    else if (!_diskProvider.FileExists(mediaFile))
                    {
                        throw new ScriptImportException("Script output contains non-existent media file: {0}", mediaFile);
                    }
                }
                else if (match.Groups["extraFile"].Success)
                {
                    var fileName = match.Groups["fileName"].Value;

                    if (!_diskProvider.FileExists(fileName))
                    {
                        _logger.Warn("Script output contains non-existent possible extra file: {0}", fileName);
                    }

                    possibleExtraFiles.Add(fileName);
                }
                else if (match.Groups["moveComplete"].Success)
                {
                    decision = ScriptImportDecision.MoveComplete;
                }
                else if (match.Groups["renameRequested"].Success)
                {
                    decision = ScriptImportDecision.RenameRequested;
                }
                else if (match.Groups["deferMove"].Success)
                {
                    decision = ScriptImportDecision.DeferMove;
                }
                else if (match.Groups["preventExtraImport"].Success)
                {
                    importExtraFiles = false;
                }
            }

            return new ScriptImportInfo(possibleExtraFiles, mediaFile, decision, importExtraFiles);
        }

        public ScriptImportDecision TryImport(string sourcePath, string destinationFilePath, LocalChapter localChapter, ChapterFile chapterFile, TransferMode mode)
        {
            var manga = localChapter.Manga;
            var downloadId = localChapter.DownloadItem?.DownloadId;

            if (!_configService.UseScriptImport)
            {
                return ScriptImportDecision.DeferMove;
            }

            var environmentVariables = new StringDictionary();

            environmentVariables.Add("Mangarr_SourcePath", sourcePath);
            environmentVariables.Add("Mangarr_DestinationPath", destinationFilePath);

            environmentVariables.Add("Mangarr_InstanceName", _configFileProvider.InstanceName);
            environmentVariables.Add("Mangarr_ApplicationUrl", _configService.ApplicationUrl);
            environmentVariables.Add("Mangarr_TransferMode", mode.ToString());

            environmentVariables.Add("Mangarr_Manga_Id", manga.Id.ToString());
            environmentVariables.Add("Mangarr_Manga_Title", manga.Title ?? string.Empty);
            environmentVariables.Add("Mangarr_Manga_Path", manga.Path ?? string.Empty);
            environmentVariables.Add("Mangarr_Manga_MangaDexId", manga.MangaDexId?.ToString() ?? string.Empty);
            environmentVariables.Add("Mangarr_Manga_MalId", manga.MalId?.ToString() ?? string.Empty);
            environmentVariables.Add("Mangarr_Manga_AniListId", manga.AniListId?.ToString() ?? string.Empty);
            environmentVariables.Add("Mangarr_Manga_Genres", manga.Genres == null ? string.Empty : string.Join("|", manga.Genres));
            environmentVariables.Add("Mangarr_Manga_Tags", manga.Tags == null ? string.Empty : string.Join("|", manga.Tags.Select(t => _tagRepository.Get(t).Label)));

            environmentVariables.Add("Mangarr_ChapterFile_ChapterCount", localChapter.Chapters.Count.ToString());
            environmentVariables.Add("Mangarr_ChapterFile_ChapterIds", string.Join(",", localChapter.Chapters.Select(c => c.Id)));
            environmentVariables.Add("Mangarr_ChapterFile_ChapterNumbers", string.Join(",", localChapter.Chapters.Select(c => c.ChapterNumber)));
            environmentVariables.Add("Mangarr_ChapterFile_VolumeNumbers", string.Join(",", localChapter.Chapters.Select(c => c.VolumeNumber?.ToString() ?? string.Empty)));
            environmentVariables.Add("Mangarr_ChapterFile_TranslatedLanguage", localChapter.TranslatedLanguage ?? string.Empty);
            environmentVariables.Add("Mangarr_ChapterFile_ScanlationGroup", localChapter.ScanlationGroup ?? string.Empty);
            environmentVariables.Add("Mangarr_ChapterFile_SceneName", localChapter.Release?.Title ?? string.Empty);
            environmentVariables.Add("Mangarr_ChapterFile_CustomFormat", localChapter.CustomFormats == null ? string.Empty : string.Join("|", localChapter.CustomFormats));
            environmentVariables.Add("Mangarr_ChapterFile_CustomFormatScore", localChapter.CustomFormatScore.ToString());

            environmentVariables.Add("Mangarr_Download_Client", localChapter.DownloadItem?.Title ?? string.Empty);
            environmentVariables.Add("Mangarr_Download_Id", downloadId ?? string.Empty);

            _logger.Debug("Executing external script for manga import: {0}", _configService.ScriptImportPath);

            var processOutput = _processProvider.StartAndCapture(_configService.ScriptImportPath, $"\"{sourcePath}\" \"{destinationFilePath}\"", environmentVariables);

            _logger.Debug("Script Output: \r\n{0}", string.Join("\r\n", processOutput.Lines));

            if (processOutput.ExitCode != 0)
            {
                throw new ScriptImportException("Script exited with non-zero exit code: {0}", processOutput.ExitCode);
            }

            var scriptImportInfo = ProcessOutput(processOutput.Lines);

            var mediaFile = scriptImportInfo.MediaFile ?? destinationFilePath;

            chapterFile.RelativePath = manga.Path.GetRelativePath(mediaFile);
            chapterFile.Path = mediaFile;

            if (scriptImportInfo.Decision != ScriptImportDecision.DeferMove)
            {
                localChapter.ScriptImported = true;
            }

            if (scriptImportInfo.Decision == ScriptImportDecision.RenameRequested)
            {
                chapterFile.Path = null;
            }

            return scriptImportInfo.Decision;
        }
    }
}
