import createAjaxRequest from 'Utilities/createAjaxRequest';
import { set } from '../baseActions';

function createFetchSchemaHandler(section, url) {
  return function(getState, payload, dispatch) {
    dispatch(set({ section, isSchemaFetching: true }));

    // quick-260608-gmm: forward the dispatched payload as query data (mirrors
    // createFetchHandler.js). createThunk defaults payload to {} so the other 5
    // schema callers send an empty query (no behavior change); the CF modals pass
    // { mediaType: 'manga' } → GET /customformat/schema?mediaType=manga.
    const promise = createAjaxRequest({
      url,
      data: payload,
      traditional: true
    }).request;

    promise.done((data) => {
      dispatch(set({
        section,
        isSchemaFetching: false,
        isSchemaPopulated: true,
        schemaError: null,
        schema: data
      }));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSchemaFetching: false,
        isSchemaPopulated: true,
        schemaError: xhr
      }));
    });
  };
}

export default createFetchSchemaHandler;
