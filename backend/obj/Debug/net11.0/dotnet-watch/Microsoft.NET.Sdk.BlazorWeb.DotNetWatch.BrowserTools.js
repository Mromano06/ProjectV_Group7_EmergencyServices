// dotnet-watch browser tools client.
//
// This module is part of the application build output. It must never be downloaded from the
// dotnet-watch provider: the provider is the party this client authenticates, so executable code
// served by it could not be trusted. The generated configuration module pins the provider's public
// key at build time and passes it to startBrowserTools.

const hotReloadActiveKey = '_dotnet_watch_hot_reload_active';
// Ensures we only connect once even when several activation paths import this module.
const scriptInjectedSentinel = '_dotnet_watch_ws_injected';
const replayResponseLoggingLevel = 1;
const AgentMessageSeverity_Warning = 1;
const AgentMessageSeverity_Error = 2;
// Bounds how long the replay waits for the Hot Reload agent. Only spent when there is something to
// replay and the apply API is still missing, which is the case that would otherwise lose updates.
const hotReloadAgentReadyTimeoutMs = 10000;
const browserToolsStyles = `
  :host {
    z-index: 1000000;
    position: fixed;
  }

  :host(#dotnet-compile-error) {
    inset: 0;
    background-color: rgba(0, 0, 0, 0.5);
    color: black;
    overflow: scroll;
  }

  .dotnet-compile-error-item {
    border: 2px solid red;
    padding: 8px;
    background-color: #faa;
  }

  .dotnet-compile-error-message {
    font-weight: bold;
  }

  :host(#dotnet-hotreload-toast) {
    width: 48px;
    height: 48px;
    top: 5px;
    left: 5px;
  }

  svg {
    filter: drop-shadow(0 2px 1px rgb(0 0 0 / 0.4));
  }

  #hotreloaded-ellipse1 {
    animation: hotreloaded-ellipse1_c_o 1800ms linear 1 normal forwards;
  }

  #hotreloaded-path1 {
    animation-name: hotreloaded-path1__m, hotreloaded-path1_c_o;
    animation-duration: 1800ms;
    animation-delay: 100ms;
    animation-fill-mode: forwards;
    animation-timing-function: linear;
    animation-direction: normal;
    animation-iteration-count: 1;
  }

  @keyframes hotreloaded-ellipse1_c_o {
    0% { opacity: 0; }
    16.666667% { opacity: 1; }
    72.222222% { opacity: 1; }
    90% { opacity: 0; }
    100% { opacity: 0; }
  }

  @keyframes hotreloaded-path1__m {
    0% { d: path('M126.151214,288.396852L196.625037,350.661591L320.793323,178.518242'); }
    16.666667% { d: path('M126.151214,288.396852L126.151214,288.396852L126.151214,288.396852'); }
    22.222222% {
      d: path('M126.151214,288.396852L196.625037,350.661591L196.625037,350.661591');
      animation-timing-function: cubic-bezier(0.42, 0, 0.58, 1);
    }
    33.333333% { d: path('M126.151214,288.396852L196.625037,350.661591L320.793323,178.518242'); }
    100% { d: path('M126.151214,288.396852L196.625037,350.661591L320.793323,178.518242'); }
  }

  @keyframes hotreloaded-path1_c_o {
    0% { opacity: 0; }
    16.666667% { opacity: 0; }
    22.222222% { opacity: 1; }
    72.222222% { opacity: 1; }
    90% { opacity: 0; }
    100% { opacity: 0; }
  }
`;
let browserToolsStylesheet;

/**
 * Connects to the dotnet-watch browser tools provider.
 *
 * @param {object} config Build-generated configuration.
 * @param {string} config.publicKey Base64 SubjectPublicKeyInfo of the provider's session key.
 * @param {string} config.connectPath Root-relative route of the provider's WebSocket endpoint.
 * @param {string} config.clearCachePath Root-relative route of the provider's cache reset endpoint.
 * @param {string} config.moduleUrl URL of the generated configuration module. Polled to detect that
 *                                  the application is reachable again after it restarted.
 */
export async function startBrowserTools(config) {
  if (window.hasOwnProperty(scriptInjectedSentinel)) {
    return;
  }
  window[scriptInjectedSentinel] = true;

  const { publicKey, connectPath, clearCachePath, moduleUrl } = config;

  const sharedSecret = await getSecret(publicKey);
  if (!sharedSecret) {
    delete window[scriptInjectedSentinel];
    console.debug('Unable to protect the dotnet-watch browser tools connection. Browser tools are disabled.');
    return;
  }

  const connectUrl = new URL(connectPath, document.baseURI);
  connectUrl.protocol = connectUrl.protocol === 'https:' ? 'wss:' : 'ws:';

  let closing = false;
  let waiting = false;

  // The provider sends the session initialization message, which carries the updates produced
  // before this browser connected, as the first message on the socket. It releases live messages
  // for this connection only after the browser acknowledges that message, so chaining the live
  // message queue on the initialization promise preserves the order even if a live message somehow
  // arrives early.
  let completeInitialization;
  let failInitialization;
  const initialized = new Promise((resolve, reject) => { completeInitialization = resolve; failInitialization = reject; });
  initialized.catch(() => { });

  let initializationMessageReceived = false;
  let receiveInitializationMessage;
  const initializationMessage = new Promise(resolve => receiveInitializationMessage = resolve);
  let messageQueue = initialized;

  let connection;
  try {
    connection = await getWebSocket(connectUrl, sharedSecret.encryptedSharedSecret, message => {
      if (!initializationMessageReceived) {
        initializationMessageReceived = true;
        receiveInitializationMessage(message);
      } else {
        messageQueue = messageQueue
          .then(() => handleMessage(message))
          .catch(error => console.debug('Failed to process a browser refresh message.', error));
      }
    });
  } catch (ex) {
    console.debug(ex);
  }

  if (!connection) {
    delete window[scriptInjectedSentinel];
    console.debug('Unable to establish a connection to the browser refresh server.');
    return;
  }

  connection.onerror = function (event) { console.debug('dotnet-watch reload socket error.', event) }
  connection.onclose = function () {
    delete window[scriptInjectedSentinel];
    failInitialization('The browser tools connection was closed.');
    console.debug('dotnet-watch reload socket closed.');
    if (!closing) {
      // The browser reaches the provider through the application, so the socket dies when the
      // application restarts and any pending Reload message is lost. Reload once it is back.
      reloadWhenApplicationReturns();
    }
  }

  if (await initializeSession()) {
    completeInitialization();
  } else {
    closing = true;
    connection.close();
  }

  async function initializeSession() {
    let payload;
    try {
      payload = JSON.parse((await withTimeout(initializationMessage, 30000)).data);
    } catch (error) {
      console.debug('Unable to initialize the browser tools session.', error);
      return false;
    }

    if (payload.type !== 'InitializeSession') {
      console.error(`Expected the browser tools session initialization message but received '${payload.type}'.`);
      return false;
    }

    if (!authenticateProvider(payload.sharedSecret)) {
      console.error('Unable to validate the browser refresh server. Closing the connection.');
      return false;
    }

    const log = [];
    let applyError;
    try {
      const updates = payload.updates ?? [];
      if (updates.length && !(await waitForDeltaApplyApi())) {
        // Keep initializing: the connection is still useful for reloads, diagnostics and CSS, and
        // failing here would close the socket and reload the page into the same state. Surface the
        // condition in the dotnet-watch console instead.
        log.push({
          "message": 'The Hot Reload agent did not become available in time, so the updates produced before this browser connected were not applied.',
          "severity": AgentMessageSeverity_Warning
        });
      }

      for (const update of updates) {
        try {
          const entries = applyDeltas(update.deltas, replayResponseLoggingLevel);
          if (entries && entries.length) {
            log.push(...entries);
          }
        } catch (error) {
          // Report the failure without failing the initialization, for the same reason as above:
          // dotnet-watch prints these entries as errors, while closing the socket would only make
          // the page reload into a browser that cannot apply updates either.
          console.warn('Unable to replay Hot Reload updates.', error);
          log.push({ "message": getMessageAndStack(error), "severity": AgentMessageSeverity_Error });
        }
      }
    } catch (error) {
      console.warn('Unable to initialize the browser tools session.', error);
      applyError = error;
      log.push({ "message": getMessageAndStack(error), "severity": AgentMessageSeverity_Error });
    }

    connection.send(JSON.stringify({ "success": !applyError, "log": log }));
    return !applyError;
  }

  async function handleMessage(message) {
    const payload = JSON.parse(message.data);
    const action = {
      'Reload': () => reload(),
      'Wait': () => wait(),
      'UpdateStaticFile': () => updateStaticFile(payload.path),
      'ApplyManagedCodeUpdates': () => applyManagedCodeUpdates(payload.sharedSecret, payload.deltas, payload.responseLoggingLevel),
      'ReportDiagnostics': () => reportDiagnostics(payload.diagnostics),
      'RefreshBrowser': () => refreshBrowser()
    };

    if (payload.type && action.hasOwnProperty(payload.type)) {
      await action[payload.type]();
    } else {
      console.error('Unknown payload:', message.data);
    }
  }

  // The provider proves it decrypted the secret this browser generated, which is only possible for
  // the process holding the private key matching the build-pinned public key.
  function authenticateProvider(providerSecret) {
    return providerSecret === sharedSecret.encodedSharedSecret;
  }

  async function reloadWhenApplicationReturns() {
    while (true) {
      await new Promise(resolve => setTimeout(resolve, 100));
      try {
        const response = await fetch(moduleUrl, { cache: 'no-store' });
        if (response.ok) {
          location.reload();
          return;
        }
      } catch (error) {
        // The application is still restarting.
      }
    }
  }

  function updateStaticFile(path) {
    if (path && path.endsWith('.css')) {
      updateCssByPath(path);
    } else {
      console.debug(`File change detected to file ${path}. Reloading page...`);
      location.reload();
      return;
    }
  }

  async function updateCssByPath(path) {
    const styleElement = document.querySelector(`link[href^="${path}"]`) ||
      document.querySelector(`link[href^="${document.baseURI}${path}"]`);

    // Receive a Clear-site-data header.
    await fetch(new URL(clearCachePath, document.baseURI), { cache: 'no-store' });

    if (!styleElement || !styleElement.parentNode) {
      console.debug('Unable to find a stylesheet to update. Updating all local css files.');
      updateAllLocalCss();
    }

    updateCssElement(styleElement);
  }

  function updateAllLocalCss() {
    [...document.querySelectorAll('link')]
      .filter(l => l.baseURI === document.baseURI)
      .forEach(e => updateCssElement(e));
  }

  function updateCssElement(styleElement) {
    if (!styleElement || styleElement.loading) {
      // A file change notification may be triggered for the same file before the browser
      // finishes processing a previous update. In this case, it's easiest to ignore later updates
      return;
    }

    const newElement = styleElement.cloneNode();
    const href = styleElement.href;
    newElement.href = href.split('?', 1)[0] + `?nonce=${Date.now()}`;

    styleElement.loading = true;
    newElement.loading = true;
    newElement.addEventListener('load', function () {
      newElement.loading = false;
      styleElement.remove();
    });

    styleElement.parentNode.insertBefore(newElement, styleElement.nextSibling);
  }

  function applyDeltas_legacy(deltas) {
    let apply = window.Blazor?._internal?.applyHotReload

    // Only apply hot reload deltas if Blazor has been initialized.
    // It's possible for Blazor to start after the initial page load, so we don't consider skipping this step
    // to be a failure. These deltas will get applied later, when Blazor completes initialization.
    if (apply) {
      deltas.forEach(d => {
        if (apply.length == 5) {
          // WASM 8.0
          apply(d.moduleId, d.metadataDelta, d.ilDelta, d.pdbDelta, d.updatedTypes)
        } else {
          // WASM 9.0
          apply(d.moduleId, d.metadataDelta, d.ilDelta, d.pdbDelta)
        }
      });
    }
  }

  function applyDeltas(deltas, responseLoggingLevel) {
    let applyDeltas = hotReloadAgentSignal().applyHotReloadDeltas ?? window.Blazor?._internal?.applyHotReloadDeltas
    if (applyDeltas) {
      // Only apply hot reload deltas if Blazor has been initialized.
      // It's possible for Blazor to start after the initial page load, so we don't consider skipping this step
      // to be a failure. These deltas will get applied later, when Blazor completes initialization.

      let wasmDeltas = deltas.map(delta => {
        return {
          "moduleId": delta.moduleId,
          "metadataDelta": delta.metadataDelta,
          "ilDelta": delta.ilDelta,
          "pdbDelta": delta.pdbDelta,
          "updatedTypes": delta.updatedTypes,
        };
      });

      return applyDeltas(wasmDeltas, responseLoggingLevel);
    }

    // Try invoke older WASM API:
    applyDeltas_legacy(deltas)
    return [];
  }

  function applyManagedCodeUpdates(providerSecret, deltas, responseLoggingLevel) {
    if (!authenticateProvider(providerSecret)) {
      throw 'Unable to validate the server. Rejecting apply-update payload.';
    }

    console.debug('Applying managed code updates.');

    let applyError = undefined;
    let log = [];
    try {
      log = applyDeltas(deltas, responseLoggingLevel);
    } catch (error) {
      console.warn(error);
      applyError = error;
      log.push({ "message": getMessageAndStack(error), "severity": AgentMessageSeverity_Error });
    }

    connection.send(JSON.stringify({
      "success": !applyError,
      "log": log
    }));

    if (!applyError) {
      displayChangesAppliedToast();
    }
  }

  function reportDiagnostics(diagnostics) {
    console.debug('Reporting Hot Reload diagnostics.');

    document.querySelectorAll('#dotnet-compile-error').forEach(el => el.remove());

    if (diagnostics.length == 0) {
      return;
    }

    const { host: el, root } = createBrowserToolsHost('dotnet-compile-error');
    document.body.appendChild(el);
    diagnostics.forEach(error => {
      const item = root.appendChild(document.createElement('div'));
      item.className = 'dotnet-compile-error-item';
      const message = item.appendChild(document.createElement('div'));
      message.className = 'dotnet-compile-error-message';
      message.textContent = error.Message;
      item.appendChild(document.createElement('div')).textContent = error;
    });
  }

  function displayChangesAppliedToast() {
    document.querySelectorAll('#dotnet-compile-error').forEach(el => el.remove());
    if (document.querySelector('#dotnet-hotreload-toast')) {
      return;
    }
    if (!window[hotReloadActiveKey])
    {
        return;
    }
    const { host: el, root } = createBrowserToolsHost('dotnet-hotreload-toast');
    root.appendChild(createHotReloadToastIcon());
    document.body.appendChild(el);
    window[hotReloadActiveKey] = false;
    setTimeout(() => el.remove(), 2000);
  }

  function createHotReloadToastIcon() {
    const svgNamespace = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(svgNamespace, 'svg');
    svg.setAttribute('viewBox', '0 0 500 500');

    const ellipse = svg.appendChild(document.createElementNS(svgNamespace, 'ellipse'));
    ellipse.id = 'hotreloaded-ellipse1';
    ellipse.setAttribute('rx', '212.808853');
    ellipse.setAttribute('ry', '205.404598');
    ellipse.setAttribute('transform', 'matrix(0.982102 0 0 1.017504 251 238)');
    ellipse.setAttribute('opacity', '0');
    ellipse.setAttribute('fill', 'rgb(120,120,120)');

    const path = svg.appendChild(document.createElementNS(svgNamespace, 'path'));
    path.id = 'hotreloaded-path1';
    path.setAttribute('d', 'M126.151214,288.396852L196.625037,350.661591L320.793323,178.518242');
    path.setAttribute('transform', 'matrix(1 0 0 1 27.527732 -26.589916)');
    path.setAttribute('opacity', '0');
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', 'rgb(255,255,255)');
    path.setAttribute('stroke-width', '40');
    path.setAttribute('stroke-linecap', 'round');
    return svg;
  }

  function createBrowserToolsHost(id) {
    const host = document.createElement('div');
    host.id = id;
    const root = host.attachShadow({ mode: 'open' });
    if ('adoptedStyleSheets' in root && typeof CSSStyleSheet === 'function') {
      if (!browserToolsStylesheet) {
        browserToolsStylesheet = new CSSStyleSheet();
        browserToolsStylesheet.replaceSync(browserToolsStyles);
      }

      root.adoptedStyleSheets = [browserToolsStylesheet];
    }

    return { host, root };
  }

  function refreshBrowser() {
    if (window.Blazor) {
      window[hotReloadActiveKey] = true;
      // hotReloadApplied triggers an enhanced navigation to
      // refresh pages that have been statically rendered with
      // Blazor SSR.
      if (window.Blazor?._internal?.hotReloadApplied)
      {
        console.debug('Refreshing browser: WASM.');
        Blazor._internal.hotReloadApplied();
      }
      else
      {
        console.debug('Refreshing browser.');
        displayChangesAppliedToast();
      }
    } else {
      console.debug('Refreshing browser: Reloading.');
      location.reload();
    }
  }

  function reload() {
    console.debug('Reloading.');
    location.reload();
  }

  function wait() {
    console.debug('Waiting for application to rebuild.');

    if (waiting) {
      return;
    }

    waiting = true;
    const glyphs = ['☱', '☲', '☴'];
    const title = document.title;
    let i = 0;
    setInterval(function () { document.title = glyphs[i++ % glyphs.length] + ' ' + title; }, 240);
  }

  function getWebSocket(url, encryptedSecret, onMessage) {
    return new Promise((resolve, reject) => {
      const webSocket = new WebSocket(url, encodeURIComponent(encryptedSecret));
      let opened = false;

      // Listen for messages before the socket opens so that the session initialization message,
      // which the provider sends first, can never be missed.
      webSocket.addEventListener('message', onMessage);

      function onOpen() {
        opened = true;
        clearEventListeners();
        resolve(webSocket);
      }

      function onClose(event) {
        if (opened) {
          // Open completed successfully. Nothing to do here.
          return;
        }

        let error = 'WebSocket failed to connect.';
        if (event instanceof ErrorEvent) {
          error = event.error;
        }

        clearEventListeners();
        reject(error);
      }

      function clearEventListeners() {
        webSocket.removeEventListener('open', onOpen);
        // The error event isn't as reliable, but close is always called even during failures.
        // If close is called without a corresponding open, we can reject the promise.
        webSocket.removeEventListener('close', onClose);
      }

      webSocket.addEventListener('open', onOpen);
      webSocket.addEventListener('close', onClose);
      if (window.Blazor?.removeEventListener && window.Blazor?.addEventListener)
      {
        webSocket.addEventListener('close', () => window.Blazor?.removeEventListener('enhancedload', displayChangesAppliedToast));
        window.Blazor?.addEventListener('enhancedload', displayChangesAppliedToast);
      }
    });
  }
}

function getMessageAndStack(error) {
  const message = error.message || '<unknown error>'
  let messageAndStack = error.stack || message
  if (!messageAndStack.includes(message)) {
    messageAndStack = message + "\n" + messageAndStack;
  }

  return messageAndStack
}

// Rendezvous with the WebAssembly initializer. Either module may create the object first because
// library initializer module evaluation order is not guaranteed; the initializer resolves it.
function hotReloadAgentSignal() {
  const agent = globalThis.__DOTNET_WATCH_HOT_RELOAD_AGENT ||= {};
  if (!agent.ready) {
    agent.ready = new Promise(resolve => { agent.setReady = resolve; });
  }

  return agent;
}

function hasDeltaApplyApi() {
  return !!(hotReloadAgentSignal().applyHotReloadDeltas || window.Blazor?._internal?.applyHotReloadDeltas || window.Blazor?._internal?.applyHotReload);
}

// The provider sends the replay snapshot once, so applying it before the agent installed the apply
// API would drop those updates while still reporting success. Wait for the agent to finish starting.
// Runtimes that install the apply API through their own bootstrap never publish the signal, so the
// wait is bounded and best effort.
async function waitForDeltaApplyApi() {
  if (hasDeltaApplyApi()) {
    return true;
  }

  try {
    await withTimeout(hotReloadAgentSignal().ready, hotReloadAgentReadyTimeoutMs);
  } catch (error) {
    console.debug('Timed out waiting for the Hot Reload agent.', error);
  }

  return hasDeltaApplyApi();
}

function withTimeout(promise, milliseconds) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(`Timed out after ${milliseconds}ms.`), milliseconds);
    promise.then(
      value => { clearTimeout(timeout); resolve(value); },
      error => { clearTimeout(timeout); reject(error); });
  });
}

// Generates the secret this browser uses to authenticate the provider and encrypts it with the
// build-pinned public key. The secret itself never leaves the browser in clear text and is never
// persisted.
async function getSecret(serverKeyString) {
  if (!serverKeyString || !window.crypto || !window.crypto.subtle) {
    return null;
  }

  const secretBytes = window.crypto.getRandomValues(new Uint8Array(32)); // 32-bytes of entropy

  // Based on https://developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto/importKey#subjectpublickeyinfo_import
  const binaryServerKey = str2ab(atob(serverKeyString));
  const serverKey = await window.crypto.subtle.importKey('spki', binaryServerKey, { name: "RSA-OAEP", hash: "SHA-256" }, false, ['encrypt']);
  const encrypted = await window.crypto.subtle.encrypt({ name: 'RSA-OAEP' }, serverKey, secretBytes);
  return {
    encryptedSharedSecret: btoa(String.fromCharCode(...new Uint8Array(encrypted))),
    encodedSharedSecret: btoa(String.fromCharCode(...secretBytes)),
  };

  function str2ab(str) {
    const buf = new ArrayBuffer(str.length);
    const bufView = new Uint8Array(buf);
    for (let i = 0, strLen = str.length; i < strLen; i++) {
      bufView[i] = str.charCodeAt(i);
    }
    return buf;
  }
}

// SIG // Begin signature block
// SIG // MIInWwYJKoZIhvcNAQcCoIInTDCCJ0gCAQExDzANBglg
// SIG // hkgBZQMEAgEFADB3BgorBgEEAYI3AgEEoGkwZzAyBgor
// SIG // BgEEAYI3AgEeMCQCAQEEEBDgyQbOONQRoqMAEEvTUJAC
// SIG // AQACAQACAQACAQACAQAwMTANBglghkgBZQMEAgEFAAQg
// SIG // AyqKBPE8oMmCigalvzCFPL2zcWZNrLsjCYx7C1xPwy+g
// SIG // ggy4MIIF8zCCA9ugAwIBAgITMwAAAceaoe7cJ+L4twAA
// SIG // AAABxzANBgkqhkiG9w0BAQsFADBXMQswCQYDVQQGEwJV
// SIG // UzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
// SIG // MSgwJgYDVQQDEx9NaWNyb3NvZnQgQ29kZSBTaWduaW5n
// SIG // IFBDQSAyMDI0MB4XDTI2MDQxNjE4NTczOVoXDTI3MDQx
// SIG // NTE4NTczOVowYzELMAkGA1UEBhMCVVMxEzARBgNVBAgT
// SIG // Cldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAc
// SIG // BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjENMAsG
// SIG // A1UEAxMELk5FVDCCASIwDQYJKoZIhvcNAQEBBQADggEP
// SIG // ADCCAQoCggEBAMB61gBm+zIpG+zndRVKQsKhMDkm93i+
// SIG // sXwp1OHJ+EGnqv1EntlMxQ3XglhWpxS83yMw+VBm/IAT
// SIG // tMIr2/2LITEnBBgY8+EA+SCxn1G0cBlR0WhlEvQs49DG
// SIG // k4iUoAbAyEDjThvokHS6apuvqwViuP+cFci9SS4x6a45
// SIG // h+ujrl5qy77RkgYpBhapvgPLM1zvtPsCzh1t7j2K/05r
// SIG // 4JJAJqWIPZ+PjSvXJLKW95EH3vxPhtfdhm6sEK4xpcKM
// SIG // CG7qsL/dCqhGeHk+IQgxTecwZyWbMyY305PiUnGcc728
// SIG // 8wHNr36J3Z8c5BWFWptyocQafTXjiMil7OS8KYmhgHYg
// SIG // 6xkCAwEAAaOCAaowggGmMA4GA1UdDwEB/wQEAwIHgDAf
// SIG // BgNVHSUEGDAWBgorBgEEAYI3TAgBBggrBgEFBQcDAzAd
// SIG // BgNVHQ4EFgQUgAm0ef/T6uytGybTjdg8DFX/L58wVAYD
// SIG // VR0RBE0wS6RJMEcxLTArBgNVBAsTJE1pY3Jvc29mdCBJ
// SIG // cmVsYW5kIE9wZXJhdGlvbnMgTGltaXRlZDEWMBQGA1UE
// SIG // BRMNNDY0MjIzKzUwNzYwNjAfBgNVHSMEGDAWgBR/WT9U
// SIG // IdqtT+8F5eaj1y0GlBIIMTBgBgNVHR8EWTBXMFWgU6BR
// SIG // hk9odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3Bz
// SIG // L2NybC9NaWNyb3NvZnQlMjBDb2RlJTIwU2lnbmluZyUy
// SIG // MFBDQSUyMDIwMjQuY3JsMG0GCCsGAQUFBwEBBGEwXzBd
// SIG // BggrBgEFBQcwAoZRaHR0cDovL3d3dy5taWNyb3NvZnQu
// SIG // Y29tL3BraW9wcy9jZXJ0cy9NaWNyb3NvZnQlMjBDb2Rl
// SIG // JTIwU2lnbmluZyUyMFBDQSUyMDIwMjQuY3J0MAwGA1Ud
// SIG // EwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggIBAImzEt/g
// SIG // Gt+QAA3NGlRZUv+koTULWxSFT/osH1YxbVKFgSYU9dA9
// SIG // BpFDzo1lF+IhVTgjjwHXhaA87P4YTztl3RQfrlrrED7F
// SIG // 008DHiJ+G/7nnTxkb7y9fNRTTw9Ac/hGTWkQBW5Vaujm
// SIG // gWQflToTpMKNlqVbGFg+UVZKxi+k5MhsULjKt5K/ulH5
// SIG // bVuvnXrZmeF3XRGuSsQe2YjpNYaYHq713itwdNwyYq7p
// SIG // rpQ4R3xiUBw6SOOaH2UyDdhyQisZl8V3wFNhY2t6yZkQ
// SIG // CyGG+GZF49Q8vc1l+Tl+pcRa8l+4u3Rq18QUDJenW4Up
// SIG // 5y/a+mLTyxM8pYRQpPDqVX5U9NTfLbgZWKxQmkN+0mpJ
// SIG // 4CRpAniIiJJC4ag7Wjky+Asgik8xb/16wqiw72xDdPCk
// SIG // 7TN0g/G4PlmyyDP+hdSjzlq5JiQK2ubfEhAqoRD1tmKK
// SIG // 4R3QqIFlLZsPjE87AXlZ4PJHzutH2YnNsUQ45oDDCf3j
// SIG // 6vfslGL01M3XAgkDXhskyOXxb1v7of0JR8GzCvsIkNeM
// SIG // QmeXc5FZwi7xXG6UeNh1Z4SA3qJo+H+ItV/dMgjxCWPl
// SIG // Yfzgh6a2CXXaEruZvnLpwD+cCuZxYhGYIJfrsWoCh4Gf
// SIG // AtkvG3Z0fHgeftB90byXroQbupqohCUppbug9df+2PjO
// SIG // aPWk0oPvu/4HzFaZMIIGvTCCBKWgAwIBAgITMwAAADk7
// SIG // tjcZvwYdZwAAAAAAOTANBgkqhkiG9w0BAQwFADCBiDEL
// SIG // MAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24x
// SIG // EDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jv
// SIG // c29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UEAxMpTWljcm9z
// SIG // b2Z0IFJvb3QgQ2VydGlmaWNhdGUgQXV0aG9yaXR5IDIw
// SIG // MTEwHhcNMjQwODA4MjA1NDE4WhcNMzYwMzIyMjIxMzA0
// SIG // WjBXMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9z
// SIG // b2Z0IENvcnBvcmF0aW9uMSgwJgYDVQQDEx9NaWNyb3Nv
// SIG // ZnQgQ29kZSBTaWduaW5nIFBDQSAyMDI0MIICIjANBgkq
// SIG // hkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA2AGcHuM4x6qV
// SIG // Fgc1rbrm/ghj18fxMqF6Yd88g17WCktpXd2GDfbhbAfT
// SIG // otwdumewG6QIM2K2vKjR21L8Rboj/IQv9stJjyEqlt9C
// SIG // 5a6wE+v2prNhwPEJb+qeNvkmwjWzxs06DdtUBO9BUvi1
// SIG // x/TdpPJyzPxB4J0zSX/IdE5sn1CprMzWvpU8Q4nssv64
// SIG // QRvvfDpAK6Gzz1rW6/XN6s5gyeyd5FHAJunJbXUhyCBT
// SIG // RxEoMOrWmNUnMhsgXr6iJddtF46yB4jzO7UXJB8rR9WR
// SIG // rJrxKZUdD+05/beZnhb2TRGLbZBb2ndSBILk5QOS0rHF
// SIG // wKYYvq1ct84ZJYcghXhitMlNPo823LlESiMcm5kcCuQX
// SIG // 1WcdMRFahOMDN8jeQ/7lvhqVR2GABnu2067VtWdd8dqo
// SIG // 9iXas+zbSOLTcs5VayH+tp2ATXt6zmEv63qVXR5UetWG
// SIG // yyxE5Ym7PYxcwK3uLDuUU8b0tcoaOyaiefaCi0Z0ci4S
// SIG // DkmckwlWaLF3ktGWSaBhFkrOHFILDKgYirQ+FoDtj5U9
// SIG // y3mkIeSKNwggObSeWQr7QrJ6miVyoabRP8ZhBEyEcmUY
// SIG // 46ZVCinfrBRVRiSVTL768NZ4SASjizuHE3qYht/YxIhD
// SIG // +Ih8xmAnELr2i6QxRcKs4LdKQT/EiSCl+XbYwzWK2Rnf
// SIG // mc1eQyiVTWUCAwEAAaOCAU4wggFKMA4GA1UdDwEB/wQE
// SIG // AwIBhjAQBgkrBgEEAYI3FQEEAwIBADAdBgNVHQ4EFgQU
// SIG // f1k/VCHarU/vBeXmo9ctBpQSCDEwGQYJKwYBBAGCNxQC
// SIG // BAweCgBTAHUAYgBDAEEwDwYDVR0TAQH/BAUwAwEB/zAf
// SIG // BgNVHSMEGDAWgBRyLToCMZBDuRQFTuHqp8cx0SOJNDBa
// SIG // BgNVHR8EUzBRME+gTaBLhklodHRwOi8vY3JsLm1pY3Jv
// SIG // c29mdC5jb20vcGtpL2NybC9wcm9kdWN0cy9NaWNSb29D
// SIG // ZXJBdXQyMDExXzIwMTFfMDNfMjIuY3JsMF4GCCsGAQUF
// SIG // BwEBBFIwUDBOBggrBgEFBQcwAoZCaHR0cDovL3d3dy5t
// SIG // aWNyb3NvZnQuY29tL3BraS9jZXJ0cy9NaWNSb29DZXJB
// SIG // dXQyMDExXzIwMTFfMDNfMjIuY3J0MA0GCSqGSIb3DQEB
// SIG // DAUAA4ICAQAUlB84KE/uiefp8sgwqtKU3VZgrAMWAB13
// SIG // KY5Q7cWszx3sH9b+JDoPFewOfsPlbjAzBh4vKy1wSp+S
// SIG // PPg1RFGBrPIy7nJHNCHguqMDi1K1NwmHWikTGjuefk+4
// SIG // 8Fidu7T5MdK5UdN7RVNM9WGKXL+mIWsOjdrFD0/gL46X
// SIG // nJ637aBN96QgJLnFL5xh9Ii+CfQmSxUFUxhUjlAW7+qG
// SIG // cuGwQURTMbx++/SGOCQ76WSlX23LoaQ3i92d3vJrpDpp
// SIG // H3LfhqIzWqbFrEGLo5SfI2Xp+S66f92JMWdgMtOmk6Sv
// SIG // +aDlZJ8KINUw0LG2PjA8oLk6YebUNAi38w2iRtsfdQaw
// SIG // U/VBvOwuhy5KosK8fT0ijd8M9OaxxH1jvkbipftFNfwB
// SIG // 0E+jQjo4SiN/f3O4Vm3So4ebrlhZATr1xkza54TUwHTl
// SIG // 002Acr2BMTvMq8r9+DwHaqNbzwxP9YXlXm69ka2pr0VI
// SIG // vZFrMCsD6sM+5/okZjPgemAxkcHhLqzNZIpgG/RWKwLN
// SIG // /GB5T52q5db1t3Rq5iU4HnwM9w5gp1zdJ73iD7EvilwS
// SIG // FsHngk6ACTBhO7/10t4fakOp4lkAAFUNZFAJpd87kuDI
// SIG // rAoIthemKCtlgKNRIFyv5V7w8VYyFVNCXS/irwn8BSZA
// SIG // 3lbifXTVxjYvgDsZNAbWHfYccC99ARJY/TGCGfswghn3
// SIG // AgEBMG4wVzELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1p
// SIG // Y3Jvc29mdCBDb3Jwb3JhdGlvbjEoMCYGA1UEAxMfTWlj
// SIG // cm9zb2Z0IENvZGUgU2lnbmluZyBQQ0EgMjAyNAITMwAA
// SIG // Aceaoe7cJ+L4twAAAAABxzANBglghkgBZQMEAgEFAKCB
// SIG // rjAZBgkqhkiG9w0BCQMxDAYKKwYBBAGCNwIBBDAcBgor
// SIG // BgEEAYI3AgELMQ4wDAYKKwYBBAGCNwIBFTAvBgkqhkiG
// SIG // 9w0BCQQxIgQgU6/eb4wRnlHRiTah24qLb1NVP0fcdhk9
// SIG // 3TPwxg2dxH4wQgYKKwYBBAGCNwIBDDE0MDKgFIASAE0A
// SIG // aQBjAHIAbwBzAG8AZgB0oRqAGGh0dHA6Ly93d3cubWlj
// SIG // cm9zb2Z0LmNvbTANBgkqhkiG9w0BAQEFAASCAQAkrQbv
// SIG // x90glOywiRWKQowms/NsZgumj7JM0su5+BHdy+VHJzAk
// SIG // dAe7+7ky1hONGqQyes19RrrdsIx+sjlN1Z6tyJx9JbNn
// SIG // 4iE33u8l8JzVcLxFkNF+R5dvT4/CUZOb+EL4gtJta1/F
// SIG // Lcyb6vgs774tqPieRopBjm9VnFMOqx2qFvV+aN2kPWyo
// SIG // 4ifnVKNOT8bJJmpCJikehnqFmOfRYQ4aAX3EakYzYGqZ
// SIG // FD6ecKx5D031+4gzrOPiX1bizI3wMC0Y7tK8j2bj151e
// SIG // LVLuZ+Al2wl214vNYopbJ/HgqM4XdNIDrNKcstSnfcsG
// SIG // PmlUvorhGd6hIAc9JdHKboU4hiI4oYIXrTCCF6kGCisG
// SIG // AQQBgjcDAwExgheZMIIXlQYJKoZIhvcNAQcCoIIXhjCC
// SIG // F4ICAQMxDzANBglghkgBZQMEAgEFADCCAVoGCyqGSIb3
// SIG // DQEJEAEEoIIBSQSCAUUwggFBAgEBBgorBgEEAYRZCgMB
// SIG // MDEwDQYJYIZIAWUDBAIBBQAEIJMeuC2RzQM+tPdeABTI
// SIG // wW/qKyJ0qmYFILsBjyz8+5sWAgZqzhRn430YEzIwMjYx
// SIG // MDIzMTAzNDA0LjA4M1owBIACAfSggdmkgdYwgdMxCzAJ
// SIG // BgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAw
// SIG // DgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3Nv
// SIG // ZnQgQ29ycG9yYXRpb24xLTArBgNVBAsTJE1pY3Jvc29m
// SIG // dCBJcmVsYW5kIE9wZXJhdGlvbnMgTGltaXRlZDEnMCUG
// SIG // A1UECxMeblNoaWVsZCBUU1MgRVNOOjU1MUEtMDVFMC1E
// SIG // OTQ3MSUwIwYDVQQDExxNaWNyb3NvZnQgVGltZS1TdGFt
// SIG // cCBTZXJ2aWNloIIR+zCCBygwggUQoAMCAQICEzMAAAIb
// SIG // 0LK4Amf3cs8AAQAAAhswDQYJKoZIhvcNAQELBQAwfDEL
// SIG // MAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24x
// SIG // EDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jv
// SIG // c29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9z
// SIG // b2Z0IFRpbWUtU3RhbXAgUENBIDIwMTAwHhcNMjUwODE0
// SIG // MTg0ODMwWhcNMjYxMTEzMTg0ODMwWjCB0zELMAkGA1UE
// SIG // BhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNV
// SIG // BAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBD
// SIG // b3Jwb3JhdGlvbjEtMCsGA1UECxMkTWljcm9zb2Z0IEly
// SIG // ZWxhbmQgT3BlcmF0aW9ucyBMaW1pdGVkMScwJQYDVQQL
// SIG // Ex5uU2hpZWxkIFRTUyBFU046NTUxQS0wNUUwLUQ5NDcx
// SIG // JTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1lLVN0YW1wIFNl
// SIG // cnZpY2UwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIK
// SIG // AoICAQCOxZ3nZlmTMHld7mD+XYaw6MDPfSyDqNXF8UlX
// SIG // 7DjEgNXJojcs7xsimbNi6XcBkeDnRQhDw+tJFkalCoWR
// SIG // E276jdgoniDa4ZgFGSwecdhHS5VIJCDnxOGRjJ6mUZfe
// SIG // gC8ZFW48ilC0CJOxHvoD+B2hTscPARtvvdsnBPKtsoeF
// SIG // H5ZozL0NAcjiTlCjj5tkOzSSPvpu+Em90ZT5LzPFAGnt
// SIG // QCGMmcWorEi6xIhMTvMIJHjbYQuGSFVU4WorbDqHUwC8
// SIG // gt7vqHFEhw+PRIEvavw723HmeNTj62DasB1TXnembKGp
// SIG // rN2lRxxgET3ANEVR3970KhbHtN2dSJwH4xqLtFPqqx7t
// SIG // 7loapfUHtueP9ke+ut8X4EkQiVL2INcBSB6S9dn4VmaO
// SIG // 8vA/5037T9yuH76vh7wWScXsRfogl+eY14M3/rxnn2Rt
// SIG // onV/4/macph/J0J5mbGsalLS1paQOTfoPeM9Vl+W/Gtz
// SIG // 7WuEIiUzm/1qAsQUjXZCIFN+k4E4GvcAYI+T54fT6Vq2
// SIG // NBqO6D7b8EPXapvzbnTQtDK1RZPai1r8didGBK/WO9nT
// SIG // 92aXUWzFZjM6cKuN90H/s3qk3JK3i+f48Y3p0UuKbuTG
// SIG // iz4H1Z9A97MmLd+4rLIMAH3NIc+PVm7ydl95xkn26bjO
// SIG // PsMWC8ldMNOcbmqUbhl1sVFr+ut/OQIDAQABo4IBSTCC
// SIG // AUUwHQYDVR0OBBYEFLa+n3f+XEumk0rw6Rq4nYC82YhQ
// SIG // MB8GA1UdIwQYMBaAFJ+nFV0AXmJdg/Tl0mWnG1M1Gely
// SIG // MF8GA1UdHwRYMFYwVKBSoFCGTmh0dHA6Ly93d3cubWlj
// SIG // cm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUy
// SIG // MFRpbWUtU3RhbXAlMjBQQ0ElMjAyMDEwKDEpLmNybDBs
// SIG // BggrBgEFBQcBAQRgMF4wXAYIKwYBBQUHMAKGUGh0dHA6
// SIG // Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY2VydHMv
// SIG // TWljcm9zb2Z0JTIwVGltZS1TdGFtcCUyMFBDQSUyMDIw
// SIG // MTAoMSkuY3J0MAwGA1UdEwEB/wQCMAAwFgYDVR0lAQH/
// SIG // BAwwCgYIKwYBBQUHAwgwDgYDVR0PAQH/BAQDAgeAMA0G
// SIG // CSqGSIb3DQEBCwUAA4ICAQBmRTVfFAPg5MzcZOG3fZNd
// SIG // KEh88Ggx9KwWwFCoU5mosk7HIk6WUgEWmam860Y0+QLl
// SIG // nyV0bxoKm+AU2j+MNZ5PkWJbnd0CP0qdnGmxDc9/l9HN
// SIG // IYdFzEQw51chXMMnBxlRfRyN/GdrvJ02/x5cH9eTobpL
// SIG // KtHY4fpLUscxbXWbdS8oX54uMg+XjmvGKa4MKgR35p3S
// SIG // U4BcDn+9k4o3mf949h4/QtFyFlfRDofyf9mZI8yVuWLc
// SIG // w7znVDT1GZP9kYdr78V3L5YsOvBxjKRX2ZTL/hNvArDo
// SIG // W11Hpk8fEx0iLWmTxjaYL8bMKrQsKwfS5MV5DpDs1zcx
// SIG // GYRH/eYtZSFtpYeBfUVthyG9HbZv4G6n5g9HlD/QGFpo
// SIG // A3oAgF9waz67+cmggHLJkoDxxPIKadQj/i9boPi/LCDd
// SIG // cEV/h/YPAUfL96+wL7nwoyX6TbBrTlfaQrRP9sI8uFqi
// SIG // /1lfKhtrB804tgaJq4pPYVa9vBnMcgUJPGMHDDo+3m5G
// SIG // 8IT+OdRx//GGU4YyfqIo71e3j29lMTZJ8gGT/fiItNEE
// SIG // noftoY9NNCfNrc59a7X91HJwLpaXmiezc+OcZdNIpLFe
// SIG // WUk+aDpH+6Uaic/9QJignqY34ReN/IMs9cuqyv3X5VMb
// SIG // WtjNEKM/AEUAe/gQjBoTRqMKt/vl5QYjf6hdTRQ/quWh
// SIG // nzCCB3EwggVZoAMCAQICEzMAAAAVxedrngKbSZkAAAAA
// SIG // ABUwDQYJKoZIhvcNAQELBQAwgYgxCzAJBgNVBAYTAlVT
// SIG // MRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdS
// SIG // ZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9y
// SIG // YXRpb24xMjAwBgNVBAMTKU1pY3Jvc29mdCBSb290IENl
// SIG // cnRpZmljYXRlIEF1dGhvcml0eSAyMDEwMB4XDTIxMDkz
// SIG // MDE4MjIyNVoXDTMwMDkzMDE4MzIyNVowfDELMAkGA1UE
// SIG // BhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNV
// SIG // BAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBD
// SIG // b3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0IFRp
// SIG // bWUtU3RhbXAgUENBIDIwMTAwggIiMA0GCSqGSIb3DQEB
// SIG // AQUAA4ICDwAwggIKAoICAQDk4aZM57RyIQt5osvXJHm9
// SIG // DtWC0/3unAcH0qlsTnXIyjVX9gF/bErg4r25PhdgM/9c
// SIG // T8dm95VTcVrifkpa/rg2Z4VGIwy1jRPPdzLAEBjoYH1q
// SIG // UoNEt6aORmsHFPPFdvWGUNzBRMhxXFExN6AKOG6N7dcP
// SIG // 2CZTfDlhAnrEqv1yaa8dq6z2Nr41JmTamDu6GnszrYBb
// SIG // fowQHJ1S/rboYiXcag/PXfT+jlPP1uyFVk3v3byNpOOR
// SIG // j7I5LFGc6XBpDco2LXCOMcg1KL3jtIckw+DJj361VI/c
// SIG // +gVVmG1oO5pGve2krnopN6zL64NF50ZuyjLVwIYwXE8s
// SIG // 4mKyzbnijYjklqwBSru+cakXW2dg3viSkR4dPf0gz3N9
// SIG // QZpGdc3EXzTdEonW/aUgfX782Z5F37ZyL9t9X4C626p+
// SIG // Nuw2TPYrbqgSUei/BQOj0XOmTTd0lBw0gg/wEPK3Rxjt
// SIG // p+iZfD9M269ewvPV2HM9Q07BMzlMjgK8QmguEOqEUUbi
// SIG // 0b1qGFphAXPKZ6Je1yh2AuIzGHLXpyDwwvoSCtdjbwzJ
// SIG // NmSLW6CmgyFdXzB0kZSU2LlQ+QuJYfM2BjUYhEfb3BvR
// SIG // /bLUHMVr9lxSUV0S2yW6r1AFemzFER1y7435UsSFF5PA
// SIG // PBXbGjfHCBUYP3irRbb1Hode2o+eFnJpxq57t7c+auIu
// SIG // rQIDAQABo4IB3TCCAdkwEgYJKwYBBAGCNxUBBAUCAwEA
// SIG // ATAjBgkrBgEEAYI3FQIEFgQUKqdS/mTEmr6CkTxGNSnP
// SIG // EP8vBO4wHQYDVR0OBBYEFJ+nFV0AXmJdg/Tl0mWnG1M1
// SIG // GelyMFwGA1UdIARVMFMwUQYMKwYBBAGCN0yDfQEBMEEw
// SIG // PwYIKwYBBQUHAgEWM2h0dHA6Ly93d3cubWljcm9zb2Z0
// SIG // LmNvbS9wa2lvcHMvRG9jcy9SZXBvc2l0b3J5Lmh0bTAT
// SIG // BgNVHSUEDDAKBggrBgEFBQcDCDAZBgkrBgEEAYI3FAIE
// SIG // DB4KAFMAdQBiAEMAQTALBgNVHQ8EBAMCAYYwDwYDVR0T
// SIG // AQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTV9lbLj+iiXGJo
// SIG // 0T2UkFvXzpoYxDBWBgNVHR8ETzBNMEugSaBHhkVodHRw
// SIG // Oi8vY3JsLm1pY3Jvc29mdC5jb20vcGtpL2NybC9wcm9k
// SIG // dWN0cy9NaWNSb29DZXJBdXRfMjAxMC0wNi0yMy5jcmww
// SIG // WgYIKwYBBQUHAQEETjBMMEoGCCsGAQUFBzAChj5odHRw
// SIG // Oi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpL2NlcnRzL01p
// SIG // Y1Jvb0NlckF1dF8yMDEwLTA2LTIzLmNydDANBgkqhkiG
// SIG // 9w0BAQsFAAOCAgEAnVV9/Cqt4SwfZwExJFvhnnJL/Klv
// SIG // 6lwUtj5OR2R4sQaTlz0xM7U518JxNj/aZGx80HU5bbsP
// SIG // MeTCj/ts0aGUGCLu6WZnOlNN3Zi6th542DYunKmCVgAD
// SIG // sAW+iehp4LoJ7nvfam++Kctu2D9IdQHZGN5tggz1bSNU
// SIG // 5HhTdSRXud2f8449xvNo32X2pFaq95W2KFUn0CS9QKC/
// SIG // GbYSEhFdPSfgQJY4rPf5KYnDvBewVIVCs/wMnosZiefw
// SIG // C2qBwoEZQhlSdYo2wh3DYXMuLGt7bj8sCXgU6ZGyqVvf
// SIG // SaN0DLzskYDSPeZKPmY7T7uG+jIa2Zb0j/aRAfbOxnT9
// SIG // 9kxybxCrdTDFNLB62FD+CljdQDzHVG2dY3RILLFORy3B
// SIG // FARxv2T5JL5zbcqOCb2zAVdJVGTZc9d/HltEAY5aGZFr
// SIG // DZ+kKNxnGSgkujhLmm77IVRrakURR6nxt67I6IleT53S
// SIG // 0Ex2tVdUCbFpAUR+fKFhbHP+CrvsQWY9af3LwUFJfn6T
// SIG // vsv4O+S3Fb+0zj6lMVGEvL8CwYKiexcdFYmNcP7ntdAo
// SIG // GokLjzbaukz5m/8K6TT4JDVnK+ANuOaMmdbhIurwJ0I9
// SIG // JZTmdHRbatGePu1+oDEzfbzL6Xu/OHBE0ZDxyKs6ijoI
// SIG // Yn/ZcGNTTY3ugm2lBRDBcQZqELQdVTNYs6FwZvKhggNW
// SIG // MIICPgIBATCCAQGhgdmkgdYwgdMxCzAJBgNVBAYTAlVT
// SIG // MRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdS
// SIG // ZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9y
// SIG // YXRpb24xLTArBgNVBAsTJE1pY3Jvc29mdCBJcmVsYW5k
// SIG // IE9wZXJhdGlvbnMgTGltaXRlZDEnMCUGA1UECxMeblNo
// SIG // aWVsZCBUU1MgRVNOOjU1MUEtMDVFMC1EOTQ3MSUwIwYD
// SIG // VQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNl
// SIG // oiMKAQEwBwYFKw4DAhoDFQCGhXqvj0zgYF3jUrVFgHVn
// SIG // R/jO4KCBgzCBgKR+MHwxCzAJBgNVBAYTAlVTMRMwEQYD
// SIG // VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
// SIG // MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
// SIG // JjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBD
// SIG // QSAyMDEwMA0GCSqGSIb3DQEBCwUAAgUA7oUX1TAiGA8y
// SIG // MDI2MTAyMjIzMTYzN1oYDzIwMjYxMDIzMjMxNjM3WjB0
// SIG // MDoGCisGAQQBhFkKBAExLDAqMAoCBQDuhRfVAgEAMAcC
// SIG // AQACAgl6MAcCAQACAhJNMAoCBQDuhmlVAgEAMDYGCisG
// SIG // AQQBhFkKBAIxKDAmMAwGCisGAQQBhFkKAwKgCjAIAgEA
// SIG // AgMHoSChCjAIAgEAAgMBhqAwDQYJKoZIhvcNAQELBQAD
// SIG // ggEBABL6sovnK95LXmXypN9eH8dnpXvkHS0cYdr2y1ec
// SIG // fuc6XvUQCZZk30PfAfK2FUzgE3zVu9GobLrgk9VWCylz
// SIG // 4xMz81qowgE07eS1Jr2vEqiaaKFcaYD5zF745a79AV7R
// SIG // COpeTBoWw1TMXoNXHH6mPqeiNpjmhQ6tN8u09CdsL0lw
// SIG // uKWrig+gL1dNmdoL9xj4gqROBM+wQIh8t7+9hT5rnE8t
// SIG // +O8rAxE5uoi/AppQGcq5xrFU2ygfmIPn4YmkFtfhe8P6
// SIG // sJngKTg1NnFPmSCxlXOx/nfwk4PG3AG/QARZWiQwWeuC
// SIG // NxeAEJUalcyR1UENPe2U4PqLa0LAloR0XSVWmq8xggQN
// SIG // MIIECQIBATCBkzB8MQswCQYDVQQGEwJVUzETMBEGA1UE
// SIG // CBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEe
// SIG // MBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSYw
// SIG // JAYDVQQDEx1NaWNyb3NvZnQgVGltZS1TdGFtcCBQQ0Eg
// SIG // MjAxMAITMwAAAhvQsrgCZ/dyzwABAAACGzANBglghkgB
// SIG // ZQMEAgEFAKCCAUowGgYJKoZIhvcNAQkDMQ0GCyqGSIb3
// SIG // DQEJEAEEMC8GCSqGSIb3DQEJBDEiBCACnZFTiYL4Bjya
// SIG // 2Fe/hwtOAz0cTyZNodfBiNk+lGm90TCB+gYLKoZIhvcN
// SIG // AQkQAi8xgeowgecwgeQwgb0EIDAlFJW4PaOYxxAIVd0u
// SIG // 4kDAOlRU1nptzp18lTzdDYuAMIGYMIGApH4wfDELMAkG
// SIG // A1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAO
// SIG // BgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29m
// SIG // dCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0
// SIG // IFRpbWUtU3RhbXAgUENBIDIwMTACEzMAAAIb0LK4Amf3
// SIG // cs8AAQAAAhswIgQgtQ8ZRM8LRoVtj5uaTGudDwAI46k7
// SIG // EyCh6vNKD6NUFOgwDQYJKoZIhvcNAQELBQAEggIAiB5t
// SIG // vkHpf4dw1yGvaHANkXbsyGLtdsw9qYHlcmJbD8wEgSpm
// SIG // TWJs1341jTNW3N8PbzH92nXSGXMrKNuyAwrdmPmB2gfr
// SIG // /M0b26PN6FRvqEkJHzlyugREd05ChSUmRqo2ODGAIPNa
// SIG // 9Pkp2f9GNoq28mxJ4P6TzRV65w5ejC58mMrw2+HxCbXk
// SIG // zR0BICchk/3qbgK0iq6LHC2zjgp7Tj3c4CJhFmk8sa6f
// SIG // z7s6cnijhe7fsUKiXYTt6XTojQPqpIBoxnsiIbcwLGjX
// SIG // SOD4Mb4KVqvLCNdebMxqtFtmKC6M+ZhUL2lMvw9iUsX2
// SIG // N3v7AhhCEA7B5f/B5S3wDh2apNm8meBHde5rrxo0q9Wo
// SIG // 9dG0J/rXBChPiH3GgDjNVRG3y2uwKkOe4ED2iVpVkoM2
// SIG // FC7WhQSbNTrFdP3XMcoZGNMp8KkZLQD+OO0xBQNKpqBJ
// SIG // qwes69og0kCrTkIvRNGp1tx0faATvyq5jAAr9xfzIWEc
// SIG // WECLgcV6AGzWV1zWnwcFaNcWlyZnO99WpoFNCtSNOczI
// SIG // sABQUkBCjNvep+rX7Zpycu2jAqYOKlIkC9jdi3QeR2Bt
// SIG // IMDxrrvxhZ8DTgLhFos7ZkXY2cYBqvv6azuwApFbmekn
// SIG // en5vmBoptNNfLET0+X0fzAOJu1h/BzptH/jtbmE4DqfZ
// SIG // frOVA/kS8QVVnKTqDW8=
// SIG // End signature block
