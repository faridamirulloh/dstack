(() => {
  const initial = document.getElementById('mini-visualization-state');
  let state = JSON.parse(initial.textContent).widgetState;
  initial.remove();
  const post = message => parent.postMessage(message, '*');
  const pending = new Map();
  let request = 0;
  const publish = () => dispatchEvent(new CustomEvent('openai:set_globals', {
    detail: { globals: window.openai }
  }));
  window.openai = {
    theme: 'dark',
    visualizationTheme: 'dark',
    visualizationStyleVariables: {},
    widgetState: state,
    statePersistence: 'session',
    stateModelContext: 'none',
    async setWidgetState(next) {
      const value = typeof next === 'function' ? next(state) : next;
      const serialized = JSON.stringify(value);
      if (value == null || typeof value !== 'object' || Array.isArray(value)
          || new TextEncoder().encode(serialized).length > 16384) {
        throw new TypeError('Widget state must be an object of at most 16 KiB.');
      }
      state = JSON.parse(serialized);
      window.openai.widgetState = state;
      publish();
      const id = ++request;
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
          pending.delete(id);
          reject(new Error('Could not save preview state.'));
        }, 5000);
        pending.set(id, { resolve, reject, timeout });
        post({ type: 'widget-state-write', id, state: serialized });
      });
    },
    async sendFollowUpMessage({ prompt }) {
      if (navigator.userActivation?.isActive && typeof prompt === 'string') {
        post({ type: 'follow-up', prompt });
      }
    },
    openExternal({ href }) {
      if (navigator.userActivation?.isActive) post({ type: 'open-external', href });
    }
  };
  document.documentElement.dataset.theme = 'dark';
  addEventListener('message', event => {
    if (event.source !== parent || event.data?.type !== 'widget-state-result') return;
    const receipt = pending.get(event.data.id);
    if (!receipt) return;
    pending.delete(event.data.id);
    clearTimeout(receipt.timeout);
    if (event.data.ok) receipt.resolve();
    else receipt.reject(new Error('Could not save preview state.'));
  });
  let measuring = false;
  const measure = () => {
    if (measuring) return;
    measuring = true;
    requestAnimationFrame(() => {
      measuring = false;
      post({ type: 'height', height: Math.ceil(Math.max(document.body.scrollHeight, document.body.getBoundingClientRect().height)) });
    });
  };
  new ResizeObserver(measure).observe(document.body);
  addEventListener('load', measure);
  addEventListener('click', event => {
    if (event.defaultPrevented || !event.isTrusted) return;
    const link = event.target.closest?.('a[href]');
    if (!link || link.getAttribute('href').startsWith('#')) return;
    event.preventDefault();
    window.openai.openExternal({ href: link.href });
  });
  addEventListener('wheel', event => {
    if (event.defaultPrevented || event.ctrlKey || !event.deltaY) return;
    for (let element = event.target; element instanceof Element; element = element.parentElement) {
      if (!/(auto|scroll)/.test(getComputedStyle(element).overflowY)) continue;
      if (event.deltaY < 0 ? element.scrollTop > 0 : element.scrollTop + element.clientHeight < element.scrollHeight - 1) return;
    }
    const scroll = document.scrollingElement;
    if (event.deltaY < 0 ? scroll.scrollTop > 0 : scroll.scrollTop + scroll.clientHeight < scroll.scrollHeight - 1) return;
    event.preventDefault();
    post({ type: 'wheel', delta: event.deltaY });
  }, { passive: false });
  publish();
})();
