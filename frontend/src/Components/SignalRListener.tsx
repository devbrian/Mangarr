// Phase 15 Plan 15-07 (Wave 3) cutover — TV resource handlers stripped per Plan 07-02
// cleanup + Phase 14 Wave 3 commit b1ce62a7f reference. Manga handlers added by Phase 7
// Plan 07-02 + Phase 13 Plans 13-07/13-08/13-09 + Phase 12 Plan 12-08 are now the
// canonical handlers. handleReconnected invalidate flipped from ['/series'] to ['/manga'].
// Removed cases: 'series', 'episode', 'episodefile', 'queue', 'queue/details',
// 'queue/status', 'wanted/cutoff', 'wanted/missing', 'qualitydefinition'.
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
import { PagedQueryResponse } from 'Helpers/Hooks/usePagedApiQuery';
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
    // Phase 15 Plan 15-07 Wave 3: invalidate flipped from ['/series'] -> ['/manga']
    // (manga is the only library root post-cutover).
    queryClient.invalidateQueries({ queryKey: ['/manga'] });

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

    if (name === 'rootfolder') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });

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

    // Phase 7 D-07 / F-01 close-out — manga resource handlers (canonical post Phase 15
    // Plan 15-07 cutover; TV 'series'/'episode'/'episodefile' handlers DELETED).
    // Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
    //
    // Backend resource names verified against [V5ApiController(...)] attributes
    // (Phase 7 Plan 07-02 Task 1 grep — see SUMMARY.md).
    //
    // WR-10: All manga handlers below gate on `if (version < 5) { return; }` BEFORE
    // acting on the action. The version floor is correct — the backing resources
    // never existed in v1-v4, so there is nothing to invalidate from a `version<5`
    // push at the data-shape level.

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
    // sibling of the deleted episodefile handler. Resource name 'chapterfile' (lowercase)
    // auto-derives from ChapterFileResource.ResourceName per RestResource.cs:11 +
    // RestControllerWithSignalR.cs:23-33. React Query key '/chapterFile' (camelCase)
    // matches the future useChapterFiles.ts path arg per the Plan 07-02 URL-shaped
    // React Query key contract.
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
          true // Add the chapter file to the list if it doesn't exist (mirrors the deleted episodefile handler's add-on-update behavior).
        );

        // Repopulate the page to handle recently imported file (mirrors the deleted episodefile precedent).
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

    // Phase 13 Plan 13-08 / 13-09 added MangaQueueDetailsController +
    // MangaQueueStatusController as RestControllerWithSignalR<,> peers of the (now
    // deleted) TV QueueDetailsController + QueueStatusController. Without these two
    // handlers every page load fires console.error('signalR: Unable to find handler …')
    // (F-06 from quick-260507-tff).
    if (name === 'manga/queue/details') {
      if (version < 5) {
        return;
      }

      queryClient.invalidateQueries({ queryKey: ['/manga/queue/details'] });
      return;
    }

    if (name === 'manga/queue/status') {
      if (version < 5) {
        return;
      }

      const statusDetails = queryClient.getQueriesData({
        queryKey: ['/manga/queue/status'],
      });

      statusDetails.forEach(([queryKey]) => {
        queryClient.setQueryData(queryKey, () => body.resource);
      });

      return;
    }

    // Phase-12 follow-up (F-MISSING-SIGNALR closure, 2026-05-06): per-row updatePagedItem
    // shape after MangaMissingController extends RestControllerWithSignalR<MissingChapterResource, Chapter>
    // + subscribes to ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent.
    // BroadcastResourceChange(ModelAction.Updated, chapterId) emits a per-row resource body.
    //
    // Phase 15 Plan 15-07 Wave 3: the structural cast here was previously `body.resource as Episode`
    // (when the TV Episode type still existed); flipped to `body.resource as ModelBase` since
    // updatePagedItem only matches by `id` at runtime — the Episode reference was a build-time
    // shim that's no longer needed (ChapterResource.Id inherits from RestResource).
    if (name === 'manga/wanted/missing') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<ModelBase>(
        queryClient,
        ['/manga/wanted/missing'],
        body.resource as ModelBase
      );

      return;
    }

    // Phase-12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06): MangaCutoffController
    // extends RestControllerWithSignalR<ChapterResource, Chapter> + subscribes to
    // ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent.
    // BroadcastResourceChange(ModelAction.Updated, chapterId) emits a per-row update body.
    //
    // Phase 15 Plan 15-07 Wave 3: structural cast flipped from `Episode` to `ModelBase`
    // (same rationale as the manga/wanted/missing handler above).
    if (name === 'manga/wanted/cutoff') {
      if (version < 5 || body.action !== 'updated') {
        return;
      }

      updatePagedItem<ModelBase>(
        queryClient,
        ['/manga/wanted/cutoff'],
        body.resource as ModelBase
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
