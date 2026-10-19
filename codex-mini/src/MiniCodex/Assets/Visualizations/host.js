(() => {
  const frame = document.getElementById('mini-visualization');
  const host = window.chrome.webview;
  addEventListener('message', event => {
    if (event.source !== frame.contentWindow || event.data == null || typeof event.data !== 'object') return;
    const data = event.data;
    switch (data.type) {
      case 'height':
        if (Number.isFinite(data.height) && data.height >= 0 && data.height <= 100000) {
          host.postMessage({ type: 'height', height: data.height });
        }
        break;
      case 'widget-state-write':
        if (Number.isSafeInteger(data.id) && data.id > 0 && typeof data.state === 'string' && data.state.length <= 16384) {
          host.postMessage({ type: 'widget-state-write', id: data.id, state: data.state });
        }
        break;
      case 'follow-up':
        if (navigator.userActivation?.isActive && typeof data.prompt === 'string' && data.prompt.trim() && data.prompt.length <= 16000) {
          host.postMessage({ type: 'follow-up', prompt: data.prompt });
        }
        break;
      case 'open-external':
        if (navigator.userActivation?.isActive && typeof data.href === 'string' && data.href.length <= 4096) {
          try {
            const url = new URL(data.href);
            if (url.protocol === 'https:' && !url.username && !url.password) {
              host.postMessage({ type: 'open-external', href: url.href });
            }
          } catch { /* Ignore unsupported links. */ }
        }
        break;
      case 'wheel':
        if (Number.isFinite(data.delta) && data.delta !== 0) host.postMessage({ type: 'wheel', delta: data.delta });
        break;
    }
  });
  host.addEventListener('message', event => {
    if (event.data?.type === 'widget-state-result') frame.contentWindow?.postMessage(event.data, '*');
  });
  frame.srcdoc = frame.dataset.srcdoc;
  delete frame.dataset.srcdoc;
})();
