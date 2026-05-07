import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
import { QueryKey, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { useDispatch } from 'react-redux';
import { setAppValue, setVersion } from 'App/appStore';
import ModelBase from 'App/ModelBase';
import Command from 'Commands/Command';
import { useUpdateCommand } from 'Commands/useCommands';
import Episode from 'Episode/Episode';
import { EpisodeFile } from 'EpisodeFile/EpisodeFile';
import { PagedQueryResponse } from 'Helpers/Hooks/usePagedApiQuery';
import Series from 'Series/Series';
import { IndexerModel } from 'Settings/Indexers/useIndexers';
import { NotificationModel } from 'Settings/Notifications/useConnections';
import { removeItem, updateItem } from 'Store/Actions/baseActions';
import { repopulatePage } from 'Utilities/pagePopulator';
import SignalRLogger from 'Utilities/SignalRLogger';

type SignalRAction = 'sync' | 'created' | 'updated' | 'deleted';

interface SignalRMessage {
  name: string;
  body: {
    action: SignalRAction;
    resource: ModelBase;
    version: string;
  };
  version: number | undefined;
}

function SignalRListener() {
  const queryClient = useQueryClient();
  const updateCommand = useUpdateCommand();
  const dispatch = useDispatch();

  const connection = useRef<HubConnection | null>(null);

  const handleStartFail = useRef((error: unknown) => {
    console.error('[signalR] failed to connect');
    console.error(error);

    setAppValue({
      isConnected: false,
      isReconnecting: false,
      isDisconnected: false,
      isRestarting: false,
    });
  });

  const handleStart = useRef(() => {
    console.debug('[signalR] connected');

    setAppValue({
      isConnected: true,
      isReconnecting: false,
      isDisconnected: false,
      isRestarting: false,
    });
  });

  const handleReconnecting = useRef(() => {
    setAppValue({ isReconnecting: true });
  });

  const handleReconnected = useRef(() => {
    setAppValue({
      isConnected: true,
      isReconnecting: false,
      isDisconnected: false,
      isRestarting: false,
    });

    // Repopulate the page (if a repopulator is set) to ensure things
    // are in sync after reconnecting.
    queryClient.invalidateQueries({ queryKey: ['/series'] });

    queryClient.invalidateQueries({ queryKey: ['/command'] });

    repopulatePage();
  });

  const handleClose = useRef(() => {
    console.debug('[signalR] connection closed');
  });

  const handleReceiveMessage = useRef((message: SignalRMessage) => {
    console.debug(
      `[signalR] received ${message.name}${
        message.version ? ` v${message.version}` : ''
      }`,
      message.body
    );

    const { name, body, version = 0 } = message;

    if (name === 'calendar') {
      if (body.action === 'updated') {
        dispatch(
          updateItem({
            section: 'calendar',
            updateOnly: true,
            ...body.resource,
          })
        );
        return;
      }
    }

    if (name === 'command') {
      if (body.action === 'sync') {
        queryClient.invalidateQueries({ queryKey: ['/command'] });
        return;
      }

      const resource = body.resource as Command;

      updateCommand(resource);

      return;
    }

    if (name === 'downloadclient') {
      const section = 'settings.downloadClients';

      if (body.action === 'created' || body.action === 'updated') {
        dispatch(updateItem({ section, ...body.resource }));
      } else if (body.action === 'deleted') {
        dispatch(removeItem({ section, id: body.resource.id }));
      }

      return;
    }

    if (name === 'episode') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as Episode;

        updateQueryClientItem(
          queryClient,
          ['/episode'],
          updatedItem,
          false // Don't add the episode to the list if it doesn't exist. Episodes should already be in the list since they are included in the series details.
        );
      }

      return;
    }

    if (name === 'episodefile') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as EpisodeFile;

        updateQueryClientItem(
          queryClient,
          ['/episodeFile'],
          updatedItem,
          true // Add the episode file to the list if it doesn't exist. This can happen when an episode file is imported and wasn't previously in the list of episode files.
        );

        // Repopulate the page to handle recently imported file
        repopulatePage('episodeFileUpdated');
      } else if (body.action === 'deleted') {
        const id = body.resource.id;

        removeQueryClientItem(queryClient, ['/episodeFile'], id);
        repopulatePage('episodeFileDeleted');
      }

      return;
    }

    if (name === 'health') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/health'] });
      return;
    }

    if (name === 'importlist') {
      const section = 'settings.importLists';

      if (body.action === 'created' || body.action === 'updated') {
        dispatch(updateItem({ section, ...body.resource }));
      } else if (body.action === 'deleted') {
        dispatch(removeItem({ section, id: body.resource.id }));
      }

      return;
    }

    if (name === 'indexer') {
      const updatedItem = body.resource as IndexerModel;

      if (body.action === 'created' || body.action === 'updated') {
        updateQueryClientItem(queryClient, ['/indexer'], updatedItem, true);
      } else if (body.action === 'deleted') {
        removeQueryClientItem(queryClient, ['/indexer'], body.resource.id);
      }

      return;
    }

    if (name === 'metadata') {
      const updatedItem = body.resource as ModelBase;

      if (body.action === 'updated') {
        updateQueryClientItem(queryClient, ['/metadata'], updatedItem, false);
      }

      return;
    }

    if (name === 'connection') {
      const updatedItem = body.resource as NotificationModel;

      if (body.action === 'created' || body.action === 'updated') {
        updateQueryClientItem(
          queryClient,
          ['/connection'],
          updatedItem,
          body.action === 'created' // Only add the connection to the list if it was created. If it was updated and it doesn't exist in the list, it likely means the connection is disabled and shouldn't be shown in the list.
        );
      } else if (body.action === 'deleted') {
        removeQueryClientItem(queryClient, ['/connection'], body.resource.id);
      }

      return;
    }

    if (name === 'qualitydefinition') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/qualitydefinition'] });
      return;
    }

    if (name === 'queue') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/queue'] });
      return;
    }

    if (name === 'queue/details') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/queue/details'] });
      return;
    }

    if (name === 'queue/status') {
      if (version < 5) {
        return;
      }

      const statusDetails = queryClient.getQueriesData({
        queryKey: ['/queue/status'],
      });

      statusDetails.forEach(([queryKey]) => {
        queryClient.setQueryData(queryKey, () => body.resource);
      });

      return;
    }

    if (name === 'rootfolder') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });

      return;
    }

    if (name === 'series') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as Series;

        updateQueryClientItem(
          queryClient,
          ['/series'],
          updatedItem,
          false // Don't add the series to the list if it doesn't exist. Series should already be in the list since they are included in the calendar and series details.
        );

        repopulatePage('seriesUpdated');
      } else if (body.action === 'deleted') {
        removeQueryClientItem(queryClient, ['/series'], body.resource.id);
      }

      return;
    }

    if (name === 'system/task') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/system/task'] });
      return;
    }

    if (name === 'tag') {
      if (version < 5 || body.action !== 'sync') {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/tag'] });
      queryClient.invalidateQueries({ queryKey: ['/tag/detail'] });

      return;
    }

    if (name === 'version') {
      setVersion({ version: body.version });
      return;
    }

    if (name === 'wanted/cutoff') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<Episode>(
        queryClient,
        ['/wanted/cutoff'],
        body.resource as Episode
      );

      return;
    }

    if (name === 'wanted/missing') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<Episode>(
        queryClient,
        ['/wanted/missing'],
        body.resource as Episode
      );

      return;
    }

    // Phase 7 D-07 / F-01 close-out — 6 manga resource handlers.
    // Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
    // Role-match analog: existing 'series' / 'episode' / 'queue/status' handlers above.
    //
    // Backend resource names verified against [V5ApiController(...)] attributes
    // (Phase 7 Plan 07-02 Task 1 grep — see SUMMARY.md).
    //
    // Type cast Option B: `{ id: number }` — Plan 03 has not shipped Manga/Chapter
    // typings yet, so we use a minimal-shape cast here. Refine to typed casts
    // (`Manga` / `Chapter`) when Plan 03 lands; the dispatch logic only reads
    // `id` in any case.
    //
    // Phase 8 cleanup: when Tv/ deletes, these become the canonical handlers.
    //
    // WR-10: All 6 manga handlers below gate on `if (version < 5) { return; }`
    // BEFORE acting on the action. This is structurally inconsistent with the
    // older 'command' handler above (which does not version-gate because the
    // command resource has been v5-shaped since v1), and creates a quiet
    // desync window on `version<5` pushes during a rolling deploy: the
    // matching React Query cache is NOT invalidated and stale data persists
    // until the next manual refresh. Acceptable for v1 (single-version
    // deploy) but worth re-evaluating in Phase 8 cutover when `version`
    // becomes a manga-specific dimension. The version floor is correct — the
    // backing resources never existed in v1-v4, so there is nothing to
    // invalidate from a `version<5` push at the data-shape level.

    if (name === 'manga') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as ModelBase;

        updateQueryClientItem(
          queryClient,
          ['/manga'],
          updatedItem,
          false // Don't add the manga to the list if it doesn't exist. Manga should already be in the list since they are included in the manga details.
        );

        repopulatePage('mangaUpdated');
      } else if (body.action === 'deleted') {
        removeQueryClientItem(queryClient, ['/manga'], body.resource.id);
      }

      return;
    }

    if (name === 'chapter') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as ModelBase;

        updateQueryClientItem(
          queryClient,
          ['/chapter'],
          updatedItem,
          false // Don't add the chapter to the list if it doesn't exist. Chapters should already be in the list since they are included in the manga details.
        );
      }

      return;
    }

    // Phase 13 Plan 13-07 (Plan 13-00 Pattern κ closure): chapterfile handler — manga
    // sibling of the episodefile handler at lines 157-181 above. Resource name
    // 'chapterfile' (lowercase) auto-derives from ChapterFileResource.ResourceName per
    // RestResource.cs:11 + RestControllerWithSignalR.cs:23-33 — bare [V5ApiController]
    // on ChapterFileController falls back to `new ChapterFileResource().ResourceName.Trim('/')`.
    // React Query key '/chapterFile' (camelCase) matches the future useChapterFiles.ts
    // path arg per the Plan 07-02 URL-shaped React Query key contract (see lines 386-397
    // above for the version<5 gating rationale).
    if (name === 'chapterfile') {
      if (version < 5) {
        return;
      }

      if (body.action === 'updated') {
        const updatedItem = body.resource as ModelBase;

        updateQueryClientItem(
          queryClient,
          ['/chapterFile'],
          updatedItem,
          true // Add the chapter file to the list if it doesn't exist. This can happen when a chapter file is imported and wasn't previously in the list of chapter files (mirrors episodefile handler at line 169).
        );

        // Repopulate the page to handle recently imported file (mirrors episodefile precedent at line 173).
        repopulatePage('chapterFileUpdated');
      } else if (body.action === 'deleted') {
        const id = body.resource.id;

        removeQueryClientItem(queryClient, ['/chapterFile'], id);
        repopulatePage('chapterFileDeleted');
      }

      return;
    }

    if (name === 'manga/queue') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/manga/queue'] });
      return;
    }

    if (name === 'manga/blocklist') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/manga/blocklist'] });
      return;
    }

    if (name === 'manga/history') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/manga/history'] });
      return;
    }

    // Phase-12 follow-up (F-MISSING-SIGNALR closure, 2026-05-06): upgraded from the
    // Plan 07-02 invalidateQueries shape to the per-row updatePagedItem shape after
    // MangaMissingController (renamed from MissingChaptersController for naming
    // consistency with the rest of the manga V5 namespace) was refactored to extend
    // RestControllerWithSignalR<MissingChapterResource, Chapter> + subscribe to
    // ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent.
    // BroadcastResourceChange(ModelAction.Updated, chapterId) now emits a per-row
    // resource body (matches TV's wanted/missing handler at lines 359-371 which
    // consumes EpisodeResource via updatePagedItem<Episode>).
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06): MangaMissingController
    // now extends RestControllerWithSignalR<ChapterResource, Chapter> (the canonical
    // chapter DTO — replaces the deleted MissingChapterResource POCO; mirrors TV's
    // EpisodeResource reuse pattern). The structural cast ``body.resource as Episode``
    // remains correct because updatePagedItem only matches by ``id`` at runtime, and
    // ChapterResource.Id (inherited from RestResource) carries the chapter id from
    // the broadcast.
    //
    // Resource name 'manga/wanted/missing' is the route-attribute literal per Plan
    // 07-02 URL-shaped React Query key contract (matches the path arg useMissing
    // passes to usePagedApiQuery when mediaType === 'manga'). Mirrors the
    // F-CUTOFF-SIGNALR closure for cross-controller consistency.
    if (name === 'manga/wanted/missing') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<Episode>(
        queryClient,
        ['/manga/wanted/missing'],
        body.resource as Episode
      );

      return;
    }

    // Phase-12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06): mirrors the TV
    // 'wanted/cutoff' handler shape at lines 345-357. Backend MangaCutoffController
    // extends RestControllerWithSignalR<,> + subscribes to ChapterGrabbedEvent /
    // ChapterImportedEvent / ChapterFileDeletedEvent; BroadcastResourceChange
    // (ModelAction.Updated, chapterId) emits a per-row update body that matches TV's
    // Episode-keyed updatePagedItem call.
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06): MangaCutoffController
    // now extends RestControllerWithSignalR<ChapterResource, Chapter> (the canonical
    // chapter DTO — replaces the deleted MangaCutoffResource POCO; mirrors TV's
    // EpisodeResource reuse pattern). The structural cast ``body.resource as Episode``
    // remains correct because updatePagedItem only matches by ``id`` at runtime, and
    // ChapterResource.Id (inherited from RestResource) carries the chapter id from
    // the broadcast.
    //
    // Resource name 'manga/wanted/cutoff' is the route-attribute literal per Plan
    // 07-02 URL-shaped React Query key contract (matches the path arg the
    // useCutoffUnmet hook passes to usePagedApiQuery when mediaType === 'manga').
    if (name === 'manga/wanted/cutoff') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<Episode>(
        queryClient,
        ['/manga/wanted/cutoff'],
        body.resource as Episode
      );

      return;
    }

    console.error(`signalR: Unable to find handler for ${name}`);
  });

  useEffect(() => {
    console.log('[signalR] starting');

    const url = `${window.Sonarr.urlBase}/signalr/messages`;

    connection.current = new HubConnectionBuilder()
      .configureLogging(new SignalRLogger(LogLevel.Information))
      .withUrl(
        `${url}?access_token=${encodeURIComponent(window.Sonarr.apiKey)}`
      )
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.elapsedMilliseconds > 180000) {
            setAppValue({ isDisconnected: true });
          }
          return Math.min(retryContext.previousRetryCount, 10) * 1000;
        },
      })
      .build();

    connection.current.onreconnecting(handleReconnecting.current);
    connection.current.onreconnected(handleReconnected.current);
    connection.current.onclose(handleClose.current);

    connection.current.on('receiveMessage', handleReceiveMessage.current);

    connection.current
      .start()
      .then(handleStart.current, handleStartFail.current);

    return () => {
      connection.current?.stop();
      connection.current = null;
    };
  }, [dispatch]);

  return null;
}

export default SignalRListener;

const updatePagedItem = <T extends ModelBase>(
  queryClient: ReturnType<typeof useQueryClient>,
  queryKey: QueryKey,
  updatedItem: T
) => {
  queryClient.setQueriesData(
    { queryKey },
    (oldData: PagedQueryResponse<T> | undefined) => {
      if (!oldData) {
        return oldData;
      }

      const itemIndex = oldData.records.findIndex(
        (item) => item.id === updatedItem.id
      );

      if (itemIndex === -1) {
        return oldData;
      }

      return {
        ...oldData,
        records: oldData.records.map((item) => {
          if (item.id === updatedItem.id) {
            return updatedItem;
          }

          return item;
        }),
      };
    }
  );
};

const updateQueryClientItem = <T extends ModelBase>(
  queryClient: ReturnType<typeof useQueryClient>,
  queryKey: QueryKey,
  updatedItem: T,
  addMissing: boolean
) => {
  queryClient.setQueriesData({ queryKey }, (oldData: T[] | undefined) => {
    if (!oldData) {
      return oldData;
    }

    const itemIndex = oldData.findIndex((item) => item.id === updatedItem.id);

    if (itemIndex === -1 && addMissing) {
      return [...oldData, updatedItem];
    }

    return oldData.map((item) => {
      if (item.id === updatedItem.id) {
        return updatedItem;
      }

      return item;
    });
  });
};

const removeQueryClientItem = <T extends ModelBase>(
  queryClient: ReturnType<typeof useQueryClient>,
  queryKey: QueryKey,
  id: T['id']
) => {
  queryClient.setQueriesData({ queryKey }, (oldData: T[] | undefined) => {
    if (!oldData) {
      return oldData;
    }

    const itemIndex = oldData.findIndex((item) => item.id === id);

    if (itemIndex === -1) {
      return oldData;
    }

    return oldData.filter((item) => item.id !== id);
  });
};
