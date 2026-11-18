// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Writes (WRITE=1) or checks mockup/countdown-golden.json: the days-to-goal rule of look X, taken from the simulation's own functions.
const { chromium } = require('playwright'); const fs = require('fs');
const FILE = SIMROOT + '/mockup/countdown-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIMURL + '/mockup/windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const days = [-4000, -400, -31, -8, -3, -2, -1, 0, 1, 2, 6, 7, 8, 21, 29, 30, 31, 79, 365, 4000];
  const cases = await p.evaluate(ds => ds.map(n => ({ days: n, level: countdownLevel(n), words: countdownWords(n), figure: countdownFigure(n), caption: countdownCaption(n) })), days);
  const doc = { about: 'The days-to-goal indicator of look X. The simulation and the Windows app must give the same level, words, figure and caption for each number of days. Generated from the simulation; do not edit by hand.', cases };
  if (process.env.WRITE) { fs.writeFileSync(FILE, JSON.stringify(doc, null, 2) + '\n'); console.log('wrote', cases.length, 'cases'); }
  else { const want = JSON.parse(fs.readFileSync(FILE, 'utf8')).cases; const bad = cases.filter((c, i) => JSON.stringify(c) !== JSON.stringify(want[i])); console.log(`${cases.length} cases, ${bad.length + Math.abs(cases.length - want.length)} mismatches; page errors: ${JSON.stringify(errs)}`); if (bad.length || cases.length !== want.length || errs.length) process.exit(1); }
  await b.close();
})();
