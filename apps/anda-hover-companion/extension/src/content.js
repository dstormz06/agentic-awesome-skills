// Runs in the page only when the person asks (a shortcut, the right-click menu or the toolbar button); there is no always-on script.
// It draws the card, invite and result panels inside a closed shadow root, so the page can neither restyle nor read them.
(function () {
  'use strict';
  if (globalThis.__andaCompanionLoaded) return;
  globalThis.__andaCompanionLoaded = true;
  const UI = globalThis.AndaUI;
  const { h } = UI;

  const host = document.createElement('div');
  host.setAttribute('data-anda-companion', '');
  for (const [k, v] of Object.entries({ all: 'initial', position: 'fixed', inset: '0', 'z-index': '2147483647', 'pointer-events': 'none' })) host.style.setProperty(k, v, 'important');
  const shadow = host.attachShadow({ mode: 'closed' });
  UI.mountStyles(shadow);
  const layer = h('div', { class: 'k', style: 'position:fixed;inset:0;pointer-events:none' });
  shadow.append(layer);
  document.documentElement.append(host);

  let current = null;   // {kind, el, number, ...}
  let hideTimer = null, idleSince = 0, hovering = false, previousFocus = null;
  let autoHideSeconds = 20;

  const clear = (restoreFocus = true) => {
    clearInterval(hideTimer); clearTimeout(hideTimer);
    if (current && current.el.isConnected) current.el.remove();
    const had = current;
    current = null; hovering = false;
    if (restoreFocus && had && had.focusable && previousFocus && previousFocus.isConnected) { try { previousFocus.focus({ preventScroll: true }); } catch (e) { /* page element gone */ } }
    previousFocus = null;
  };

  function place(el, anchor) {
    el.style.setProperty('position', 'absolute'); el.style.setProperty('pointer-events', 'auto');
    el.style.setProperty('visibility', 'hidden');
    layer.append(el);
    const r = el.getBoundingClientRect(), vw = document.documentElement.clientWidth, vh = document.documentElement.clientHeight, m = 12;
    let x, y;
    if (anchor) {
      x = anchor.x; y = anchor.y + anchor.h + 8;
      if (x + r.width > vw - m) x = vw - m - r.width;
      if (y + r.height > vh - m) y = anchor.y - r.height - 8;   // flip above the selection when there is no room below
    } else { x = vw - r.width - 16; y = 16; }
    x = Math.max(m, Math.min(x, Math.max(m, vw - r.width - m)));
    y = Math.max(m, Math.min(y, Math.max(m, vh - r.height - m)));
    el.style.setProperty('left', Math.round(x) + 'px'); el.style.setProperty('top', Math.round(y) + 'px');
    el.style.removeProperty('visibility');
  }

  const touch = () => { idleSince = Date.now(); };
  function armAutoHide(isExempt) {
    clearInterval(hideTimer);
    touch();
    if (!autoHideSeconds) return;
    hideTimer = setInterval(() => { if (!hovering && !isExempt() && (Date.now() - idleSince) / 1000 >= autoHideSeconds) clear(); }, 250);
  }
  layer.addEventListener('pointerenter', () => { hovering = true; touch(); }, true);
  layer.addEventListener('pointerleave', () => { hovering = false; touch(); }, true);
  layer.addEventListener('pointermove', touch, true);
  layer.addEventListener('keydown', (e) => {
    touch();
    if (!current) return;
    if (e.key === 'Escape') { e.stopPropagation(); clear(); return; }
    if (current.kind === 'card' && !current.mini && !(e.ctrlKey || e.metaKey || e.altKey)) {
      const k = e.key.toLowerCase();
      if (k === 'e') { e.preventDefault(); current.expanded = !current.expanded; draw(); }
      else if (k === 's') { e.preventDefault(); copy(current.model.statusLine, null); }
    }
  }, true);
  // a click outside the card closes a card or panel; a chip or notice just fades
  document.addEventListener('pointerdown', (e) => { if (current && current.kind !== 'notice' && e.target !== host && !host.contains(e.target)) clear(false); }, true);
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && current) clear(); }, true);

  async function copy(text, button) {
    let ok = false;
    try { await navigator.clipboard.writeText(text); ok = true; } catch (e) { /* not a secure page, or no permission */ }
    if (!ok) {
      const ta = document.createElement('textarea');
      ta.value = text;
      for (const [k, v] of Object.entries({ position: 'fixed', left: '-9999px', top: '0' })) ta.style.setProperty(k, v);
      document.documentElement.append(ta); ta.select();
      try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
      ta.remove();
    }
    if (button) { const was = button.textContent; button.textContent = ok ? 'Copied ✓' : 'Copy blocked here'; setTimeout(() => { button.textContent = was; }, 1400); }
  }

  const ask = (msg) => chrome.runtime.sendMessage(msg);

  function draw() {
    if (!current || current.kind !== 'card') return;
    const old = current.el, anchor = current.anchor;
    const el = UI.renderCard(current.model, {
      mini: current.mini, expanded: current.expanded, reason: current.reason,
      handlers: {
        onClose: () => clear(),
        onExpand: () => { current.expanded = !current.expanded; draw(); },
        onMore: () => { current.mini = false; draw(); el2focus(); },
        onCopy: (t, b) => copy(t, b),
        onSister: async (n) => { const r = await ask({ type: 'anda:card', number: n }); if (r && r.ok) { current.model = r.card; current.expanded = false; current.mini = false; draw(); } },
      },
    });
    old.replaceWith(el);
    current.el = el;
    place(el, current.expanded ? null : anchor);
    if (!current.mini) el2focus();
  }
  const el2focus = () => { if (current && current.el && !current.mini) current.el.focus({ preventScroll: true }); };

  function openCard(msg, mini, reason) {
    clear();
    previousFocus = document.activeElement;
    const placeholder = h('div');
    layer.append(placeholder);
    current = { kind: 'card', el: placeholder, model: msg.card, mini, expanded: false, reason, anchor: msg.anchor, focusable: !mini };
    draw();
    armAutoHide(() => current && current.expanded);
  }

  function showInvite(msg) {
    clear();
    const chip = UI.renderChip(`${msg.card.kind} ${msg.card.number}`, async () => {
      const a = current && current.anchor;
      const r = await ask({ type: 'anda:card', number: msg.card.number });
      if (r && r.ok) openCard({ card: r.card, anchor: a }, false, null);
    });
    current = { kind: 'chip', el: chip, anchor: msg.anchor };
    place(chip, msg.anchor);
    clearInterval(hideTimer);
    hideTimer = setTimeout(() => { if (current && current.kind === 'chip') clear(false); }, 3000);
  }

  function showNotice(text, anchor) {
    clear();
    const el = UI.renderNotice(text);
    current = { kind: 'notice', el };
    place(el, anchor || null);
    hideTimer = setTimeout(() => clear(false), Math.min(8, Math.max(2.5, 1.2 + text.length / 14)) * 1000);
  }

  function showPanel(msg) {
    clear();
    previousFocus = document.activeElement;
    let body;
    if (msg.kind === 'draft') {
      body = h('div', null, h('div', { class: 'slot who', text: msg.note || '' }), UI.renderFindings(msg.result));
    } else {
      const open = async (n) => { const r = await ask({ type: 'anda:card', number: n }); if (r && r.ok) openCard({ card: r.card, anchor: null }, false, null); };
      body = h('div', null, msg.note ? h('div', { class: 'slot who', text: msg.note }) : null, UI.renderList(msg.items, open, msg.empty || 'Nothing to show.'));
    }
    const el = UI.panel(msg.title, body, () => clear());
    current = { kind: 'panel', el, focusable: true };
    place(el, null);
    el.focus({ preventScroll: true });
    armAutoHide(() => true);   // a result panel stays until you close it
  }

  chrome.runtime.onMessage.addListener((msg) => {
    if (!msg || typeof msg.type !== 'string' || !msg.type.startsWith('anda:')) return;
    if (typeof msg.autoHideSeconds === 'number') autoHideSeconds = msg.autoHideSeconds;
    switch (msg.type) {
      case 'anda:show':
        if (msg.step === 'invite') showInvite(msg);
        else openCard(msg, msg.step === 'peek', msg.reason || null);
        break;
      case 'anda:notice': showNotice(msg.text, msg.anchor); break;
      case 'anda:panel': showPanel(msg); break;
      case 'anda:close': clear(); break;
      default: break;
    }
  });
})();
