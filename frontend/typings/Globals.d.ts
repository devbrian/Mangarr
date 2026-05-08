declare module '*.module.css';

interface Window {
  Mangarr: {
    apiKey: string;
    apiRoot: string;
    instanceName: string;
    theme: string;
    urlBase: string;
    version: string;
    isProduction: boolean;
  };
}
