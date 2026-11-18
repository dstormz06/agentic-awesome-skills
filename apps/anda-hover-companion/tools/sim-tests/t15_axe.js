// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const fs = require('fs');
const AXE = fs.readFileSync(AXE_FILE, 'utf8');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const LAB = SIMURL + '/mockup/appearance-lab.html';
const OPT = process.env.OPTIONS_PAGE ? require('url').pathToFileURL(process.env.OPTIONS_PAGE).href : '';      // an older proposals page that is not in the repository: scanned only when its path is given
(async () => {
  const b = await chromium.launch();
  const all = [];
  async function scan(page, label) {
    await page.evaluate(AXE);
    const r = await page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } })).violations.map(v => ({ id: v.id, impact: v.impact, n: v.nodes.length, help: v.help, sample: v.nodes.slice(0, 2).map(n => n.target.join(' ') + ' :: ' + (n.any[0]?.message || n.all[0]?.message || '').slice(0, 110)) })));
    all.push([label, r]);
  }
  for (const [name, w, h] of [['desktop', 1366, 768], ['phone', 390, 844]]) {
    const p = await b.newPage({ viewport: { width: w, height: h } });
    await p.goto(SIM); await p.waitForTimeout(300);
    await scan(p, `sim ${name}: start`);
    for (const look of ['k', 'p', 'x', 'v', 'u', 't']) {
      await p.evaluate(l => { applyLook(l); openCard('216482', { anchor: { x: 300, y: 80 }, forceFull: true }); }, look);
      await p.waitForTimeout(600);
      await scan(p, `sim ${name}: card look ${look}`);
      await p.evaluate(() => closeCard());
    }
    await p.evaluate(() => { applyLook('k'); openCard('216482', { anchor: { x: 300, y: 80 }, forceFull: true }); document.getElementById('expBtn')?.click(); });
    await p.waitForTimeout(500); await scan(p, `sim ${name}: expanded card`);
    await p.evaluate(() => closeCard());
    for (const id of ['settings', 'lookup', 'draft', 'wlist', 'plib', 'replies']) {
      await p.evaluate(i => { document.querySelectorAll('.app-win').forEach(e => e.style.display = 'none'); document.getElementById(i).style.display = 'block'; }, id);
      await p.waitForTimeout(100); await scan(p, `sim ${name}: window ${id}`);
      await p.evaluate(i => { document.getElementById(i).style.display = 'none'; }, id);
    }
    await p.close();
  }
  for (const [n, u] of [['lab', LAB], ...(OPT ? [['options', OPT]] : [])]) { const p = await b.newPage({ viewport: { width: 1366, height: 768 } }); await p.goto(u); await p.waitForTimeout(300); await scan(p, n); await p.close(); }
  await b.close();
  const merged = {};
  for (const [label, r] of all) for (const v of r) { const k = v.id; (merged[k] ||= { impact: v.impact, help: v.help, where: new Set(), sample: v.sample }).where.add(label); }
  const keys = Object.keys(merged);
  if (!keys.length) console.log('NO VIOLATIONS');
  for (const k of keys) console.log(`${merged[k].impact.toUpperCase()} ${k}: ${merged[k].help}\n   in ${merged[k].where.size} scans, e.g. ${[...merged[k].where][0]}\n   ${merged[k].sample.join('\n   ')}`);
})();
