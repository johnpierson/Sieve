/* eslint-disable @typescript-eslint/no-explicit-any */
// Type definitions for WebView2Script
// Project: https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/javascript/
// Definitions by jeremyong <https://github.com/jeremyong>
// Definitions: https://github.com/DefinitelyTyped/DefinitelyTyped

interface WebViewEventListener {
  (evt: Event & { data?: any }): void;
}

type WebViewEventListenerOrEventListenerObject =
  | WebViewEventListener
  | { handleEvent(object: Event & { data?: any }): void };

export interface WebView extends EventTarget {
  addEventListener(
    type: string,
    listener: WebViewEventListenerOrEventListenerObject,
    options?: boolean | AddEventListenerOptions
  ): void;

  postMessage(message: unknown): void;

  removeEventListener(
    type: string,
    listener: WebViewEventListenerOrEventListenerObject,
    options?: boolean | EventListenerOptions
  ): void;
}

declare global {
  interface Window {
    chrome: {
      webview?: WebView;
    };
  }
}

