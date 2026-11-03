import { extractNumber, findNumbers } from './core/recognizer.js';
import { pageDraft, pageText } from './page-readers.js';

const UI = globalThis.AndaUI;
const $ = (id) => document.getElementById(id);
const ask = (msg) => chrome.runtime.sendMessage(msg);
UI.mountStyles(document);

// ---------- tabs ----------
const TABS = ['look', 'draft', 'watch', 'page'];
function select(name, focusTab) {
  for (const t of TABS) {
    $('t-' + t).setAttribute('aria-selected', String(t === name));
    $('t-' + t).tabIndex = t === name ? 0 : -1; // one tab stop; arrow keys move between tabs
    $('p-' + t).hidden = t !== name;
  }
  if (focusTab) $('t-' + name).focus();
  else if (name === 'look') $('num').focus();
  if (name === 'watch') loadWatch();
}
for (const t of TABS) {
  $('t-' + t).addEventListener('click', () => select(t));
  $('t-' + t).addEventListener('keydown', (e) => {
    const at = TABS.indexOf(t);
    const to = e.key === 'ArrowRight' ? (at + 1) % TABS.length : e.key === 'ArrowLeft' ? (at + TABS.length - 1) % TABS.length
      : e.key === 'Home' ? 0 : e.key === 'End' ? TABS.length - 1 : -1;
    if (to < 0) return;
    e.preventDefault();
    select(TABS[to], true);
  });
}
for (const t of TABS.slice(1)) $('t-' + t).tabIndex = -1;

// ---------- look up ----------
let card = null, expanded = false;
function drawCard() {
  const host = $('cardHost');
  host.replaceChildren();
  if (!card) return;
  host.append(UI.renderCard(card, { expanded, handlers: {
    onClose: () => { card = null; drawCard(); },
    onExpand: () => { expanded = !expanded; drawCard(); },
    onCopy: async (t, b) => { try { await navigator.clipboard.writeText(t); b.textContent = 'Copied ✓'; } catch (e) { b.textContent = 'Copy blocked'; } setTimeout(() => { b.textContent = 'Copy status'; }, 1400); },
    onSister: (n) => open(n),
  } }));
}
async function open(text) {
  $('lookMsg').hidden = true;
  const r = await ask({ type: 'svc:lookup', text });
  if (!r || !r.ok) { card = null; drawCard(); $('lookMsg').hidden = false; $('lookMsg').className = 'status bad'; $('lookMsg').textContent = r ? r.message : 'No answer from the extension.'; return; }
  card = r.card; expanded = false; $('num').value = r.card.number; select('look'); drawCard();
}
$('lookForm').addEventListener('submit', (e) => { e.preventDefault(); open($('num').value); });

// ---------- draft ----------
async function activeTab() { return (await chrome.tabs.query({ active: true, currentWindow: true }))[0]; }
$('draftPage').addEventListener('click', async () => {
  try {
    const tab = await activeTab();
    const res = await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, func: pageDraft });
    const d = res.map((r) => r.result).find((x) => x && x.text && x.text.trim());
    if (d) { $('draftText').value = d.text; runDraft(); } else $('draftOut').replaceChildren(UI.renderFindings({ findings: [{ kind: 'note', text: 'There is no selection or message on this page to read.' }] }));
  } catch (e) { $('draftOut').replaceChildren(UI.renderFindings({ findings: [{ kind: 'note', text: 'This page cannot be read by the extension.' }] })); }
});
async function runDraft() {
  const r = await ask({ type: 'svc:draft', text: $('draftText').value });
  $('draftOut').replaceChildren(r && r.ok ? UI.renderFindings(r.result) : UI.renderFindings({ findings: [{ kind: 'note', text: r ? r.message : 'No answer.' }] }));
}
$('draftGo').addEventListener('click', runDraft);

// ---------- watch list ----------
let watch = null;
function drawWatch() {
  const out = $('watchOut');
  out.replaceChildren();
  if (!watch) return;
  const withNotes = $('watchNotes').checked;
  const items = watch.items.filter((i) => withNotes || i.severity !== 'info');
  $('watchNote').textContent = `${watch.shown} of ${watch.total} ANDAs have something worth a look${withNotes ? '' : '; notes hidden'}.`;
  out.append(UI.renderList(items, (n) => open(n), 'Nothing needs a second look in your data.'));
}
async function loadWatch() {
  $('watchNote').textContent = 'Reading your data…';
  const r = await ask({ type: 'svc:watchlist' });
  if (!r || !r.ok) { watch = null; $('watchOut').replaceChildren(); $('watchNote').textContent = r ? r.message : 'No answer.'; return; }
  watch = r.list; drawWatch();
}
$('watchGo').addEventListener('click', loadWatch);
$('watchNotes').addEventListener('change', drawWatch);

// ---------- scan this page ----------
$('scanGo').addEventListener('click', async () => {
  const msg = $('scanMsg'), out = $('scanOut');
  out.replaceChildren(); msg.hidden = false; msg.className = 'status'; msg.textContent = 'Reading this page…';
  try {
    const tab = await activeTab();
    const res = await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, func: pageText });
    const numbers = findNumbers(res.map((r) => (r.result ? r.result.text : '')).join('\n'), 200);
    if (numbers.length === 0) { msg.textContent = 'No ANDA numbers found on this page.'; return; }
    const r = await ask({ type: 'svc:scan', numbers });
    if (!r || !r.ok) { msg.className = 'status bad'; msg.textContent = r ? r.message : 'No answer.'; return; }
    msg.textContent = `${numbers.length} ANDA number${numbers.length === 1 ? '' : 's'} on this page${numbers.length > 40 ? ' (showing the first 40)' : ''}.`;
    out.append(UI.renderList(r.items, (n) => open(n), 'Nothing to show.'));
  } catch (e) { msg.className = 'status bad'; msg.textContent = 'This page cannot be read by the extension (browser and store pages are off limits).'; }
});

// ---------- footer ----------
(async () => { const s = await ask({ type: 'svc:status' }); $('source').textContent = s ? `Source: ${s.label}${s.ok ? '' : ' (not reachable)'}` : ''; })();
$('openOptions').addEventListener('click', () => chrome.runtime.openOptionsPage());
select('look');
