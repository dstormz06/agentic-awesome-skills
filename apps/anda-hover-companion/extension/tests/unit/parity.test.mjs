// The extension's JavaScript must say exactly what the app says. golden.json is written by the app's own code (see
// app/AndaCompanion.Tests/ExtensionGolden.cs); every case in it is reproduced here.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { extractNumber } from '../../src/core/recognizer.js';
import { BusinessCalendar, holidaysOf, parseIso, toIso, DAY } from '../../src/core/calendar.js';
import { buildCard, buildWatchList } from '../../src/core/card.js';
import { checkDraft } from '../../src/core/draftcheck.js';
import { parseCsv, readXlsx, openPortfolio } from '../../src/core/portfolio.js';
import { DEMO_RECORDS } from '../../src/core/demo-records.js';

const here = dirname(fileURLToPath(import.meta.url));
const golden = JSON.parse(readFileSync(join(here, '..', 'golden.json'), 'utf8'));
const ctxOf = (c, cal = new BusinessCalendar()) => ({ today: parseIso(c.today), calendar: cal, lead: c.lead, lateDays: c.lateDays });
const flagJson = (f) => ({ date: toIso(f.t), lead: f.lead, kind: f.kind, name: f.name, near: f.near, before: f.before, prev: toIso(f.prev), next: toIso(f.next) });

test('the recognizer returns what the app returns for every golden text', () => {
  for (const { text, number } of golden.recognizer) assert.equal(extractNumber(text), number, JSON.stringify(text));
});

test('the demo records are exactly the app\'s fictional records', () => {
  assert.deepEqual(DEMO_RECORDS, golden.demoRecords);
});

test('the federal calendar: every flag, extra closures, every day of 2026-2027 and the holiday lists', () => {
  const cal = new BusinessCalendar();
  for (const g of golden.calendar.flags) {
    const f = cal.evaluate(parseIso(g.date), g.lead);
    assert.deepEqual({ date: g.date, lead: g.lead, kind: f.kind, name: f.name, near: f.near, before: f.before, prev: toIso(f.prev), next: toIso(f.next) }, g, `${g.date} lead ${g.lead}`);
  }
  const extra = new BusinessCalendar([golden.calendar.extraClosure]);
  for (const g of golden.calendar.extraFlags) {
    const f = extra.evaluate(parseIso(g.date), g.lead);
    assert.deepEqual({ date: g.date, lead: g.lead, kind: f.kind, name: f.name, near: f.near, before: f.before, prev: toIso(f.prev), next: toIso(f.next) }, g, `extra ${g.date}`);
  }
  let kinds = '';
  for (let t = parseIso('2026-01-01'); t <= parseIso('2027-12-31'); t += DAY) kinds += { weekend: 'W', holiday: 'H', business: 'B' }[cal.kindOf(t)];
  assert.equal(kinds, golden.calendar.kinds2026to2027);
  for (const { year, items } of golden.calendar.holidays)
    assert.deepEqual(holidaysOf(year).map((h) => ({ observed: toIso(h.observed), actual: toIso(h.actual), name: h.name, regional: h.regional })), items, String(year));
});

test('every card (demo and synthetic records under three clocks) is identical to the app\'s', () => {
  for (const c of golden.cards) assert.deepEqual(buildCard(c.record, ctxOf(c.ctx)), c.expected, `${c.label} @ ${c.ctx.today} lead ${c.ctx.lead} late ${c.ctx.lateDays}`);
});

test('the watch list over the demo records is identical to the app\'s', () => {
  const w = golden.watchlistDemo;
  assert.deepEqual(buildWatchList(w.records, ctxOf(w.ctx)), w.expected);
});

test('the draft check says the same thing, finding by finding', () => {
  for (const d of golden.drafts) assert.deepEqual(checkDraft(d.text, (n) => golden.demoRecords.find((r) => r.number === n) ?? null), { findings: d.findings, numbersChecked: d.numbersChecked, datesChecked: d.datesChecked, statusChecked: d.statusChecked }, JSON.stringify(d.text));
});

test('portfolio files: CSV and the xlsx read to the same status, columns, cards and watch list', async () => {
  for (const p of golden.portfolios) {
    let table;
    try {
      table = p.source === 'xlsx'
        ? await readXlsx(new Uint8Array(readFileSync(join(here, '..', p.xlsxFile))))
        : parseCsv(p.text, p.name);
    } catch (e) { assert.fail(`${p.name}: ${e.message}`); }
    const repo = openPortfolio(table, { name: p.name, overrides: p.overrides, savedAt: p.savedAt });
    const e = p.expected;
    assert.deepEqual({ ok: repo.status.ok, count: repo.status.count, message: repo.status.message }, { ok: e.ok, count: e.count, message: e.message }, `${p.name} status`);
    assert.deepEqual(repo.headers, e.headers, `${p.name} headers`);
    assert.deepEqual(repo.mapping, e.mapping, `${p.name} mapping`);
    assert.deepEqual(repo.numbers(), e.numbers, `${p.name} numbers`);
    const ctx = ctxOf({ today: p.today, lead: 3, lateDays: 7 });
    const cards = [...repo.numbers()].sort().map((n) => buildCard(repo.find(n, p.today), ctx));
    assert.deepEqual(cards, e.cards, `${p.name} cards`);
    assert.deepEqual(buildWatchList(repo.records(p.today), ctx), e.watchlist, `${p.name} watch list`);
  }
});
