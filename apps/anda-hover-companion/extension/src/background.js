// The extension's service worker. It does nothing until the person acts: a shortcut, the right-click menu or the toolbar button.
// Even then it only touches the one tab they are in (the "activeTab" permission), and only to read their selection and draw the card.
import { createService } from './service.js';
import { NativeBridge } from './native.js';
import { extractNumber, findNumbers } from './core/recognizer.js';
import { pageSelection, pageDraft, pageText } from './page-readers.js';

const storage = {
  get: async (k) => (await chrome.storage.local.get(k))[k],
  set: (k, v) => chrome.storage.local.set({ [k]: v }),
  remove: (k) => chrome.storage.local.remove(k),
};
const native = new NativeBridge(chrome);
const service = createService({ storage, native });
const MENU = { peek: 'anda-peek', draft: 'anda-draft', scan: 'anda-scan' };

// ---------- the right-click menu ----------
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: MENU.peek, title: 'ANDA Companion (Notula): peek at “%s”', contexts: ['selection'] });
    chrome.contextMenus.create({ id: MENU.draft, title: 'ANDA Companion (Notula): check this text as a draft', contexts: ['selection', 'editable'] });
    chrome.contextMenus.create({ id: MENU.scan, title: 'ANDA Companion (Notula): scan this page for ANDA numbers', contexts: ['page'] });
  });
});

// ---------- reading the page (only the tab the person is in) ----------
async function run(tabId, func, allFrames = false) {
  const results = await chrome.scripting.executeScript({ target: { tabId, allFrames }, func });
  return results.map((r) => r.result).filter(Boolean);
}
async function ensureContent(tabId) {
  await chrome.scripting.executeScript({ target: { tabId, frameIds: [0] }, files: ['src/ui/card-ui.js', 'src/content.js'] });
}
const send = (tabId, msg) => chrome.tabs.sendMessage(tabId, msg, { frameId: 0 });

async function cannotRunHere(tab) {
  await chrome.action.setBadgeBackgroundColor({ color: '#8a5800', tabId: tab.id });
  await chrome.action.setBadgeText({ text: '!', tabId: tab.id });
  await chrome.action.setTitle({ title: 'ANDA Companion (Notula) cannot run on this page. Please use the toolbar button to look a number up.', tabId: tab.id });
  setTimeout(() => { chrome.action.setBadgeText({ text: '', tabId: tab.id }).catch(() => {}); chrome.action.setTitle({ title: 'ANDA Companion (Notula)', tabId: tab.id }).catch(() => {}); }, 6000);
}

async function withPage(tab, job) {
  if (!tab || tab.id === undefined) return;
  try { await ensureContent(tab.id); } catch (e) { await cannotRunHere(tab); return; }
  await job(tab.id);
}

// ---------- the three things a person can ask for ----------
async function peek(tab, menuText) {
  await withPage(tab, async (tabId) => {
    let found = [];
    try { found = await run(tabId, pageSelection, true); } catch (e) { /* frames that cannot be read are skipped */ }
    const sel = found.find((f) => f.text && f.text.trim()) || null;
    const text = (menuText && menuText.trim()) || (sel && sel.text) || '';
    const anchor = sel && sel.top ? sel.rect : null;   // a selection inside a frame is not placed near itself; the card goes to the corner
    const settings = await service.effectiveSettings();
    if (!text.trim()) return send(tabId, { type: 'anda:notice', text: 'Please select an ANDA number first, then use the shortcut or right-click.', autoHideSeconds: settings.autoHideSeconds });
    if (extractNumber(text) === null)
      return send(tabId, { type: 'anda:notice', text: 'No ANDA number in the selection (a number labelled NDA, BLA, IND, DMF or MF is not treated as an ANDA). Please use the toolbar button to type one.', anchor, autoHideSeconds: settings.autoHideSeconds });
    const r = await service.lookup(text);
    if (!r.ok) return send(tabId, { type: 'anda:notice', text: r.message, anchor, autoHideSeconds: settings.autoHideSeconds });
    return send(tabId, { type: 'anda:show', step: r.step, card: r.card, anchor, reason: r.step === 'peek' && r.card.urgency.isUrgent ? r.card.urgency.reason : null, autoHideSeconds: r.settings.autoHideSeconds });
  });
}

async function checkDraft(tab, menuText) {
  await withPage(tab, async (tabId) => {
    const settings = await service.effectiveSettings();
    let drafts = [];
    try { drafts = await run(tabId, pageDraft, true); } catch (e) { /* skipped frames */ }
    const d = drafts.find((x) => x.text && x.text.trim()) || { text: '', from: 'selection' };
    const text = (menuText && menuText.trim()) || d.text;
    if (!text.trim()) return send(tabId, { type: 'anda:notice', text: 'Please select some text, or click into the message you are writing, then try again.', autoHideSeconds: settings.autoHideSeconds });
    const r = await service.draft(text);
    if (!r.ok) return send(tabId, { type: 'anda:notice', text: r.message, autoHideSeconds: settings.autoHideSeconds });
    return send(tabId, { type: 'anda:panel', kind: 'draft', title: 'Check a draft', result: r.result, note: `Checked ${text.length.toLocaleString()} characters from ${menuText ? 'your selection' : d.from}. Nothing was sent anywhere.`, autoHideSeconds: settings.autoHideSeconds });
  });
}

async function scanPage(tab) {
  await withPage(tab, async (tabId) => {
    const settings = await service.effectiveSettings();
    let texts = [];
    try { texts = await run(tabId, pageText, true); } catch (e) { /* skipped frames */ }
    const numbers = findNumbers(texts.map((t) => t.text).join('\n'), 200);
    if (numbers.length === 0) return send(tabId, { type: 'anda:notice', text: 'No ANDA numbers found on this page.', autoHideSeconds: settings.autoHideSeconds });
    const r = await service.scan(numbers);
    if (!r.ok) return send(tabId, { type: 'anda:notice', text: r.message, autoHideSeconds: settings.autoHideSeconds });
    return send(tabId, { type: 'anda:panel', kind: 'scan', title: `ANDA numbers on this page · ${numbers.length}`, items: r.items, note: numbers.length > 40 ? 'Showing the first 40. Read from this page only; nothing was sent anywhere.' : 'Read from this page only; nothing was sent anywhere.', empty: 'Nothing to show.', autoHideSeconds: r.settings.autoHideSeconds });
  });
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === MENU.peek) peek(tab, info.selectionText);
  else if (info.menuItemId === MENU.draft) checkDraft(tab, info.selectionText);
  else if (info.menuItemId === MENU.scan) scanPage(tab);
});

chrome.commands.onCommand.addListener(async (command, tab) => {
  tab = tab || (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
  if (command === 'peek-selection') peek(tab);
  else if (command === 'check-draft') checkDraft(tab);
  else if (command === 'scan-page') scanPage(tab);
});

// Reachable only from this worker's own console (and the automated tests): a page cannot see a service worker's global scope.
globalThis.andaTest = { peek, checkDraft, scanPage, service };

// ---------- messages from the page script, the popup and the settings page ----------
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg.type !== 'string') return false;
  const own = sender.id === chrome.runtime.id;
  if (!own) return false;
  const answer = (p) => { p.then(sendResponse, (e) => sendResponse({ ok: false, error: 'error', message: e && e.message })); return true; };
  switch (msg.type) {
    case 'anda:card': return answer(service.lookup(String(msg.number || '')).then((r) => (r.ok ? { ok: true, card: r.card } : r)));
    case 'svc:lookup': return answer(service.lookup(String(msg.text || '')));
    case 'svc:draft': return answer(service.draft(String(msg.text || '').slice(0, 20000)));
    case 'svc:watchlist': return answer(service.watchlist());
    case 'svc:scan': return answer(service.scan((Array.isArray(msg.numbers) ? msg.numbers : []).map(String).slice(0, 200)));
    case 'svc:status': return answer(service.status());
    case 'svc:settings': return answer(msg.patch ? service.saveSettings(msg.patch) : service.getSettings());
    case 'svc:disconnect-native': native.close(); sendResponse({ ok: true }); return false;
    default: return false;
  }
});
