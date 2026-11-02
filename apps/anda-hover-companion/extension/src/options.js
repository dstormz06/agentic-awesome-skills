import { createService } from './service.js';
import { NativeBridge } from './native.js';
import { FIELDS } from './core/portfolio.js';

const $ = (id) => document.getElementById(id);
const storage = {
  get: async (k) => (await chrome.storage.local.get(k))[k],
  set: (k, v) => chrome.storage.local.set({ [k]: v }),
  remove: (k) => chrome.storage.local.remove(k),
};
const native = new NativeBridge(chrome);
const service = createService({ storage, native });

async function paintStatus() {
  const s = await service.status();
  const el = $('sourceStatus');
  el.className = 'status ' + (s.ok ? 'ok' : 'bad');
  el.textContent = `${s.label}. ${s.message}`;
}

async function paintSettings() {
  const s = await service.getSettings();
  document.querySelector(`input[name=source][value=${s.dataSource}]`).checked = true;
  document.querySelector(`input[name=consent][value=${s.consent}]`).checked = true;
  $('escalate').checked = s.escalateUrgent; $('late').value = String(s.urgentLateDays); $('lead').value = String(s.holidayLeadDays); $('hide').value = String(s.autoHideSeconds);
  $('follow').checked = s.followApp;
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
$('shortcuts').addEventListener('keydown', (e) => { if (e.key === 'Enter') shortcuts(); });

await paintSettings(); await paintImport(); await paintStatus();
