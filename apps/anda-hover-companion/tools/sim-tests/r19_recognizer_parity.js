// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// The simulation's recognizer must give the same answer as the app's (the golden file the app writes) for every case.
const { chromium } = require('playwright');
const fs = require('fs');
const golden = JSON.parse(fs.readFileSync(SIMROOT + '/extension/tests/golden.json', 'utf8'));
(async () => {
  const b = await chromium.launch(); const p = await b.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIMURL + '/mockup/windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const got = await p.evaluate(texts => texts.map(t => extractNumber(t)), golden.recognizer.map(c => c.text));
  let bad = 0;
  golden.recognizer.forEach((c, i) => { if (got[i] !== c.number) { bad++; console.log('MISMATCH', JSON.stringify(c.text), 'app', c.number, 'simulation', got[i]); } });
  for (const t of ['N210793', 'n210793', 'N-210793', 'A210793', 'IN210793', 'N210793 and A216482', '_N210793']) console.log(JSON.stringify(t), '->', golden.recognizer.find(c => c.text === t).number);
  console.log(`${golden.recognizer.length} cases, ${bad} mismatches; page errors: ${JSON.stringify(errs)}`);
  await b.close(); process.exit(bad || errs.length ? 1 : 0);
})();
