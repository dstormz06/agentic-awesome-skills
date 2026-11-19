// Writes (WRITE=1) or checks mockup/round31-golden.json: the answers of the browser simulation for round 31 (a moment as it is written in
// a time zone, with the zone's abbreviation and daylight time; how long ago; and the line that says where a piece of data is from, as of
// when, and whether it is stale). The Windows app's tests (Round31Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round31-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round31-golden.json';
const Z = s => Date.parse(s);
const M = 60000, H = 60 * M, D = 24 * H;
const ZONES = ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix', 'America/Los_Angeles', 'America/Anchorage', 'Pacific/Honolulu', 'America/Puerto_Rico', 'UTC', 'Etc/UTC',
  'Europe/Berlin', 'Asia/Kolkata', 'America/St_Johns', 'Pacific/Kiritimati', 'Not/AZone'];
// Both nights the clocks change in the United States in 2026 (Mar 8 and Nov 1), each side of them; noon, midnight, a year's end, a leap day.
const MOMENTS = ['2026-03-08T06:59:59Z', '2026-03-08T07:00:00Z', '2026-03-08T09:59:59Z', '2026-03-08T10:00:00Z', '2026-11-01T05:59:59Z', '2026-11-01T06:00:00Z', '2026-11-01T08:59:59Z', '2026-11-01T09:00:00Z',
  '2026-07-16T14:42:05Z', '2026-07-16T16:00:00Z', '2026-07-16T04:00:00Z', '2026-12-31T23:59:59Z', '2027-01-01T00:00:00Z', '2028-02-29T12:34:56Z', '2026-01-15T00:05:09Z'].map(Z);
const now = Z('2026-11-18T20:00:00Z');
const AGO = [0, 1, 59 * 1000, 60 * 1000, 61 * 1000, 59 * M + 59000, H, H + 1, 119 * M, 2 * H, 23 * H + 59 * M, D, D + H, 2 * D - 1, 2 * D, 6 * D + 23 * H, 7 * D, 30 * D, 365 * D, -1000, -59 * 1000, -60 * 1000, -61 * 1000, -D];
const FRESH = [];
for (const days of [1, 3, 7, 14, 30, 5, 0]) for (const age of [0, 30 * M, D - 1, D, D + 1, 3 * D, 7 * D, 7 * D + 1, 9 * D, 14 * D + M, 30 * D, 31 * D]) FRESH.push({ source: 'YOUR FILE · my-portfolio.xlsx', asOf: now - age, now, days, zone: 'America/New_York' });
FRESH.push({ source: 'Fictional demo data · extract #412', asOf: null, now, days: 7, zone: 'America/Chicago' }, { source: '<b>markup</b> & "quotes"', asOf: now - 9 * D, now, days: 7, zone: 'Pacific/Honolulu' },
  { source: 'A file from a clock that was ahead', asOf: now + 2 * H, now, days: 7, zone: 'UTC' }, { source: 'Across the clock change', asOf: Z('2026-10-31T16:00:00Z'), now: Z('2026-11-07T16:30:00Z'), days: 7, zone: 'America/New_York' });
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(([zones, moments, now, ago, fresh]) => ({
    about: 'Shared cases for round 31, written by the browser simulation. The Windows app must give the same answers.',
    zoneChoices: TZ_CHOICES, abbreviations: TZ_ABBR, staleChoices: STALE_CHOICES, staleDefault: STALE_DEFAULT,
    labels: zones.flatMap(zone => moments.map(ms => ({ zone, ms, offset: zoneClock(ms, zone).offset, abbr: zoneAbbr(ms, zone), label: timeLabel(ms, zone, false), withSeconds: timeLabel(ms, zone, true), time: timeOfDay(ms, zone, false), timeWithSeconds: timeOfDay(ms, zone, true) }))),
    unknown: [timeLabel(null, 'UTC', false), timeLabel(NaN, 'UTC', true), relTime(null, 5), relTime(5, NaN), timeOfDay(null, 'UTC', true)],
    relative: ago.map(a => ({ then: now - a, now, text: relTime(now - a, now) })),
    fresh: fresh.map(c => ({ ...c, result: freshLine(c.source, c.asOf, c.now, c.days, c.zone) })),
  }), [ZONES, MOMENTS, now, AGO, FRESH]);
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.labels.length} moments in ${ZONES.length} zones, ${doc.relative.length} ages, ${doc.fresh.length} data lines`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round31-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
