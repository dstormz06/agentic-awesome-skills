// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// axe-core over the round-18 surfaces: the three new cards, Settings with Display and Recent ANDAs, One Bar, the plain lookup with recents, and Today. Desktop and phone.
const { chromium } = require('playwright');
const fs = require('fs');
const AXE = fs.readFileSync(AXE_FILE, 'utf8');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
(async () => {
  const b = await chromium.launch();
  const all = [];
  async function scan(page, label) {
    await page.evaluate(AXE);
    const r = await page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } })).violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, sample: v.nodes.slice(0, 2).map(n => n.target.join(' ') + ' :: ' + (n.failureSummary || '').split('\n').slice(1, 3).join(' ').slice(0, 160)) })));
    all.push([label, r]);
  }
  for (const [name, w, h] of [['desktop', 1366, 800], ['phone', 390, 844]]) {
    const p = await b.newPage({ viewport: { width: w, height: h } });
    await p.goto(SIM); await p.waitForTimeout(300);
    await p.dblclick('#deskIcon').catch(() => {}); await p.waitForTimeout(150);
    await p.evaluate(() => { state.op = 100; root.style.setProperty('--op', '100%'); });
    for (const [look, label] of [['t', 'Pocket Plain'], ['u', 'Calendar Ribbon'], ['v', 'Glass Sheet']]) {
      await p.evaluate(l => { closeCard(); applyLook(l); openCard('216482', { anchor: { x: 520, y: 70 }, forceFull: true }); }, look); await p.waitForTimeout(550);
      await scan(p, `${name}: card, ${label}, a late ANDA`);
    }
    await p.evaluate(() => { closeCard(); applyLook('t'); openCard('214990', { anchor: { x: 520, y: 70 }, forceFull: true }); }); await p.waitForTimeout(500);
    await scan(p, `${name}: card, Pocket Plain, an ANDA ahead of its goal`);
    await p.evaluate(() => { closeCard(); document.getElementById('chipPin'); state.pins = ['216482']; state.recents = ['214990', '216482']; openLookup({ x: 400, y: 200 }); }); await p.waitForTimeout(200);
    await scan(p, `${name}: plain lookup with recents and a kept ANDA`);
    await p.evaluate(() => { closeApp('lookup'); state.lookupStyle = 'bar'; openLookup({ x: 400, y: 200 }); }); await p.fill('#lookupIn', '216482'); await p.waitForTimeout(200);
    await scan(p, `${name}: One Bar with suggestions`);
    await p.evaluate(() => { closeApp('lookup'); state.lookupStyle = 'box'; openToday(); }); await p.waitForTimeout(200);
    await scan(p, `${name}: Today`);
    await p.evaluate(() => { closeApp('today'); showApp('settings'); document.getElementById('secStart').open = true; document.getElementById('secLook').open = true; }); await p.waitForTimeout(250);
    await scan(p, `${name}: Settings, display, recents and lookup window open`);
    await p.evaluate(() => { document.querySelector('input[name=ts][value=xl]').click(); document.querySelector('input[name=density][value=compact]').click(); closeApp('settings'); openCard('216482', { anchor: { x: 520, y: 70 }, forceFull: true }); }); await p.waitForTimeout(500);
    await scan(p, `${name}: card at the largest text and compact spacing`);
    await p.close();
  }
  await b.close();
  const merged = {};
  for (const [label, r] of all) for (const v of r) { (merged[v.id] ||= { impact: v.impact, help: v.help, where: new Set(), sample: v.sample }).where.add(label); }
  console.log(`${all.length} scans`);
  if (!Object.keys(merged).length) console.log('NO VIOLATIONS');
  for (const k of Object.keys(merged)) console.log(`${merged[k].impact.toUpperCase()} ${k}: ${merged[k].help}\n   in ${merged[k].where.size} scans, e.g. ${[...merged[k].where][0]}\n   ${merged[k].sample.join('\n   ')}`);
})();
