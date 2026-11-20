import { createService } from './service.js';
import { NativeBridge } from './native.js';
import { FIELDS } from './core/portfolio.js';
import { TZ_CHOICES, STALE_CHOICES, timeLabel, zoneSaid, computerZone } from './core/time.js';
import { CHANGES } from './core/about.js';
import { guideStep, guideStart, guideOpensAtStart, GUIDE_NOT_REMEMBERED } from './core/guide.js';

const UI = globalThis.AndaUI;

const $ = (id) => document.getElementById(id);
const storage = {
  get: async (k) => (await chrome.storage.local.get(k))[k],
  set: (k, v) => chrome.storage.local.set({ [k]: v }),
  remove: (k) => chrome.storage.local.remove(k),
};
const native = new NativeBridge(chrome);
const service = createService({ storage, native });

// What is switched on, said at the top of the page and again in step 4 of the guide.
let connection = { state: 'local', short: 'Offline · Local only', full: 'Offline · Local only.' };
async function paintConnection() {
  connection = await service.connection();
  $('conn').className = 'conn ' + connection.state;
  if ($('conn').textContent !== connection.full) $('conn').textContent = connection.full;      // written only when it changes, so it is not announced on every unrelated change
  if (!$('first').hidden) paintGuide(false);
}

async function paintStatus() {
  const s = await service.status();
  const el = $('sourceStatus');
  el.className = 'status ' + (s.ok ? 'ok' : 'bad');
  el.textContent = `${s.label}. ${s.message}`;
  await paintConnection();
}

// ---------- time ----------
for (const [id, name] of TZ_CHOICES) $('tz').append(new Option(name, id));
for (const d of STALE_CHOICES) $('stale').append(new Option(String(d), String(d)));
async function paintNow() { $('timeNow').textContent = `Now: ${timeLabel(Date.now(), await service.zone(), false)}`; }
UI.everyMinute(paintNow);

async function paintSettings() {
  const s = await service.getSettings();
  document.querySelector(`input[name=source][value=${s.dataSource}]`).checked = true;
  document.querySelector(`input[name=consent][value=${s.consent}]`).checked = true;
  $('escalate').checked = s.escalateUrgent; $('late').value = String(s.urgentLateDays); $('lead').value = String(s.holidayLeadDays); $('hide').value = String(s.autoHideSeconds);
  $('follow').checked = s.followApp;
  $('tz').value = s.timeZone; $('stale').value = String(s.staleAfterDays);
  await paintNow();
  $('closures').value = s.extraClosures.map((c) => `${c.date} ${c.name}`).join('\n');
  $('extId').textContent = chrome.runtime.id;
}

async function paintImport() {
  const info = await service.importedInfo();
  $('clearImport').hidden = !info; $('map').hidden = !info; $('importStatus').hidden = !info;
  if (!info) return;
  $('importStatus').className = 'status ' + (info.status.ok ? 'ok' : 'bad');
  $('importStatus').textContent = info.status.message;
  const table = $('map');
  table.replaceChildren();
  for (const [key, label] of FIELDS) {
    const sel = document.createElement('select');
    sel.setAttribute('aria-label', `Column for ${label}`);
    sel.append(new Option('(automatic)', ''));
    for (const h of info.headers) sel.append(new Option(h, h));
    sel.value = info.mapping[key] ?? '';
    sel.addEventListener('change', async () => {
      const overrides = { ...(info.overrides || {}) };
      for (const [k] of FIELDS) { const cur = table.querySelector(`select[data-key=${k}]`); overrides[k] = cur.value; }
      const r = await service.setOverrides(overrides);
      await paintImport(); await paintStatus();
      if (r && !r.status.ok) $('importStatus').textContent = r.status.message;
    });
    sel.dataset.key = key;
    const tr = document.createElement('tr'), a = document.createElement('td'), b = document.createElement('td');
    a.textContent = label; b.append(sel); tr.append(a, b); table.append(tr);
  }
}

const save = async (patch) => { await service.saveSettings(patch); await paintStatus(); };
for (const r of document.querySelectorAll('input[name=source]')) r.addEventListener('change', () => save({ dataSource: r.value }));
for (const r of document.querySelectorAll('input[name=consent]')) r.addEventListener('change', () => save({ consent: r.value }));
$('escalate').addEventListener('change', (e) => save({ escalateUrgent: e.target.checked }));
$('late').addEventListener('change', (e) => save({ urgentLateDays: +e.target.value }));
$('lead').addEventListener('change', (e) => save({ holidayLeadDays: +e.target.value }));
$('hide').addEventListener('change', (e) => save({ autoHideSeconds: +e.target.value }));
$('follow').addEventListener('change', (e) => save({ followApp: e.target.checked }));
$('tz').addEventListener('change', async (e) => {
  await save({ timeZone: e.target.value });
  await paintNow(); await paintImport();      // the time the spreadsheet was saved is written in the new zone
  $('timeHint').textContent = zoneSaid(e.target.value, computerZone(), Date.now());
});
$('stale').addEventListener('change', (e) => save({ staleAfterDays: +e.target.value }));
$('closures').addEventListener('change', (e) => {
  const closures = e.target.value.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => ({ date: l.slice(0, 10), name: l.slice(10).trim() || 'Closure' }));
  save({ extraClosures: closures });
});
$('testSource').addEventListener('click', paintStatus);

$('file').addEventListener('change', async (e) => {
  const f = e.target.files[0];
  if (!f) return;
  const box = $('importStatus');
  box.hidden = false; box.className = 'status'; box.textContent = 'Reading the file…';
  try {
    await service.importFile(f.name, new Uint8Array(await f.arrayBuffer()), f.lastModified);
    await service.saveSettings({ dataSource: 'imported' });
    await paintSettings(); await paintImport(); await paintStatus();
  } catch (err) {
    box.className = 'status bad'; box.textContent = err && err.message ? err.message : 'That file could not be read.';
  }
  e.target.value = '';
});
$('clearImport').addEventListener('click', async () => { await service.clearImported(); await paintSettings(); await paintImport(); await paintStatus(); });

$('connect').addEventListener('click', async () => {
  const granted = await chrome.permissions.request({ permissions: ['nativeMessaging'] });
  if (granted) await service.saveSettings({ dataSource: 'app' });
  await paintSettings(); await paintStatus();
  if (!granted) { $('sourceStatus').className = 'status bad'; $('sourceStatus').textContent = 'The permission was not granted, so the desktop app is not connected.'; }
});
const shortcuts = () => chrome.tabs.create({ url: 'chrome://extensions/shortcuts' });
$('shortcuts').addEventListener('click', shortcuts);

// The ways to start it: each command with the key Edge really gave it, or a plain "not set".
async function paintCommands() {
  const body = $('cmds').querySelector('tbody');
  body.replaceChildren();
  const rows = [['Right-click menu', 'on, for selected text', true], ['Toolbar button', 'on', true]];
  let commands = [];
  try { commands = (await chrome.commands.getAll()).filter((c) => !c.name.startsWith('_')); } catch (e) { commands = []; }
  for (const c of commands) rows.push([c.description || c.name, c.shortcut || 'not set (taken by Edge or another extension)', !!c.shortcut]);
  for (const [name, how, ok] of rows) {
    const tr = document.createElement('tr');
    const a = document.createElement('td'); a.textContent = name;
    const b = document.createElement('td'); b.textContent = (ok ? '✓ ' : '✗ ') + how;
    tr.append(a, b);
    body.append(tr);
  }
}
paintCommands();

// ---------- this version ----------
$('version').textContent = `Version ${chrome.runtime.getManifest().version}`;
(() => {
  const box = $('changes'); box.className = 'changes';
  for (const c of CHANGES) {
    const head = document.createElement('h3'); head.textContent = `Version ${c.version} · ${c.title}${c.version === chrome.runtime.getManifest().version ? ' (installed)' : ''}`;
    const list = document.createElement('ul');
    for (const item of c.items) { const li = document.createElement('li'); li.textContent = item; list.append(li); }
    box.append(head, list);
  }
})();

// ---------- first steps ----------
let guideAt = 0, peekKey = '';
async function readPeekKey() { try { peekKey = ((await chrome.commands.getAll()).find((c) => c.name === 'peek-selection') || {}).shortcut || ''; } catch (e) { peekKey = ''; } }
function paintGuide(say = true) {
  const v = guideStep(guideAt, guideStart(peekKey), connection.short);
  guideAt = v.at;
  $('fsPlace').textContent = v.place; $('fsTitle').textContent = v.title;
  $('fsLines').replaceChildren(...v.lines.map((t) => { const p = document.createElement('p'); p.className = 'line'; p.textContent = t; return p; }));
  $('fsAct').hidden = !v.action; $('fsAct').textContent = v.action ? v.action.label : ''; $('fsAct').dataset.act = v.action ? v.action.id : '';
  if (!v.action || v.action.id !== 'sample') { sampleCard = null; drawSample(); }
  $('fsBack').disabled = !v.back; $('fsNext').textContent = v.next;
  if (say) $('fsSay').textContent = `${v.place}: ${v.title}`;
}
async function openGuide(at = null) {
  await readPeekKey();
  if (at !== null || $('first').hidden) guideAt = at ?? 0;      // "Help" on an open guide keeps its place
  $('first').hidden = false; $('help').setAttribute('aria-expanded', 'true');
  paintGuide();
  $('fsTitle').focus();
}
function closeGuide() {
  $('first').hidden = true; $('help').setAttribute('aria-expanded', 'false'); sampleCard = null; drawSample();
  $('help').focus();
}
function go(to) { guideAt = to; paintGuide(); $('fsTitle').focus(); }
$('help').addEventListener('click', () => openGuide());
$('fsBack').addEventListener('click', () => go(guideAt - 1));
$('fsNext').addEventListener('click', () => { if (guideStep(guideAt).next === 'Done') closeGuide(); else go(guideAt + 1); });
$('fsLeave').addEventListener('click', closeGuide);
$('first').addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.preventDefault(); closeGuide(); } });
// The sample card is drawn in its own shadow root, so the card's styles and this page's styles cannot change each other.
let sampleCard = null, sampleWide = false;
const sampleRoot = $('fsCard').attachShadow({ mode: 'open' }), sampleLayer = UI.h('div', { class: 'k' });
UI.mountStyles(sampleRoot); sampleRoot.append(sampleLayer);
sampleLayer.addEventListener('keydown', (e) => {
  if (!sampleCard || e.ctrlKey || e.metaKey || e.altKey) return;
  const k = e.key.toLowerCase();
  if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); sampleCard = null; drawSample(); $('fsAct').focus(); $('fsSay').textContent = 'The sample card is closed. The guide is still open.'; }
  else if (k === 'e') { e.preventDefault(); sampleWide = !sampleWide; drawSample(); const el = sampleLayer.querySelector('.card'); if (el) el.focus(); }
  else if (k === 's') { e.preventDefault(); const b = sampleLayer.querySelector('[data-act="copy"]'); if (b) b.click(); }
});
function drawSample() {
  const host = sampleLayer;
  host.replaceChildren(); $('fsCard').hidden = !sampleCard;
  if (!sampleCard) return;
  host.append(UI.renderCard(sampleCard, { expanded: sampleWide, handlers: {
    onClose: () => { sampleCard = null; drawSample(); $('fsAct').focus(); },
    onExpand: () => { sampleWide = !sampleWide; drawSample(); },
    onCopy: async (t, b) => { try { await navigator.clipboard.writeText(t); b.textContent = 'Copied ✓'; } catch (e) { b.textContent = 'Copy blocked'; } setTimeout(() => { b.textContent = 'Copy status'; }, 1400); },
    onSister: async () => { /* the sample card stays on the sample ANDA */ },
  } }));
}
$('fsAct').addEventListener('click', async () => {
  if ($('fsAct').dataset.act === 'file') { $('file').scrollIntoView({ block: 'center' }); $('file').focus(); $('fsSay').textContent = 'Moved to “My spreadsheet”.'; return; }
  const r = await service.sample();
  if (!r.ok) { $('fsSay').textContent = r.message; return; }
  sampleCard = { ...r.card, sisters: [] }; sampleWide = false; drawSample();
  const el = sampleLayer.querySelector('.card'); if (el) el.focus();
  $('fsSay').textContent = 'The sample card is shown below. It is fictional sample data.';
});

await paintSettings(); await paintImport(); await paintStatus();

// Shown once: when the page is opened after installing, or the first time it is opened at all. Marked as shown here, where it is drawn.
{
  const f = await service.firstSteps(true);
  if (location.hash === '#first' || guideOpensAtStart(f.seen)) {
    await openGuide(0);
    if (!f.kept) { $('fsNote').hidden = false; $('fsNote').textContent = GUIDE_NOT_REMEMBERED; }
  }
}
