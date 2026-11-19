// Writes (WRITE=1) or checks mockup/round32-golden.json: the answers of the browser simulation for round 32 (when a reference file was
// saved, how long ago, and whether it is stale after its own number of days). The Windows app's tests (Round32Tests) must reproduce
// every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round32-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round32-golden.json';
const Z = s => Date.parse(s);
const M = 60000, H = 60 * M, D = 24 * H;
const now = Z('2026-11-19T18:00:00Z');
const REF = [];
for (const days of [14, 30, 45, 60, 90, 7, 0]) for (const age of [0, 30 * M, D, 14 * D, 14 * D + 1, 18 * D + 5 * H, 30 * D + M, 45 * D, 45 * D + 1, 60 * D, 60 * D + H, 90 * D + 1, 400 * D]) REF.push({ saved: now - age, now, days, zone: 'America/New_York' });
REF.push({ saved: null, now, days: 45, zone: 'UTC' }, { saved: 1e20, now, days: 45, zone: 'UTC' }, { saved: now + 3 * H, now, days: 45, zone: 'Pacific/Honolulu' }, { saved: Z('2026-11-01T03:30:00Z'), now, days: 14, zone: 'America/Los_Angeles' },
  { saved: Z('2026-11-01T03:30:00Z'), now, days: 14, zone: 'UTC' }, { saved: Z('2026-01-01T02:00:00Z'), now: Z('2026-02-20T02:00:00Z'), days: 45, zone: 'America/Chicago' }, { saved: Z('2026-01-01T02:00:00Z'), now: Z('2026-02-20T02:00:00Z'), days: 45, zone: 'Not/AZone' });
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(ref => ({
    about: 'Shared cases for round 32, written by the browser simulation. The Windows app must give the same answers.',
    refChoices: REF_STALE_CHOICES, refDefault: REF_STALE_DEFAULT,
    ref: ref.map(c => ({ ...c, saved: Number.isFinite(c.saved) && Math.abs(c.saved) < 9e15 ? c.saved : c.saved === null ? null : 'huge', result: refSaved(c.saved, c.now, c.days, c.zone) })),
  }), REF);
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.ref.length} reference files`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round32-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
