// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 32 in the browser simulation. Part A: the Drugs@FDA reference file says when it was saved and how long ago, and is marked STALE
// after a number of days of its own (45 unless the person changes it), separate from the days for the person's own data.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async () => {
    const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300); return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE_FILE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(document.querySelector(s))).violations.map(v => v.id + ': ' + v.nodes.length), sel); };
  const rld = p => p.evaluate(() => [document.getElementById('c-rld').textContent, document.getElementById('c-rld').classList.contains('stale')]);
  const card = async p => { await p.evaluate(() => { APP_WINS.forEach(closeApp); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await settle(p, 400); return rld(p); };
  const look = p => p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; });

  // ====================================================== A. The reference file's own days
  let p = await fresh();
  eq(await p.evaluate(() => [REF_STALE_CHOICES, REF_STALE_DEFAULT, state.refStaleDays, STALE_DEFAULT]), [[14, 30, 45, 60, 90], 45, 45, 7], 'A1 the reference file is stale after 45 days unless another number is chosen; my own data keeps its 7');
  eq((await card(p))[1], false, 'A2 with no reference file chosen nothing is called stale');
  await p.evaluate(() => { showApp('settings'); document.getElementById('dataRefPick').click(); }); await settle(p);
  eq(await p.evaluate(() => document.getElementById('dataRefStatus').textContent), 'Drugs@FDA file loaded (simulated: 1 reference product, file saved Jun 28, 2026).', 'A3 Settings says when the file was saved, with the year');
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago)', false], 'A4 the card says where the labeling date is from, when that file was saved and how long ago');
  await p.evaluate(() => document.getElementById('swOldRef').click()); await settle(p);
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved May 17, 2026 (60 days ago) · STALE: older than 45 days', true], 'A5 a file older than its days is marked STALE, in words and by its look');
  eq(await p.evaluate(() => [dataLine().stale, watchFlags('216482').some(f => f.code === 'stale-data')]), [false, false], 'A6 my own data is not called stale because the reference file is');
  await look(p); await p.selectOption('#setRefStale', '90'); await settle(p);
  eq([await p.evaluate(() => document.getElementById('setTimeHint').textContent), await p.evaluate(() => [state.refStaleDays, state.staleDays])], ['A Drugs@FDA file older than 90 days is now marked STALE.', [90, 7]], 'A7 the number is the person' + "'" + 's choice, said plainly, and leaves the days for my data alone');
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved May 17, 2026 (60 days ago)', false], 'A8 with 90 days chosen the same file is not stale');
  await look(p); await p.selectOption('#setRefStale', '14'); await settle(p);
  await p.evaluate(() => document.getElementById('swOldRef').click()); await settle(p);
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago) · STALE: older than 14 days', true], 'A9 with 14 days chosen a file of 18 days is stale');
  await look(p); await p.selectOption('#setTz', 'UTC'); await settle(p);
  eq((await card(p))[0], 'Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago) · STALE: older than 14 days', 'A10 the day is the day in the zone in use');
  await look(p); await p.selectOption('#setTz', ''); await p.selectOption('#setRefStale', '45'); await settle(p);
  // the open card keeps step: a day passes and nobody calls anything but the minute tick
  await card(p);
  eq(await p.evaluate(() => { window.__now = FIXED_NOW + 86400000 + 60000; timeTick(); const t = document.getElementById('c-rld').textContent; delete window.__now; timeTick(); return t; }), 'Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (19 days ago)', 'A11 on an open card the age moves with the clock');
  // settings: carried by a backup; a value that is not a choice becomes 45
  eq(await p.evaluate(() => { state.refStaleDays = 90; const x = JSON.parse(backupText()); const carried = x.settings.refStaleAfterDays; x.settings.refStaleAfterDays = 7; importBackup(JSON.stringify(x)); const a = state.refStaleDays; x.settings.refStaleAfterDays = 60; importBackup(JSON.stringify(x)); const b2 = state.refStaleDays; state.refStaleDays = 45; return [carried, a, b2, document.getElementById('setRefStale').value]; }),
    [90, 45, 60, '60'], 'A12 a settings backup carries the number; 7, which is a choice for my data only, becomes 45');
  eq(await p.evaluate(() => [refSaved(null, 5, 45, 'UTC'), refSaved(1e20, 5, 45, 'UTC'), refSaved(Date.UTC(2026, 10, 19, 21), Date.UTC(2026, 10, 19, 18), 45, 'UTC')].map(x => [x.text, x.stale])),
    [['saved: time not known', false], ['saved: time not known', false], ["saved Nov 19, 2026 (later than now: please check this computer's clock)", false]], 'A13 a file whose time is not known, or is later than now, is not called stale and says why');
  await look(p); await settle(p);
  eq(await axe(p, '#secLook'), [], 'A14 accessibility scan of the part of Settings that holds the three boxes');
  eq(await p.evaluate(() => document.querySelector('label[for=setRefStale]').textContent), 'Mark the Drugs@FDA reference file as stale when it is older than', 'A15 the new box has a label of its own');
  await p.close();
  // a phone-sized window
  { const ctx = await b.newContext({ viewport: { width: 390, height: 780 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.goto(SIM); await q.waitForTimeout(300); await look(q); await settle(q, 250);
    eq(await q.evaluate(() => { const w = document.getElementById('settings'); return [document.documentElement.scrollWidth <= innerWidth + 1, w.scrollWidth <= w.clientWidth + 1, document.getElementById('setRefStale').getBoundingClientRect().right <= innerWidth]; }), [true, true, true], 'A16 nothing runs off the side at phone width');
    await q.close(); }

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
