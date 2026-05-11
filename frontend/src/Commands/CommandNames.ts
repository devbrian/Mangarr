// Sonarr divergence: GH issue #63 — three manga sibling command names added per the
// Phase 6 manga-side IndexerSearch family. Mangarr-side string values mirror the C#
// `Command.Name = GetType().Name.Replace("Command", "")` derivation (Command.cs:41).
// Role-match analogs:
//   * ChapterSearch              ← EpisodeSearch
//   * MissingChapterSearch       ← MissingEpisodeSearch
//   * CutoffUnmetChapterSearch   ← CutoffUnmetEpisodeSearch
// Phase 8 cleanup: collapse with the Episode-side trio when Tv/ deletes.
enum CommandNames {
  ApplicationUpdate = 'ApplicationUpdate',
  Backup = 'Backup',
  ChapterSearch = 'ChapterSearch',
  ClearBlocklist = 'ClearBlocklist',
  ClearLog = 'ClearLog',
  CutoffUnmetChapterSearch = 'CutoffUnmetChapterSearch',
  CutoffUnmetEpisodeSearch = 'CutoffUnmetEpisodeSearch',
  DeleteLogFiles = 'DeleteLogFiles',
  DeleteSeriesFiles = 'DeleteSeriesFiles',
  DeleteUpdateLogFiles = 'DeleteUpdateLogFiles',
  DownloadedEpisodesScan = 'DownloadedEpisodesScan',
  EpisodeSearch = 'EpisodeSearch',
  ManualImport = 'ManualImport',
  MangaSearch = 'MangaSearch',
  MissingChapterSearch = 'MissingChapterSearch',
  MissingEpisodeSearch = 'MissingEpisodeSearch',
  MoveSeries = 'MoveSeries',
  RefreshManga = 'RefreshManga',
  RefreshMonitoredDownloads = 'RefreshMonitoredDownloads',
  RefreshSeries = 'RefreshSeries',
  RenameFiles = 'RenameFiles',
  RenameSeries = 'RenameSeries',
  ResetApiKey = 'ResetApiKey',
  ResetQualityDefinitions = 'ResetQualityDefinitions',
  RssSync = 'RssSync',
  SeasonSearch = 'SeasonSearch',
  SeriesSearch = 'SeriesSearch',
}

export default CommandNames;
