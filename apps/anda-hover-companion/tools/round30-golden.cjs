// Writes (WRITE=1) or checks mockup/round30-golden.json: the answers of the browser simulation for round 30 (which backup copies of a list
// stay, the day a moment falls on for the person, and the archive of finished to-do items as the window shows it). The Windows app's tests
// (Round30Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round30-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round30-golden.json';
const Z = s => Date.parse(s);
const H = 3600000, D = 24 * H;
// The night the clocks go back (Nov 1, 2026) and the night they go forward (Mar 8, 2026) in New York, and zones far from UTC.
const DAYS = [
  ['2026-11-01T03:59:59Z', 'America/New_York'], ['2026-11-01T04:00:00Z', 'America/New_York'], ['2026-11-01T05:30:00Z', 'America/New_York'], ['2026-11-01T06:30:00Z', 'America/New_York'],
  ['2026-11-02T04:59:59Z', 'America/New_York'], ['2026-11-02T05:00:00Z', 'America/New_York'],
  ['2026-03-08T04:59:59Z', 'America/New_York'], ['2026-03-08T05:00:00Z', 'America/New_York'], ['2026-03-09T03:59:59Z', 'America/New_York'], ['2026-03-09T04:00:00Z', 'America/New_York'],
  ['2026-11-01T03:59:59Z', 'UTC'], ['2026-12-31T23:59:59Z', 'UTC'], ['2026-12-31T18:29:59Z', 'Asia/Kolkata'], ['2026-12-31T18:30:00Z', 'Asia/Kolkata'],
  ['2026-12-31T09:59:59Z', 'Pacific/Kiritimati'], ['2026-12-31T10:00:00Z', 'Pacific/Kiritimati'], ['2026-07-04T06:59:59Z', 'America/Los_Angeles'], ['2026-07-04T07:00:00Z', 'America/Los_Angeles'],
  ['2026-11-01T03:59:59Z', 'Not/AZone'], ['2026-11-01T03:59:59Z', ''],
].map(([t, tz]) => [Z(t), tz]);
const now = Z('2026-11-18T20:00:00Z');
const burst = n => Array.from({ length: n }, (_, i) => now - i * 60000);                 // n copies today, a minute apart
const daily = (n, from) => Array.from({ length: n }, (_, i) => from - i * D);            // one copy a day
const dst = [Z('2026-11-02T03:30:00Z'), Z('2026-11-01T04:30:00Z'), Z('2026-11-01T03:30:00Z'), Z('2026-10-31T12:00:00Z')];
const PLANS = [
  { stamps: [], keep: 30, tz: 'America/New_York' },
  { stamps: burst(5), keep: 10, tz: 'America/New_York' },
  { stamps: burst(12), keep: 10, tz: 'America/New_York' },
  { stamps: daily(40, now), keep: 30, tz: 'America/New_York' },
  { stamps: [...burst(25), ...daily(40, now - D)], keep: 10, tz: 'America/New_York' },
  { stamps: [...burst(10), ...dst, ...daily(12, Z('2026-10-30T12:00:00Z'))], keep: 10, tz: 'America/New_York' },
  { stamps: [...burst(10), ...dst, ...daily(12, Z('2026-10-30T12:00:00Z'))], keep: 10, tz: 'UTC' },
  { stamps: [...burst(10), ...dst, ...daily(12, Z('2026-10-30T12:00:00Z'))], keep: 10, tz: 'Asia/Kolkata' },
  { stamps: [...burst(3), ...burst(3), now - 5 * D], keep: 10, tz: 'UTC' },
  { stamps: daily(45, now), keep: 7, tz: 'UTC' },
  { stamps: [...burst(70), ...daily(70, now - D)], keep: 60, tz: 'America/New_York' },
  { stamps: [...burst(120), ...daily(120, now - D)], keep: 100, tz: 'Not/AZone' },
];
const item = (id, title, number) => ({ id, title, number: number || null, done: true, doneOn: '2026-10-01' });
const ARCHIVE = [
  { archivedOn: '2026-11-01', why: 'finished more than 30 days ago', item: item('t1', 'Send the meeting minutes', '216482') },
  { archivedOn: '2026-11-03', why: 'cleared by the person', item: item('t2', 'Ask about the  facility\tinspection\ndate') },
  null, 'not a line', { archivedOn: '2026-11-04', why: 'x' }, { archivedOn: '2026-11-04', why: 'x', item: { title: 'no id' } }, { archivedOn: '2026-11-04', why: 'x', item: { id: '', title: 'empty id' } },
  { restoredOn: '2026-11-05', id: 't1' },
  { archivedOn: '2026-11-10', why: 'cleared by the person', item: item('t1', 'Send the meeting minutes', '216482') },
  { archivedOn: '2026-11-11', why: 'the list was full', item: item('t3', 'CALL the applicant about ANDA 214990', '21499') },
  { restoredOn: '2026-11-12', id: 'never-archived' },
  { archivedOn: 20261113, why: 7, item: item('t4', '<b>Bold</b> & "quoted" É', '214990') },
  { restoredOn: '2026-11-14', id: 't4' }, { restoredOn: '2026-11-15', id: 't4' },
];
const QUERIES = [['', 0], ['minutes', 0], ['MINUTES', 0], ['  facility   inspection ', 0], ['216482', 0], ['2149', 0], ['nothing like this', 0], ['<b>', 0], ['é', 0], ['', 2], ['e', 1]];
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(([days, plans, archive, queries]) => ({
    about: 'Shared cases for round 30, written by the browser simulation. The Windows app must give the same answers.',
    keepChoices: BACKUP_KEEP_CHOICES, keepDefault: BACKUP_KEEP_DEFAULT, archiveDaysChoices: ARCHIVE_DAYS_CHOICES, archiveDaysDefault: ARCHIVE_DAYS_DEFAULT,
    days: days.map(([ms, tz]) => ({ ms, tz, day: localDay(ms, tz) })),
    plans: plans.map(c => ({ ...c, result: backupPlan(c.stamps, c.keep, c.tz) })),
    archive, archiveRows: queries.map(([query, max]) => ({ query, max, result: archiveRows(archive, query, max) })),
    emptyArchive: archiveRows([], '', 0),
  }), [DAYS, PLANS, ARCHIVE, QUERIES]);
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.days.length} days, ${doc.plans.length} plans, ${doc.archiveRows.length} views of the archive`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round30-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
