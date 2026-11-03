// What the extension does with a request, with its storage and its line to the desktop app passed in so it can be tested without a
// browser. There are two data sources that work on their own (the fictional demo records, and the person's imported spreadsheet) and
// one that goes through the desktop app. The source is a visible setting and there is no silent fallback: if the chosen source cannot
// answer, the person is told, rather than being shown some other source's data.
import { normalizeSettings, DEFAULT_SETTINGS } from './core/settings.js';
import { BusinessCalendar } from './core/calendar.js';
import { buildCard, buildWatchList } from './core/card.js';
import { checkDraft } from './core/draftcheck.js';
import { openPortfolio, readTable, PortfolioError } from './core/portfolio.js';
import { extractNumber, findNumbers } from './core/recognizer.js';
import { firstStep } from './core/watch.js';
import { DEMO_RECORDS } from './core/demo-records.js';
import { parseIso } from './core/calendar.js';
import { NativeError } from './native.js';

const pad = (n) => String(n).padStart(2, '0');
const localIso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const MAX_STORED_BYTES = 8 * 1024 * 1024;

export function createService({ storage, native = null, now = () => new Date() }) {
  const todayIso = () => localIso(now());
  const demoRepo = () => ({ find: (n) => DEMO_RECORDS.find((r) => r.number === n) ?? null, records: () => DEMO_RECORDS, label: 'Fictional demo data', kind: 'demo' });
  let appSettingsCache = null;

  async function getSettings() { return normalizeSettings((await storage.get('settings')) || {}); }
  async function saveSettings(patch) {
    const next = normalizeSettings({ ...(await getSettings()), ...patch });
    await storage.set('settings', next);
    return next;
  }

  async function importedRepo() {
    const imp = await storage.get('imported');
    if (!imp) return null;
    const p = openPortfolio({ headers: imp.headers, rows: imp.rows }, { name: imp.name, overrides: imp.overrides, savedAt: imp.savedAt });
    return { find: (n) => p.find(n, todayIso()), records: () => p.records(todayIso()), label: `Your portfolio: ${imp.name}`, kind: 'imported', portfolio: p, imported: imp };
  }

  async function repo(settings) {
    if (settings.dataSource === 'imported') {
      const r = await importedRepo();
      if (!r) throw new NativeError('no-import', 'No spreadsheet has been imported yet. Choose one on the extension\'s Settings page.');
      if (!r.portfolio.status.ok) throw new NativeError('bad-import', r.portfolio.status.message);
      return r;
    }
    return demoRepo();
  }

  const useApp = (settings) => settings.dataSource === 'app';
  const needNative = () => { if (!native) throw new NativeError('not-permitted', 'The desktop app connection is turned off.'); return native; };

  /** The settings in force: when connected to the app and "follow the app" is on, the app's consent, urgent rule and holiday window win. */
  async function effectiveSettings() {
    const s = await getSettings();
    if (!useApp(s) || !s.followApp || !native) return s;
    try {
      if (!appSettingsCache || Date.now() - appSettingsCache.at > 30000) {
        const r = await native.request({ type: 'settings' });
        appSettingsCache = r && r.ok ? { at: Date.now(), settings: r.settings } : null;
      }
      return appSettingsCache ? normalizeSettings({ ...s, ...appSettingsCache.settings }) : s;
    } catch (e) { return s; }
  }

  async function ctxFor(settings) {
    return { today: parseIso(todayIso()), calendar: new BusinessCalendar(settings.extraClosures), lead: settings.holidayLeadDays, lateDays: settings.urgentLateDays };
  }

  const fail = (e) => ({ ok: false, error: e.code || 'error', message: e.message || 'Something went wrong.' });

  /** One answer for one number from an already-chosen source (null for the desktop app). */
  async function lookupWith(settings, rp, number, ctx) {
    let card;
    if (useApp(settings)) {
      const r = await needNative().request({ type: 'lookup', number });
      if (!r.ok) return { ok: false, error: r.error, message: r.message };
      card = r.card;
    } else {
      const rec = rp.find(number);
      if (!rec) return { ok: false, error: 'not-found', message: rp.kind === 'imported' ? `${number} is not in your portfolio (${rp.imported.name})` : `${number} is not a recognized ANDA` };
      card = buildCard(rec, ctx);
    }
    return { ok: true, card, step: firstStep(settings.consent, settings.escalateUrgent, card.urgency.isUrgent), settings: { consent: settings.consent, autoHideSeconds: settings.autoHideSeconds } };
  }

  /** @returns {ok:true, card, step} or {ok:false, error, message} */
  async function lookup(text) {
    const settings = await effectiveSettings();
    const number = extractNumber(text);
    if (number === null) return { ok: false, error: 'bad-number', message: 'That is not an ANDA number.' };
    try { return await lookupWith(settings, useApp(settings) ? null : await repo(settings), number, await ctxFor(settings)); } catch (e) { return fail(e); }
  }

  async function draft(text) {
    const settings = await effectiveSettings();
    try {
      if (useApp(settings)) {
        const r = await needNative().request({ type: 'draft', text });
        return r.ok ? { ok: true, result: { findings: r.findings, numbersChecked: r.numbersChecked, datesChecked: r.datesChecked, statusChecked: r.statusChecked } } : { ok: false, error: r.error, message: r.message };
      }
      const rp = await repo(settings);
      return { ok: true, result: checkDraft(text, (n) => rp.find(n)) };
    } catch (e) { return fail(e); }
  }

  async function watchlist() {
    const settings = await effectiveSettings();
    try {
      if (useApp(settings)) {
        const r = await needNative().request({ type: 'watchlist' });
        return r.ok ? { ok: true, list: { items: r.items, total: r.total, shown: r.shown } } : { ok: false, error: r.error, message: r.message };
      }
      const rp = await repo(settings);
      return { ok: true, list: buildWatchList(rp.records(), await ctxFor(settings)) };
    } catch (e) { return fail(e); }
  }

  /** For a page scan: a one-line summary per number found, from the chosen source. Numbers the source does not know are listed as such. */
  async function scan(numbers) {
    const settings = await effectiveSettings();
    const items = [];
    let rp = null, ctx = null;
    try { rp = useApp(settings) ? null : await repo(settings); ctx = await ctxFor(settings); } catch (e) { return fail(e); }   // the data is read once, not once per number
    for (const n of numbers.slice(0, 40)) {
      let r;
      try { r = await lookupWith(settings, rp, n, ctx); } catch (e) { return fail(e); }
      if (r.ok) items.push({ number: n, drug: r.card.drug, status: `Goal ${r.card.goalDate}${r.card.hasForecast ? ` · forecast ${r.card.forecastDate} (${r.card.forecastDelta.replace('▲', '').replace('▼', '').trim()})` : ''}`,
        topFlag: r.card.watch[0] ? r.card.watch[0].text : null, severity: r.card.watch[0] ? r.card.watch[0].severity : null });
      else if (r.error === 'not-found') items.push({ number: n, drug: 'Not in your data', status: 'Nothing to show for this number', topFlag: null, severity: null });
      else return { ok: false, error: r.error, message: r.message };
    }
    return { ok: true, items, settings: { autoHideSeconds: settings.autoHideSeconds } };
  }

  /** Reads a spreadsheet the person chose and keeps it in this browser profile only. */
  async function importFile(name, bytes, lastModifiedMs) {
    const table = await readTable(bytes, name);
    const d = new Date(lastModifiedMs || Date.now());
    const savedAt = { y: d.getFullYear(), m: d.getMonth() + 1, d: d.getDate(), hh: d.getHours(), mm: d.getMinutes() };
    const p = openPortfolio(table, { name, overrides: null, savedAt });
    const record = { name, savedAt, headers: table.headers, rows: table.rows, overrides: null };
    if (JSON.stringify(record).length > MAX_STORED_BYTES) throw new PortfolioError('That file is too large to keep in the browser. Use a smaller export, or connect the desktop app instead.');
    await storage.set('imported', record);
    return { status: p.status, headers: p.headers, mapping: p.mapping };
  }

  async function setOverrides(overrides) {
    const imp = await storage.get('imported');
    if (!imp) return null;
    imp.overrides = overrides;
    await storage.set('imported', imp);
    const p = openPortfolio({ headers: imp.headers, rows: imp.rows }, { name: imp.name, overrides, savedAt: imp.savedAt });
    return { status: p.status, headers: p.headers, mapping: p.mapping };
  }

  async function importedInfo() {
    const r = await importedRepo();
    return r ? { name: r.imported.name, status: r.portfolio.status, headers: r.portfolio.headers, mapping: r.portfolio.mapping, overrides: r.imported.overrides } : null;
  }

  async function clearImported() { await storage.remove('imported'); const s = await getSettings(); if (s.dataSource === 'imported') await saveSettings({ dataSource: 'demo' }); }

  /** What the Data page shows: which source is in use and whether it is reachable. */
  async function status() {
    const s = await getSettings();
    if (useApp(s)) {
      if (!native) return { kind: 'app', ok: false, label: 'Desktop app', message: 'The desktop app connection is turned off.' };
      try { const r = await native.request({ type: 'hello' }); return { kind: 'app', ok: !!r.ok, label: r.dataLabel, message: r.ok ? `Connected to ANDA Companion (Notula) ${r.version}. ${r.dataKind === 'portfolio' ? r.dataMessage : 'The app is using its fictional demo records.'}` : r.message, count: r.count }; }
      catch (e) { return { kind: 'app', ok: false, label: 'Desktop app', message: e.message }; }
    }
    if (s.dataSource === 'imported') {
      const r = await importedRepo();
      return r ? { kind: 'imported', ok: r.portfolio.status.ok, label: r.label, message: r.portfolio.status.message, count: r.portfolio.status.count } : { kind: 'imported', ok: false, label: 'Imported spreadsheet', message: 'No spreadsheet has been imported yet.' };
    }
    return { kind: 'demo', ok: true, label: 'Fictional demo data', message: 'The two fictional demo ANDAs. Import your own spreadsheet or connect the desktop app on the Settings page.', count: DEMO_RECORDS.length };
  }

  return { getSettings, saveSettings, effectiveSettings, lookup, draft, watchlist, scan, importFile, setOverrides, importedInfo, clearImported, status, DEFAULT_SETTINGS, findNumbers };
}
