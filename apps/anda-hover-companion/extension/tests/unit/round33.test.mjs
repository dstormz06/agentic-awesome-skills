// Round 33 in the extension: the card's data line and its age, the time zone, settings carried over from the earlier form, the moment a
// spreadsheet was saved, what is switched on, the first steps, and the timer that writes a line again as each minute begins.
import { test, mock } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { createService, savedMsOf } from '../../src/service.js';
import { normalizeSettings, DEFAULT_SETTINGS, SETTINGS_SCHEMA } from '../../src/core/settings.js';
import { connectionLine, CHANGES } from '../../src/core/about.js';
import { GUIDE_STEPS, guideStep, guideStart, guideOpensAtStart, SAMPLE_NUMBER } from '../../src/core/guide.js';
import { BusinessCalendar, parseIso } from '../../src/core/calendar.js';
import { evaluateWatch } from '../../src/core/watch.js';
import '../../src/ui/card-ui.js';

const here = dirname(fileURLToPath(import.meta.url));
const src = (f) => readFileSync(join(here, '..', '..', f), 'utf8');
const memory = (seed = {}) => { const m = new Map(Object.entries(seed)); return { m, get: async (k) => (m.has(k) ? structuredClone(m.get(k)) : undefined), set: async (k, v) => { m.set(k, structuredClone(v)); }, remove: async (k) => { m.delete(k); } }; };
const T = (s) => new TextEncoder().encode(s);
const CSV = 'ANDA,Product,Applicant,Goal date,Forecast date,Status\n930001,Alpha tabs,Birch,2027-10-04,2027-10-18,In review\n';
const NY = () => 'America/New_York';
const at = (y, m, d, hh = 0, mm = 0) => Date.UTC(y, m - 1, d, hh, mm);
const DAY = 86400000;

// ---------- settings ----------
test('settings stored before round 33 are carried over: every choice kept, the new ones at their usual setting, the form numbered', () => {
  const old = { consent: 'peek', escalateUrgent: false, urgentLateDays: 14, holidayLeadDays: 5, autoHideSeconds: 40, dataSource: 'imported', followApp: false, extraClosures: [{ date: '2026-12-24', name: 'Closing' }] };
  const now = normalizeSettings(old);
  assert.deepEqual(now, { ...old, schema: SETTINGS_SCHEMA, timeZone: '', staleAfterDays: 7 });
  assert.equal(SETTINGS_SCHEMA, 2);
  assert.deepEqual(normalizeSettings(now), now, 'carrying over twice changes nothing');
});

test('a setting that is not one of the choices goes back to its usual setting, and what is not a setting is dropped', () => {
  const s = normalizeSettings({ timeZone: 'Europe/Paris', staleAfterDays: 2, schema: 99, somethingElse: 'x', consent: 'loud' });
  assert.equal(s.timeZone, ''); assert.equal(s.staleAfterDays, 7); assert.equal(s.schema, SETTINGS_SCHEMA); assert.equal(s.consent, 'whisper');
  assert.equal('somethingElse' in s, false);
  for (const bad of [null, undefined, 'text', 7, []]) assert.deepEqual(normalizeSettings(bad), { ...DEFAULT_SETTINGS }, JSON.stringify(bad));
  assert.equal(normalizeSettings({ timeZone: 'America/Phoenix', staleAfterDays: 30 }).timeZone, 'America/Phoenix');
});

test('the service keeps the time settings and gives them back', async () => {
  const store = memory({ settings: { consent: 'full' } }), svc = createService({ storage: store, computer: NY });
  assert.equal((await svc.getSettings()).staleAfterDays, 7);
  await svc.saveSettings({ timeZone: 'America/Denver', staleAfterDays: 3 });
  assert.deepEqual([store.m.get('settings').schema, store.m.get('settings').timeZone, store.m.get('settings').staleAfterDays, store.m.get('settings').consent], [2, 'America/Denver', 3, 'full']);
  assert.equal(await svc.zone(), 'America/Denver');
});

// ---------- the moment a spreadsheet was saved ----------
test('a spreadsheet kept before round 33 (wall-clock parts) is still read, and one kept now holds one moment in UTC', async () => {
  const parts = { y: 2026, m: 11, d: 2, hh: 8, mm: 30 };
  assert.equal(savedMsOf({ savedAt: parts }), new Date(2026, 10, 2, 8, 30).getTime());
  assert.equal(savedMsOf({ savedMs: 123456789000, savedAt: parts }), 123456789000, 'the moment wins where both are kept');
  assert.ok(Number.isNaN(savedMsOf({})), 'nothing kept: not known');
  const store = memory(), svc = createService({ storage: store, now: () => new Date(at(2026, 11, 19, 17)), computer: NY });
  const saved = at(2026, 11, 18, 20, 0);
  const info = await svc.importFile('Tracker.csv', T(CSV), saved);
  assert.equal(store.m.get('imported').savedMs, saved); assert.equal('savedAt' in store.m.get('imported'), false);
  assert.match(info.status.message, /^Loaded 1 ANDA from Tracker\.csv \(file saved Nov 18, 2026, 3:00 PM EST\)\./);
  const legacy = memory({ imported: { name: 'Old.csv', savedAt: parts, headers: ['ANDA', 'Goal date'], rows: [['930009', '2027-01-05']], overrides: null }, settings: { dataSource: 'imported' } });
  const old = createService({ storage: legacy, now: () => new Date(at(2026, 11, 19, 17)), computer: NY });
  assert.match((await old.importedInfo()).status.message, /\(file saved Nov \d+, 2026, \d+:\d\d [AP]M E[SD]T\)/);
  assert.equal((await old.lookup('930009')).ok, true);
});

test('a file with no readable saved time is given the time it was read, never an invented one', async () => {
  const now = at(2026, 11, 19, 17), store = memory(), svc = createService({ storage: store, now: () => new Date(now), computer: NY });
  await svc.importFile('NoTime.csv', T(CSV), undefined);
  assert.equal(store.m.get('imported').savedMs, now);
});

// ---------- the card's line ----------
async function withFile(savedMs, nowBox, settings = {}) {
  const store = memory(), svc = createService({ storage: store, now: () => new Date(nowBox.ms), computer: NY });
  await svc.importFile('Tracker.csv', T(CSV), savedMs);
  await svc.saveSettings({ dataSource: 'imported', consent: 'full', ...settings });
  return { svc, store };
}

test('a card from the person\'s file says where the data is from, as of when with the zone, and how long ago', async () => {
  const box = { ms: at(2026, 11, 19, 17) }, { svc } = await withFile(at(2026, 11, 18, 20), box);
  const r = await svc.lookup('930001');
  assert.equal(r.card.freshness, 'Your portfolio file · Tracker.csv · as of Nov 18, 2026, 3:00 PM EST (21 hours ago)');
  assert.equal(r.card.stale, false);
  assert.equal(r.card.watch.some((f) => f.code === 'stale-data'), false);
});

test('the line ages by the minute, and is marked STALE exactly when it is older than the days chosen; the watch flag agrees with it', async () => {
  const saved = at(2026, 11, 10, 15), box = { ms: saved + 59000 }, { svc } = await withFile(saved, box);
  const line = async () => (await svc.fresh('930001'));
  assert.match((await line()).freshness, /\(just now\)$/);
  box.ms = saved + 60000; assert.match((await line()).freshness, /\(1 minute ago\)$/);
  box.ms = saved + 119 * 60000; assert.match((await line()).freshness, /\(1 hour ago\)$/);
  box.ms = saved + 7 * DAY; let f = await line();
  assert.equal(f.stale, false, 'exactly seven days old is not older than seven days'); assert.match(f.freshness, /\(7 days ago\)$/);
  assert.equal((await svc.lookup('930001')).card.watch.some((x) => x.code === 'stale-data'), false);
  box.ms = saved + 7 * DAY + 60000; f = await line();
  assert.equal(f.stale, true); assert.match(f.freshness, /\(7 days ago\) · STALE: older than 7 days$/);
  const flag = (await svc.lookup('930001')).card.watch.find((x) => x.code === 'stale-data');
  assert.equal(flag.text, 'This data is 7 days old (as of Nov 10).');
  await svc.saveSettings({ staleAfterDays: 14 });
  f = await line(); assert.equal(f.stale, false, 'the person chose more days: no longer STALE');
  assert.equal((await svc.lookup('930001')).card.watch.some((x) => x.code === 'stale-data'), false);
  await svc.saveSettings({ staleAfterDays: 1 });
  box.ms = saved + DAY + 60000; f = await line();
  assert.match(f.freshness, /\(1 day ago\) · STALE: older than 1 day$/);
  assert.equal((await svc.lookup('930001')).card.watch.find((x) => x.code === 'stale-data').text, 'This data is 1 day old (as of Nov 10).');
});

test('the morning daylight time begins: one minute apart is one minute apart, and the abbreviation changes', async () => {
  const before = at(2026, 3, 8, 6, 59), after = at(2026, 3, 8, 7, 0);      // 1:59 AM EST, then 3:00 AM EDT
  const box = { ms: after + 60000 };
  const a = await withFile(before, box), b = await withFile(after, box);
  assert.equal((await a.svc.fresh('930001')).freshness, 'Your portfolio file · Tracker.csv · as of Mar 8, 2026, 1:59 AM EST (2 minutes ago)');
  assert.equal((await b.svc.fresh('930001')).freshness, 'Your portfolio file · Tracker.csv · as of Mar 8, 2026, 3:00 AM EDT (1 minute ago)');
  const fall = at(2026, 11, 1, 5, 30), again = at(2026, 11, 1, 6, 30);        // 1:30 AM EDT, then 1:30 AM EST: the same wall clock, an hour apart
  box.ms = again + 60000;
  assert.match((await (await withFile(fall, box)).svc.fresh('930001')).freshness, /as of Nov 1, 2026, 1:30 AM EDT \(1 hour ago\)$/);
  assert.match((await (await withFile(again, box)).svc.fresh('930001')).freshness, /as of Nov 1, 2026, 1:30 AM EST \(1 minute ago\)$/);
});

test('choosing another zone changes how the moment is written and nothing that is stored', async () => {
  const saved = at(2026, 11, 18, 4, 30), box = { ms: saved + 3600000 }, { svc, store } = await withFile(saved, box);      // 11:30 PM EST on Nov 17
  assert.match((await svc.fresh('930001')).freshness, /as of Nov 17, 2026, 11:30 PM EST \(1 hour ago\)$/);
  await svc.saveSettings({ timeZone: 'Pacific/Honolulu' });
  assert.match((await svc.fresh('930001')).freshness, /as of Nov 17, 2026, 6:30 PM HST \(1 hour ago\)$/);
  await svc.saveSettings({ timeZone: 'UTC' });
  assert.match((await svc.fresh('930001')).freshness, /as of Nov 18, 2026, 4:30 AM UTC \(1 hour ago\)$/);
  assert.match((await svc.importedInfo()).status.message, /\(file saved Nov 18, 2026, 4:30 AM UTC\)/);
  assert.equal(store.m.get('imported').savedMs, saved, 'the stored moment is the same');
});

test('the fictional records say they are sample data that does not change, and are never called stale', async () => {
  const svc = createService({ storage: memory(), now: () => new Date(at(2031, 1, 1)), computer: NY });
  const r = await svc.lookup('216482');
  assert.equal(r.card.freshness, 'Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (sample data, which does not change)');
  assert.equal(r.card.stale, false); assert.equal(r.card.watch.some((f) => f.code === 'stale-data'), false);
});

test('with the desktop app as the source the app\'s own line is shown, and STALE is read from it', async () => {
  const line = { v: 'Your portfolio file · P.xlsx · as of Nov 1, 2026, 9:00 AM EST (18 days ago) · STALE: older than 7 days' };
  const native = { request: async (b) => (b.type === 'lookup' ? { ok: true, card: { number: '930001', kind: 'ANDA', drug: 'From the app', freshness: line.v, urgency: { isUrgent: false }, watch: [], hasForecast: false } } : { ok: false, error: 'x', message: 'no' }) };
  const svc = createService({ storage: memory({ settings: { dataSource: 'app', followApp: false } }), native, computer: NY });
  let f = await svc.fresh('930001');
  assert.deepEqual(f, { ok: true, number: '930001', freshness: line.v, stale: true });
  line.v = 'Your portfolio file · P.xlsx · as of Nov 19, 2026, 9:00 AM EST (just now)';
  f = await svc.fresh('930001'); assert.equal(f.stale, false);
  assert.equal((await svc.fresh('nonsense')).ok, false);
});

test('the watch rule on its own: without a moment it counts days as before; with one it counts to the minute in the zone in use', () => {
  const cal = new BusinessCalendar(), today = parseIso('2026-11-19');
  const r = { demo: false, asOfDate: '2026-11-10', goalDateValue: '2027-10-04', forecastDate: '—', sourceLabel: 'S' };
  const text = (rec, s) => (evaluateWatch(rec, today, cal, s).find((f) => f.code === 'stale-data') || {}).text;
  assert.equal(text(r, { staleDays: 7 }), 'This data is 9 days old (as of Nov 10).');
  assert.equal(text(r, { staleDays: 14 }), undefined);
  const m = { ...r, asOfUtc: '2026-11-11T03:30:00.000Z' };      // 10:30 PM EST on Nov 10
  assert.equal(text(m, { staleDays: 7, nowMs: at(2026, 11, 18, 3, 30), zone: 'America/New_York' }), undefined, 'exactly seven days');
  assert.equal(text(m, { staleDays: 7, nowMs: at(2026, 11, 18, 3, 31), zone: 'America/New_York' }), 'This data is 7 days old (as of Nov 10).');
  assert.equal(text(m, { staleDays: 7, nowMs: at(2026, 11, 18, 3, 31), zone: 'UTC' }), 'This data is 7 days old (as of Nov 11).', 'the day is the day in the zone in use');
});

// ---------- the sample card of the guide ----------
test('the guide\'s sample card is always the fictional ANDA: never the person\'s file, never through the desktop app', async () => {
  const calls = [], native = { request: async (b) => { calls.push(b.type); return { ok: false, error: 'x', message: 'no' }; } };
  const box = { ms: at(2026, 11, 19, 17) }, store = memory(), svc = createService({ storage: store, native, now: () => new Date(box.ms), computer: NY });
  await svc.importFile('Mine.csv', T('ANDA,Product\n216482,MY OWN PRODUCT\n'), box.ms);
  for (const dataSource of ['demo', 'imported', 'app']) {
    await svc.saveSettings({ dataSource });
    const r = await svc.sample();
    assert.equal(r.ok, true, dataSource); assert.equal(r.card.number, SAMPLE_NUMBER); assert.equal(r.card.demo, true, dataSource);
    assert.equal(r.card.drug, 'Metformin HCl ER Tablets, 500 mg', dataSource); assert.match(r.card.freshness, /sample data, which does not change/);
  }
  assert.deepEqual(calls, [], 'the desktop app was not asked');
  assert.equal((await svc.getSettings()).dataSource, 'app', 'and the chosen source was left as it was');
});

// ---------- first steps ----------
test('the guide is shown once: marked when it is drawn, and it says so when the browser would not keep the mark', async () => {
  const store = memory(), svc = createService({ storage: store, now: () => new Date(at(2026, 11, 19, 17)) });
  assert.deepEqual(await svc.firstSteps(false), { seen: false, kept: true });
  assert.equal(store.m.has('firstSteps'), false, 'asking does not mark');
  assert.deepEqual(await svc.firstSteps(true), { seen: false, kept: true });
  assert.deepEqual(store.m.get('firstSteps'), { seen: true, atMs: at(2026, 11, 19, 17) });
  assert.deepEqual(await svc.firstSteps(true), { seen: true, kept: true });
  const refusing = { get: async () => undefined, set: async () => { throw new Error('quota'); }, remove: async () => {} };
  assert.deepEqual(await createService({ storage: refusing }).firstSteps(true), { seen: false, kept: false });
  const forgetful = { get: async () => undefined, set: async () => {}, remove: async () => {} };
  assert.deepEqual(await createService({ storage: forgetful }).firstSteps(true), { seen: false, kept: false });
  const broken = { get: async () => { throw new Error('x'); }, set: async () => {}, remove: async () => {} };
  assert.equal((await createService({ storage: broken }).firstSteps(false)).seen, false);
  assert.equal(guideOpensAtStart(false), true); assert.equal(guideOpensAtStart(undefined), true); assert.equal(guideOpensAtStart(true), false); assert.equal(guideOpensAtStart('true'), true);
});

test('five steps at most, each in plain sentences, and a place that stays inside the guide', () => {
  assert.equal(GUIDE_STEPS.length, 5);
  for (const s of GUIDE_STEPS) { assert.ok(s.lines.length >= 2 && s.lines.length <= 3, s.title); for (const l of s.lines) assert.ok(l.length <= 200 && /[.}]$/.test(l), l); }
  assert.deepEqual(GUIDE_STEPS.map((s) => s.title), ['What this is', 'Try it on the sample number', 'Your own data', 'What is switched on', 'Where things are']);
  const first = guideStep(0, 'press Alt+Shift+A', 'Offline · Local only'), last = guideStep(4, 'x', 'y');
  assert.deepEqual([first.place, first.back, first.next], ['Step 1 of 5', false, 'Next']);
  assert.deepEqual([last.place, last.back, last.next], ['Step 5 of 5', true, 'Done']);
  for (const [i, want] of [[-3, 0], [99, 4], [NaN, 0], [undefined, 0], ['2', 0], [1.9, 1], [Infinity, 0]]) assert.equal(guideStep(i, '', '').at, want, String(i));
  for (let i = 0; i < 5; i++) for (const l of guideStep(i, 'press K', 'ON').lines) assert.equal(/\{(start|connections)\}/.test(l), false, l);
});

test('the two places are filled in one pass: a filled-in text is never read again for a place', () => {
  assert.match(guideStep(1, '{connections}', 'SECRET').lines[0], /then \{connections\}\. Or press/);
  assert.match(guideStep(3, 'x', '{start} $& $1').lines[0], /At the moment: \{start\} \$& \$1$/);
  assert.equal(guideStart('Alt+Shift+A'), 'press Alt+Shift+A');
  for (const none of ['', '   ', null, undefined, 7]) assert.match(guideStart(none), /^right-click it and choose “ANDA Companion \(Notula\): peek at…” \(no shortcut is set at the moment\)$/);
  assert.match(guideStep(1, guideStart(''), '').lines[0], /select the number 216482, then right-click it and choose/);
});

test('what the guide says is true of the code it describes', () => {
  const content = src('src/content.js'), bg = src('src/background.js'), options = src('src/options.html'), popup = src('src/popup.html'), ui = src('src/ui/card-ui.js'), manifest = JSON.parse(src('manifest.json'));
  const all = GUIDE_STEPS.flatMap((s) => s.lines).join(' ');
  // "E shows more, S copies the status line and Esc closes the card"
  assert.match(content, /k === 'e'/); assert.match(content, /k === 's'/); assert.match(content, /e\.key === 'Escape'/);
  // "marked FICTIONAL DEMO"
  assert.match(ui, /'FICTIONAL DEMO'/);
  // "makes no network requests, and it reads a page only when you ask it to"
  assert.equal(manifest.host_permissions, undefined); assert.equal(manifest.content_scripts, undefined);
  for (const f of ['src/content.js', 'src/background.js', 'src/service.js', 'src/options.js', 'src/popup.js', 'src/native.js', 'src/ui/card-ui.js', 'src/core/time.js', 'src/core/guide.js', 'src/core/about.js'])
    assert.equal(/\bfetch\s*\(|XMLHttpRequest|WebSocket|sendBeacon|EventSource/.test(src(f)), false, `${f} makes no network request`);
  // the places it names exist under those names
  for (const name of ['My spreadsheet', 'Ways to start it', 'Help: first steps']) assert.ok(options.includes(name), name);
  assert.ok(popup.includes('Help: first steps')); for (const tab of ['Look up', 'Draft', 'Watch list', 'This page']) assert.ok(popup.includes(`>${tab}<`), tab);
  assert.ok(bg.includes('ANDA Companion (Notula): peek at “%s”'));
  assert.ok(all.includes('216482 and 214990'));
  // "kept in this browser profile on this PC": local storage only, never the synced kind
  for (const f of ['src/background.js', 'src/options.js']) { assert.match(src(f), /chrome\.storage\.local/); assert.equal(/chrome\.storage\.sync/.test(src(f)), false); }
  // the product is named in full wherever it is named
  for (const l of GUIDE_STEPS.flatMap((s) => s.lines)) for (const m of l.matchAll(/ANDA Companion/g)) assert.equal(l.slice(m.index, m.index + 23), 'ANDA Companion (Notula)', l);
});

test('the guide opens after an installation only, and the page that draws it marks it', () => {
  const bg = src('src/background.js'), options = src('src/options.js');
  assert.match(bg, /details\.reason === 'install'/);
  assert.match(bg, /service\.firstSteps\(false\)\.then\(\(f\) => \{ if \(!f\.seen\) return chrome\.tabs\.create\(\{ url: chrome\.runtime\.getURL\('src\/options\.html#first'\) \}\); \}\)/);
  assert.match(options, /const f = await service\.firstSteps\(true\);\s+if \(location\.hash === '#first' \|\| guideOpensAtStart\(f\.seen\)\)/);
  assert.match(options, /if \(!f\.kept\) \{ \$\('fsNote'\)\.hidden = false; \$\('fsNote'\)\.textContent = GUIDE_NOT_REMEMBERED; \}/);
});

// ---------- what is switched on ----------
test('what is switched on is said in one line: local only, linked by name with what is sent and kept, or chosen and not answering', async () => {
  assert.equal(connectionLine('demo', null).short, 'Offline · Local only'); assert.equal(connectionLine('imported', null).state, 'local');
  const linked = connectionLine('app', { ok: true });
  assert.equal(linked.state, 'linked'); assert.match(linked.full, /ANDA Companion \(Notula\), the desktop app on this PC\. Sent to it: .* Where it goes: .* Kept by the extension: nothing\./);
  assert.equal(connectionLine('app', { ok: false }).state, 'silent'); assert.equal(connectionLine('app', null).state, 'silent');
  const up = { request: async (b) => (b.type === 'hello' ? { ok: true, version: '1', dataKind: 'demo', dataLabel: 'L', count: 2 } : { ok: false }) };
  const svc = createService({ storage: memory(), native: up });
  assert.equal((await svc.connection()).state, 'local');
  await svc.saveSettings({ dataSource: 'app' }); assert.equal((await svc.connection()).state, 'linked');
  const off = createService({ storage: memory({ settings: { dataSource: 'app' } }), native: null });
  assert.equal((await off.connection()).short, 'Desktop app chosen, not answering · nothing is sent');
});

test('the version in the list of changes is the one in the manifest', () => {
  const manifest = JSON.parse(src('manifest.json'));
  assert.equal(CHANGES[0].version, manifest.version);
  assert.ok(CHANGES.every((c) => /^\d+\.\d+\.\d+$/.test(c.version) && c.items.length > 0));
});

// ---------- the timer and the line on an open card ----------
test('the line is written again as each minute begins, and the timer stops when the card closes', () => {
  const UI = globalThis.AndaUI;
  mock.timers.enable({ apis: ['setTimeout', 'Date'], now: at(2026, 11, 19, 17, 0) + 42000 });      // 42 seconds into a minute
  try {
    let n = 0; const stop = UI.everyMinute(() => { n++; });
    mock.timers.tick(17000); assert.equal(n, 0, 'not before the minute ends');
    mock.timers.tick(1100); assert.equal(n, 1, 'just after the minute begins');
    mock.timers.tick(59000); assert.equal(n, 1); mock.timers.tick(1100); assert.equal(n, 2, 'and again a minute later');
    stop(); const before = n; mock.timers.tick(180000); assert.equal(n, before, 'stopped');
    let threw = 0; const stop2 = UI.everyMinute(() => { threw++; throw new Error('x'); });
    const tick = (ms) => { try { mock.timers.tick(ms); } catch (e) { assert.equal(e.message, 'x'); } };
    tick(61000); tick(60000); tick(60000);
    assert.equal(threw, 3, 'a write that fails does not end the timer');
    stop2(); tick(180000); assert.equal(threw, 3);
  } finally { mock.timers.reset(); }
});

test('writing the line again changes only the line', () => {
  const UI = globalThis.AndaUI;
  const el = { textContent: 'old', classes: new Set(), classList: { toggle(c, on) { on ? el.classes.add(c) : el.classes.delete(c); } } };
  const card = { querySelector: (q) => (q === '.fresh' ? el : null) };
  assert.equal(UI.setFresh(card, 'new · STALE: older than 7 days', true), true); assert.equal(el.textContent, 'new · STALE: older than 7 days'); assert.ok(el.classes.has('stale'));
  assert.equal(UI.setFresh(card, 'fresh', false), true); assert.equal(el.classes.has('stale'), false);
  assert.equal(UI.setFresh(card, '', false), false); assert.equal(el.textContent, 'fresh', 'an empty answer leaves the line as it was');
  assert.equal(UI.setFresh({ querySelector: () => null }, 'x', false), false); assert.equal(UI.setFresh(null, 'x', false), false);
  assert.match(readFileSync(join(here, '..', '..', 'src', 'content.js'), 'utf8'), /stopMinute = UI\.everyMinute\(refreshLine\)/);
  assert.match(readFileSync(join(here, '..', '..', 'src', 'content.js'), 'utf8'), /if \(stopMinute\) \{ stopMinute\(\); stopMinute = null; \}/);
});
