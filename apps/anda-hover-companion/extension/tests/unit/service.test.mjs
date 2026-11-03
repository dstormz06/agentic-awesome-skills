// The service layer on its own (storage and the line to the app are passed in): which source answers, what it says when it cannot, and
// that nothing quietly answers from the wrong source.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createService } from '../../src/service.js';
import { NativeError } from '../../src/native.js';

const memory = () => { const m = new Map(); const gets = []; return { m, gets, get: async (k) => { gets.push(k); return m.has(k) ? structuredClone(m.get(k)) : undefined; }, set: async (k, v) => { m.set(k, structuredClone(v)); }, remove: async (k) => { m.delete(k); } }; };
const NOW = () => new Date(2026, 10, 3, 9, 0, 0);
const T = (s) => new TextEncoder().encode(s);
const CSV = 'ANDA,Product,Applicant,Goal date,Forecast date,Status\n930001,Alpha tabs,Birch,2026-10-03,2026-10-17,In review\n930002,Beta tabs,Birch,2031-03-04,2031-03-03,Waiting\n';

test('demo records answer by default, and whisper decides what opens first', async () => {
  const svc = createService({ storage: memory(), now: NOW });
  const risk = await svc.lookup('ANDA 216482'), calm = await svc.lookup('214990');
  assert.equal(risk.ok, true); assert.equal(risk.step, 'peek'); assert.equal(risk.card.demo, true);
  assert.equal(calm.step, 'invite');
  assert.equal((await svc.lookup('999999')).error, 'not-found');
  assert.equal((await svc.lookup('NDA 021234')).error, 'bad-number');
  await svc.saveSettings({ consent: 'full' });
  assert.equal((await svc.lookup('214990')).step, 'full');
});

test('with an imported sheet as the source, demo numbers are not found and the sheet answers', async () => {
  const store = memory(), svc = createService({ storage: store, now: NOW });
  await svc.importFile('Tracker.csv', T(CSV), new Date(2026, 10, 2, 8, 30).getTime());
  await svc.saveSettings({ dataSource: 'imported' });
  const r = await svc.lookup('930001');
  assert.equal(r.ok, true); assert.equal(r.card.source, 'Your portfolio: Tracker.csv'); assert.equal(r.card.drug, 'Alpha tabs');
  assert.equal(r.step, 'peek');                                                                  // its goal date has passed: urgent
  const demo = await svc.lookup('216482');
  assert.equal(demo.ok, false); assert.match(demo.message, /216482 is not in your portfolio \(Tracker\.csv\)/);
});

test('an imported source that is missing or unreadable says so; it never falls back to the demo records', async () => {
  const svc = createService({ storage: memory(), now: NOW });
  await svc.saveSettings({ dataSource: 'imported' });
  const none = await svc.lookup('216482');
  assert.equal(none.ok, false); assert.equal(none.error, 'no-import');
  assert.equal((await svc.draft('ANDA 216482 is on track.')).ok, false);
  assert.equal((await svc.watchlist()).ok, false);
  assert.equal((await svc.scan(['216482'])).ok, false);
});

test('scanning a page reads the imported data once, not once per number', async () => {
  const store = memory(), svc = createService({ storage: store, now: NOW });
  await svc.importFile('Tracker.csv', T(CSV), Date.now());
  await svc.saveSettings({ dataSource: 'imported' });
  store.gets.length = 0;
  const r = await svc.scan(['930001', '930002', '111111', '222222', '333333', '930001', '930002', '444444']);
  assert.equal(r.ok, true); assert.equal(r.items.length, 8);
  assert.equal(store.gets.filter((k) => k === 'imported').length, 1);
  assert.equal(r.items.find((i) => i.number === '111111').drug, 'Not in your data');
});

test('the app source asks the app, follows its settings when asked to, and has no silent fallback', async () => {
  const calls = [];
  const native = { request: async (b) => { calls.push(b.type); if (b.type === 'settings') return { ok: true, settings: { consent: 'full', escalateUrgent: false, urgentLateDays: 14, holidayLeadDays: 0, autoHideSeconds: 40 } };
    if (b.type === 'lookup') return { ok: true, card: { number: '930001', kind: 'ANDA', drug: 'From the app', urgency: { isUrgent: true, reason: 'x' }, watch: [], hasForecast: false } };
    return { ok: false, error: 'unknown-type', message: 'no' }; } };
  const svc = createService({ storage: memory(), native, now: NOW });
  await svc.saveSettings({ dataSource: 'app' });
  const r = await svc.lookup('930001');
  assert.equal(r.card.drug, 'From the app'); assert.equal(r.step, 'full'); assert.equal(r.settings.autoHideSeconds, 40);   // the app's consent wins
  await svc.saveSettings({ followApp: false });
  assert.equal((await svc.lookup('930001')).step, 'peek');                                                                 // the extension's own: whisper + urgent
  const down = createService({ storage: memory(), native: { request: async () => { throw new NativeError('not-installed', 'The desktop app connection is not set up.'); } }, now: NOW });
  await down.saveSettings({ dataSource: 'app' });
  const bad = await down.lookup('216482');
  assert.equal(bad.ok, false); assert.equal(bad.error, 'not-installed');                                                  // and the demo records were NOT used instead
  assert.equal((await down.draft('x 216482')).ok, false); assert.equal((await down.watchlist()).ok, false);
  const off = createService({ storage: memory(), native: null, now: NOW });
  await off.saveSettings({ dataSource: 'app' });
  assert.equal((await off.lookup('216482')).error, 'not-permitted');
  assert.equal((await off.status()).ok, false);
});

test('a spreadsheet too big to keep is refused, and a refused file leaves the earlier one in place', async () => {
  const store = memory(), svc = createService({ storage: store, now: NOW });
  await svc.importFile('Keep.csv', T(CSV), Date.now());
  const long = 'x'.repeat(600);
  const huge = 'ANDA,Notes\n' + Array.from({ length: 19000 }, (_, i) => `${100000 + i},${long}`).join('\n');
  await assert.rejects(svc.importFile('Huge.csv', T(huge), Date.now()), /too large to keep/);
  assert.equal((await svc.importedInfo()).name, 'Keep.csv');
  await assert.rejects(svc.importFile('Bad.docx', T('x'), Date.now()), /not supported/);
  assert.equal((await svc.importedInfo()).name, 'Keep.csv');
});

test('a large portfolio builds its cards and its watch list quickly (no quadratic work)', async () => {
  const rows = ['ANDA,Product,Applicant,Goal date,Forecast date,Status'];
  for (let i = 0; i < 5000; i++) rows.push(`${100000 + i},Drug ${i % 50} tabs,Company ${i % 10},2026-12-${String(1 + (i % 28)).padStart(2, '0')},2027-01-05,In review`);
  const svc = createService({ storage: memory(), now: NOW });
  const t0 = performance.now();
  await svc.importFile('Big.csv', T(rows.join('\n')), Date.now());
  await svc.saveSettings({ dataSource: 'imported' });
  const w = await svc.watchlist();
  const ms = performance.now() - t0;
  assert.equal(w.ok, true); assert.equal(w.list.total, 5000);
  assert.ok(ms < 4000, `took ${ms | 0} ms`);
});
