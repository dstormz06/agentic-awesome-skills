// The extension writes a time exactly as the simulation and the Windows app do. The shared answers in mockup/round31-golden.json and
// mockup/round32-golden.json are written by the simulation; every one of them is reproduced here, and the rules themselves are compared
// with the simulation's text so that the copy cannot drift.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import * as T from '../../src/core/time.js';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..', '..');
const g31 = JSON.parse(readFileSync(join(root, 'mockup', 'round31-golden.json'), 'utf8'));
const g32 = JSON.parse(readFileSync(join(root, 'mockup', 'round32-golden.json'), 'utf8'));

test('the rules are the simulation\'s own text, copied without change', () => {
  const sim = readFileSync(join(root, 'mockup', 'windows-simulation.html'), 'utf8');
  const a = sim.indexOf('/* The zones offered. "" is the computer\'s own zone. */'), b = sim.indexOf('/* ---------------- the clock (round 21)');
  assert.ok(a > 0 && b > a, 'the block is still where it was in the simulation');
  const mine = readFileSync(join(here, '..', '..', 'src', 'core', 'time.js'), 'utf8');
  const copied = mine.slice(mine.indexOf('/* The zones offered.'), mine.indexOf('/* The name of this computer\'s own zone'));
  assert.equal(copied.replace(/^export /gm, '').trim(), sim.slice(a, b).trim());
});

test('the choices and the abbreviations are the shared ones', () => {
  assert.deepEqual(T.TZ_CHOICES, g31.zoneChoices);
  assert.deepEqual(T.TZ_ABBR, g31.abbreviations);
  assert.deepEqual(T.STALE_CHOICES, g31.staleChoices); assert.equal(T.STALE_DEFAULT, g31.staleDefault);
  assert.deepEqual(T.REF_STALE_CHOICES, g32.refChoices); assert.equal(T.REF_STALE_DEFAULT, g32.refDefault);
});

test('506 moments in 22 zones are written as the simulation writes them, across both daylight-time changes', () => {
  assert.equal(g31.labels.length, 506);
  for (const c of g31.labels) {
    const at = `${c.zone} @ ${c.ms}`;
    assert.equal(T.zoneClock(c.ms, c.zone).offset, c.offset, at);
    assert.equal(T.zoneAbbr(c.ms, c.zone), c.abbr, at);
    assert.equal(T.timeLabel(c.ms, c.zone, false), c.label, at);
    assert.equal(T.timeLabel(c.ms, c.zone, true), c.withSeconds, at);
    assert.equal(T.timeOfDay(c.ms, c.zone, false), c.time, at);
    assert.equal(T.timeOfDay(c.ms, c.zone, true), c.timeWithSeconds, at);
  }
});

test('a moment that is not known is said to be not known, and the ends of what can be written are the shared ones', () => {
  const odd = [NaN, Infinity, -Infinity, T.TIME_MIN - 1, T.TIME_MAX + 1];
  assert.equal(g31.unknown.length, odd.length);
  odd.forEach((ms, i) => { assert.equal(T.timeLabel(ms, 'America/New_York', false), g31.unknown[i]); assert.equal(T.relTime(ms, 0), 'time not known'); });
  const now = Date.UTC(2026, 10, 19, 17, 0, 0);
  for (const c of g31.bounds) {
    assert.equal(T.timeKnown(c.ms), c.known, String(c.ms));
    assert.equal(T.timeLabel(c.ms, 'America/New_York', true), c.label, String(c.ms));
    assert.equal(T.timeOfDay(c.ms, 'UTC', false), c.time, String(c.ms));
  }
  assert.equal(T.relTime(now, now), 'just now');
});

test('how long ago: 24 ages, never rounded up', () => {
  for (const c of g31.relative) assert.equal(T.relTime(c.then, c.now), c.text, `${c.now - c.then} ms`);
});

test('88 data lines: source, as of, how long ago, and STALE past the days chosen', () => {
  for (const c of g31.fresh) assert.deepEqual(T.freshLine(c.source, c.asOf, c.now, c.days, c.zone), c.result, JSON.stringify(c).slice(0, 120));
});

test('what Settings says after a zone is chosen: 12 sentences', () => {
  for (const c of g31.said) assert.equal(T.zoneSaid(c.chosen, c.computer, c.ms), c.text, JSON.stringify(c.chosen));
});

test('98 reference files: when saved, how long ago, STALE after their own days', () => {
  assert.equal(g32.ref.length, 98);
  for (const c of g32.ref) assert.deepEqual(T.refSaved(c.saved, c.now, c.days, c.zone), c.result, JSON.stringify(c).slice(0, 120));
});

test('the zone in use: the one chosen where it is offered and known, the computer\'s own otherwise', () => {
  assert.equal(T.zoneInUse('', 'America/Chicago'), 'America/Chicago');
  assert.equal(T.zoneInUse('America/Denver', 'America/Chicago'), 'America/Denver');
  assert.equal(T.zoneInUse('Europe/Paris', 'America/Chicago'), 'America/Chicago', 'a zone that is not offered is not used');
  assert.equal(T.zoneInUse('Not/AZone', 'America/Chicago'), 'America/Chicago');
  assert.equal(T.zoneInUse(undefined, 'America/Chicago'), 'America/Chicago');
  assert.ok(T.zoneKnown(T.computerZone()));
});

test('a record\'s data line, as the app writes it: sample data says it does not change, a file ages, a record without a moment keeps its old line', () => {
  const now = Date.UTC(2026, 10, 19, 17, 0, 0);
  const demo = { demo: true, dataSource: 'Fictional demo data · extract #412', asOfUtc: '2026-07-16T11:12:00Z', dataAsOf: 'old' };
  assert.deepEqual(T.dataLine(demo, now, 1, 'America/New_York'), { text: 'Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (sample data, which does not change)', stale: false });
  const file = { demo: false, dataSource: 'Your portfolio file · P.csv', asOfUtc: '2026-11-18T20:00:00.000Z', dataAsOf: 'old' };
  assert.deepEqual(T.dataLine(file, now, 7, 'America/New_York'), { text: 'Your portfolio file · P.csv · as of Nov 18, 2026, 3:00 PM EST (21 hours ago)', stale: false });
  assert.deepEqual(T.dataLine(file, now + 7 * 86400000, 7, 'America/New_York'), { text: 'Your portfolio file · P.csv · as of Nov 18, 2026, 3:00 PM EST (7 days ago) · STALE: older than 7 days', stale: true });
  assert.deepEqual(T.dataLine({ dataAsOf: 'old line' }, now, 7, 'UTC'), { text: 'old line', stale: false });
  assert.deepEqual(T.dataLine({ dataSource: 'X', asOfUtc: 'not a date' }, now, 7, 'UTC'), { text: '', stale: false });
});
