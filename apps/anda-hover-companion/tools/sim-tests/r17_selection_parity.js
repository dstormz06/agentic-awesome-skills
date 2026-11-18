// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright'); const fs = require('fs');
const G = JSON.parse(fs.readFileSync(SIMROOT + '/mockup/selection-golden.json', 'utf8'));
(async () => {
  const b = await chromium.launch(); const p = await b.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(SIMURL + '/mockup/windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const out = await p.evaluate(cases => cases.map(c => ({ shape: shClassify(c.text), describe: shDescribe(c.text), duplicates: shFindDuplicates(c.text), compare: shCompareColumns(c.text, 1, 2), compare23: shCompareColumns(c.text, 2, 3), blanks: shFindBlanks(c.text), totals: shTotals(c.text) })), G.cases);
  let bad = 0;
  G.cases.forEach((c, i) => {
    const g = out[i];
    const gs = { kind: g.shape.kind, rows: g.shape.rows, columns: g.shape.columns, label: g.shape.label, suggestion: g.shape.suggestion };
    for (const k of ['shape', 'describe', 'duplicates', 'compare', 'compare23', 'blanks', 'totals']) {
      const got = k === 'shape' ? gs : g[k];
      if (JSON.stringify(got) !== JSON.stringify(c[k])) { bad++; console.log('MISMATCH', k, JSON.stringify(c.text.slice(0, 60))); console.log('  want', JSON.stringify(c[k])); console.log('  got ', JSON.stringify(got)); }
    }
  });
  console.log(`${G.cases.length} cases, ${bad} field mismatches; page errors: ${JSON.stringify(errs)}`);
  await b.close(); process.exit(bad || errs.length ? 1 : 0);
})();
