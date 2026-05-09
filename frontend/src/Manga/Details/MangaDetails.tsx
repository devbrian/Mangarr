// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetails.tsx (hero
// header card + tabbed body — Sonarr's nests episodes under collapsible
// season cards; manga sibling renders a flat sortable Chapters tab body
// per PROJECT.md "Volumes/Seasons" Out-of-Scope).
//
// Manga sibling preserves: hero card layout (cover left, info right, status
// badges, monitor toggle, hero toolbar), PageContent + PageToolbar shell,
// metadata-source links chip, alternate-titles popover.
// Manga sibling diverges from SeriesDetails:
//   * Tab navigation (Overview / Chapters / Files / History / Search) per
//     UI-04 — Series uses no tabs, just collapsible season cards.
//     Components/Tab/ does NOT exist in the codebase, so the tab buttons are
//     stateful and rendered inline (RESEARCH Assumption A4 fallback).
//   * No EpisodeFile / Season / NextAiring / lastAired references.
//   * `qualityProfileId` → `translationProfileId` + `customFormatProfileId`
//     in the metadata strip. TranslationProfileName lookup is inlined here
//     until Plan 07 ships `Settings/Profiles/Translations/TranslationProfileName.tsx`.
//   * Hero toolbar: Refresh / Search Manga / Edit / Delete / History
//     (UI-SPEC §Manga Detail). Edit + Delete + History modals reuse the
//     existing Sonarr Series modals via seriesId={mangaId} bridge — same
//     pattern Plan 07-04 documented in MangaIndex (the modals are
//     media-type-agnostic at the JSX level until Phase 8 collapses).
//   * Files / History / Search tabs render minimal placeholder content for
//     v1; full content is deferred to Plans 07-08 (Files), 07-09 (History
//     wrapper), and the existing InteractiveSearch component (Search tab).
//
// T-07-15 (XSS) mitigation: every user-controlled string field flows through
// React JSX default escaping ({manga.title}, {manga.overview}). NO
// dangerouslySetInnerHTML in this file.
//
// Phase 8 cleanup: collapse with SeriesDetails when Tv/ deletes.
import React, { useCallback, useMemo, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import MetadataAttribution from 'Components/MetadataAttribution';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import MangaPoster from 'Manga/MangaPoster';
import {
  useSingleManga,
  useToggleMangaMonitored,
} from 'Manga/useManga';
// Sonarr divergence: Phase 15 Plan 15-12 — Series/Edit modal was inlined as a
// stub when the TV subtree was deleted in Plan 15-07. The dedicated
// single-manga Edit modal under Manga/Edit/ now ships the deferred
// "v1.1+ Edit" path (fix(manga-edit-button-no-op)). The Delete modal remains
// stubbed below; a sibling fix-forward PR will wire it.
import EditMangaModal from 'Manga/Edit/EditMangaModal';
import { useChaptersByManga } from 'Chapter/useChapter';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import MangaAlternateTitles from './MangaAlternateTitles';
import MangaDetailsChapters from './MangaDetailsChapters';
import MangaDetailsFiles from './MangaDetailsFiles';
import MangaDetailsHistory from './MangaDetailsHistory';
import MangaDetailsLinks from './MangaDetailsLinks';
import MangaDetailsProvider from './MangaDetailsProvider';
import MangaProgressLabel from './MangaProgressLabel';
import MangaTags from './MangaTags';
import styles from './MangaDetails.css';

type TabKey = 'overview' | 'chapters' | 'files' | 'history' | 'search';

const TABS: { key: TabKey; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'chapters', label: 'Chapters' },
  { key: 'files', label: 'Files' },
  { key: 'history', label: 'History' },
  { key: 'search', label: 'Search' },
];

interface TranslationProfileResource {
  id: number;
  name?: string;
  languages: string[];
}

function useTranslationProfileName(profileId?: number): string | undefined {
  // Inline lookup against /api/v5/translationprofile (Phase 5 D-01). Plan 07
  // (Settings rework) will ship a dedicated <TranslationProfileName> component
  // once the Profiles/Translations/ subtree is in place; until then this hook
  // does the resolution where it's needed (the metadata strip on the hero).
  const { data: profiles } = useApiQuery<TranslationProfileResource[]>({
    path: '/translationprofile',
    queryOptions: { staleTime: Infinity },
  });

  if (profileId == null || !profiles) {
    return undefined;
  }
  return profiles.find((p) => p.id === profileId)?.name;
}

interface MangaDetailsProps {
  mangaId: number;
}

function MangaDetails({ mangaId }: MangaDetailsProps) {
  const manga = useSingleManga(mangaId);
  const { toggleMangaMonitored, isTogglingMangaMonitored } =
    useToggleMangaMonitored(mangaId);

  const { data: chapters, isFetching: isChaptersFetching } =
    useChaptersByManga(mangaId);

  const translationProfileName = useTranslationProfileName(
    manga?.translationProfileId
  );

  const [activeTab, setActiveTab] = useState<TabKey>('overview');
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);

  // Toolbar command dispatch — Refresh + SearchManga buttons.
  // History toolbar button activates the in-page History tab (Plan 07-09 sibling
  // already shipped in PR #21 — reuses MangaDetailsHistory.tsx). Per-row analogs
  // live in MangaIndexRow.onRefreshPress / onSearchPress; canonical command name
  // + payload shapes mirror that file (CommandNames.RefreshManga uses
  // `mangaIds: [id]` array; CommandNames.MangaSearch uses singular `mangaId`).
  const executeCommand = useExecuteCommand();
  const isRefreshing = useCommandExecuting(CommandNames.RefreshManga, {
    mangaIds: [mangaId],
  });
  const isSearching = useCommandExecuting(CommandNames.MangaSearch, {
    mangaIds: [mangaId],
  });

  const handleRefreshPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RefreshManga,
      mangaIds: [mangaId],
    });
  }, [executeCommand, mangaId]);

  const handleSearchPress = useCallback(() => {
    // MangaSearchCommand binds to `MangaIds: List<int>` (plural) per Phase 6 D-06
    // bulk-dispatch shape; a singular `mangaId` payload silently no-ops in
    // MangaSearchService.Execute ("MangaSearchCommand received with no MangaIds;
    // nothing to search"). The command status reports as "completed" — misleading.
    executeCommand({
      name: CommandNames.MangaSearch,
      mangaIds: [mangaId],
    });
  }, [executeCommand, mangaId]);

  const handleHistoryPress = useCallback(() => {
    setActiveTab('history');
  }, []);

  const handleEditPress = useCallback(() => setIsEditModalOpen(true), []);
  const handleEditModalClose = useCallback(
    () => setIsEditModalOpen(false),
    []
  );

  const handleDeletePress = useCallback(() => {
    setIsEditModalOpen(false);
    setIsDeleteModalOpen(true);
  }, []);
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const handleDeleteModalClose = useCallback(
    () => setIsDeleteModalOpen(false),
    []
  );

  const handleMonitorTogglePress = useCallback(
    (value: boolean) => {
      toggleMangaMonitored({ monitored: value });
    },
    [toggleMangaMonitored]
  );

  const alternateTitles = useMemo(
    () =>
      (manga?.alternateTitles ?? []).filter((t) => t.title !== manga?.title),
    [manga]
  );

  if (!manga) {
    return null;
  }

  const {
    title,
    path,
    monitored,
    overview,
    images,
    tags,
    statistics = {},
    mangaDexId,
    aniListId,
    malId,
  } = manga;

  const chapterCount = chapters.length;
  const chapterFileCount = chapters.filter((c) => c.chapterFileId != null).length;
  const sizeOnDisk = statistics.sizeOnDisk ?? 0;

  void handleDeleteModalClose;

  return (
    <MangaDetailsProvider mangaId={mangaId}>
      <PageContent title={title}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('Refresh')}
              iconName={icons.REFRESH}
              spinningName={icons.REFRESH}
              title={translate('RefreshAndScanTooltip')}
              isSpinning={isRefreshing}
              onPress={handleRefreshPress}
            />

            <PageToolbarButton
              label={translate('SearchManga')}
              iconName={icons.SEARCH}
              isDisabled={!monitored || chapterCount === 0}
              isSpinning={isSearching}
              onPress={handleSearchPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('Edit')}
              iconName={icons.EDIT}
              onPress={handleEditPress}
            />

            <PageToolbarButton
              label={translate('Delete')}
              iconName={icons.DELETE}
              onPress={handleDeletePress}
            />

            <PageToolbarButton
              label={translate('History')}
              iconName={icons.HISTORY}
              isDisabled={chapterCount === 0}
              onPress={handleHistoryPress}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody innerClassName={styles.innerContentBody}>
          <div className={styles.header}>
            <div className={styles.headerContent}>
              <MangaPoster
                className={styles.poster}
                images={images}
                size={500}
                lazy={false}
                title={title}
              />

              <div className={styles.info}>
                <div className={styles.titleRow}>
                  <div className={styles.titleContainer}>
                    <div className={styles.toggleMonitoredContainer}>
                      <MonitorToggleButton
                        className={styles.monitorToggleButton}
                        monitored={monitored}
                        isSaving={isTogglingMangaMonitored}
                        size={40}
                        onPress={handleMonitorTogglePress}
                      />
                    </div>

                    <div className={styles.title}>{title}</div>

                    {alternateTitles.length ? (
                      <div className={styles.alternateTitlesIconContainer}>
                        <Popover
                          anchor={
                            <Icon name={icons.ALTERNATE_TITLES} size={20} />
                          }
                          title={translate('AlternateTitles')}
                          body={
                            <MangaAlternateTitles
                              alternateTitles={alternateTitles}
                            />
                          }
                          position={tooltipPositions.BOTTOM}
                        />
                      </div>
                    ) : null}
                  </div>
                </div>

                <div>
                  <Label className={styles.detailsLabel} size={sizes.LARGE}>
                    <div>
                      <Icon name={icons.FOLDER} size={17} />
                      <span className={styles.path}>{path}</span>
                    </div>
                  </Label>

                  <Label className={styles.detailsLabel} size={sizes.LARGE}>
                    <div>
                      <Icon name={icons.DRIVE} size={17} />
                      <span className={styles.sizeOnDisk}>
                        {formatBytes(sizeOnDisk)}
                      </span>
                    </div>
                  </Label>

                  {translationProfileName ? (
                    <Label
                      className={styles.detailsLabel}
                      title={translate('TranslationProfile')}
                      size={sizes.LARGE}
                    >
                      <div>
                        <Icon name={icons.PROFILE} size={17} />
                        <span className={styles.translationProfileName}>
                          {translationProfileName}
                        </span>
                      </div>
                    </Label>
                  ) : null}

                  <Label className={styles.detailsLabel} size={sizes.LARGE}>
                    <div>
                      <Icon
                        name={monitored ? icons.MONITORED : icons.UNMONITORED}
                        size={17}
                      />
                      <span>
                        {monitored
                          ? translate('Monitored')
                          : translate('Unmonitored')}
                      </span>
                    </div>
                  </Label>

                  <Label
                    className={styles.detailsLabel}
                    title={manga.status}
                    size={sizes.LARGE}
                  >
                    <div>
                      <Icon name={icons.INFO} size={17} />
                      <span className={styles.statusName}>{manga.status}</span>
                    </div>
                  </Label>

                  {mangaDexId || aniListId || malId ? (
                    <Tooltip
                      anchor={
                        <Label
                          className={styles.detailsLabel}
                          size={sizes.LARGE}
                        >
                          <div>
                            <Icon name={icons.EXTERNAL_LINK} size={17} />
                            <span className={styles.links}>
                              {translate('Links')}
                            </span>
                          </div>
                        </Label>
                      }
                      tooltip={
                        <MangaDetailsLinks
                          mangaDexId={mangaDexId}
                          aniListId={aniListId}
                          malId={malId}
                        />
                      }
                      kind={kinds.INVERSE}
                      position={tooltipPositions.BOTTOM}
                    />
                  ) : null}

                  {tags.length ? (
                    <Tooltip
                      anchor={
                        <Label
                          className={styles.detailsLabel}
                          size={sizes.LARGE}
                        >
                          <Icon name={icons.TAGS} size={17} />
                          <span className={styles.tags}>
                            {translate('Tags')}
                          </span>
                        </Label>
                      }
                      tooltip={<MangaTags mangaId={mangaId} />}
                      kind={kinds.INVERSE}
                      position={tooltipPositions.BOTTOM}
                    />
                  ) : null}

                  <MangaProgressLabel
                    className={styles.mangaProgressLabel}
                    monitored={monitored}
                    chapterCount={chapterCount}
                    chapterFileCount={chapterFileCount}
                  />
                </div>

                {overview ? (
                  <div className={styles.overview}>{overview}</div>
                ) : null}

                <MetadataAttribution />
              </div>
            </div>
          </div>

          <div className={styles.contentContainer}>
            {/* Tab navigation — RESEARCH A4 fallback (no Components/Tab in
                the codebase). Phase 8 may swap to a real Tab component if
                Components/Tab/ ships in a later plan. */}
            <div className={styles.tabContainer} role="tablist">
              {TABS.map((tab) => (
                <button
                  key={tab.key}
                  type="button"
                  role="tab"
                  aria-selected={activeTab === tab.key}
                  className={
                    activeTab === tab.key
                      ? styles.tabButtonActive
                      : styles.tabButton
                  }
                  onClick={() => setActiveTab(tab.key)}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            <div className={styles.tabBody}>
              {activeTab === 'overview' ? (
                <div>
                  {overview ? <div>{overview}</div> : null}
                </div>
              ) : null}

              {activeTab === 'chapters' ? (
                <MangaDetailsChapters mangaId={mangaId} />
              ) : null}

              {activeTab === 'files' ? (
                <MangaDetailsFiles mangaId={mangaId} />
              ) : null}

              {activeTab === 'history' ? (
                <MangaDetailsHistory mangaId={mangaId} />
              ) : null}

              {activeTab === 'search' ? (
                <InteractiveSearch
                  type="manga"
                  searchPayload={{ mangaId }}
                />
              ) : null}
            </div>

            {isChaptersFetching && activeTab === 'chapters' ? (
              <LoadingIndicator />
            ) : null}
          </div>

          {/* fix(manga-edit-button-no-op): per-manga Edit modal now wired
              (was Phase 15 Plan 15-12 stub `null : null`). Delete modal
              remains stubbed until its sibling fix-forward PR. */}
          <EditMangaModal
            isOpen={isEditModalOpen}
            mangaId={mangaId}
            onModalClose={handleEditModalClose}
          />
          {isDeleteModalOpen ? null : null}
        </PageContentBody>
      </PageContent>
    </MangaDetailsProvider>
  );
}

export default MangaDetails;
