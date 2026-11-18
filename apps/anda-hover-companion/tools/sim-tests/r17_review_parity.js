// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright'); const fs = require('fs');
const G = JSON.parse(fs.readFileSync(SIMROOT + '/mockup/review-golden.json', 'utf8'));
(async () => {
  const b = await chromium.launch(); const p = await b.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIMURL + '/mockup/windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const [y, m, d] = G.today.split('-').map(Number);
  const out = await p.evaluate(({ cases, t }) => cases.map(c => { const r = draftReview(c.draft, t); return { draft: c.draft, numbers: r.numbers ?? 0, dates: r.dates ?? 0, status: r.status ?? 0, findings: r.findings.map(f => ({ kind: f.kind, text: f.text })) }; }), { cases: G.cases, t: Date.UTC(y, m - 1, d) });
  let bad = 0;
  G.cases.forEach((c, i) => {
    const g = out[i], same = JSON.stringify(g.findings) === JSON.stringify(c.findings) && g.numbers === c.numbers && g.dates === c.dates && g.status === c.status;
    if (!same) { bad++; console.log('MISMATCH', JSON.stringify(c.draft.slice(0, 80))); console.log('  want', JSON.stringify(c.findings.map(f => f.kind + ':' + f.text).slice(0, 6)), c.numbers, c.dates, c.status); console.log('  got ', JSON.stringify(g.findings.map(f => f.kind + ':' + f.text).slice(0, 6)), g.numbers, g.dates, g.status); }
  });
  console.log(`${G.cases.length - bad}/${G.cases.length} cases match; page errors: ${JSON.stringify(errs)}`);
  await b.close(); process.exit(bad ? 1 : 0);
})();
