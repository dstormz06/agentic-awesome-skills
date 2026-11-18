// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// axe-core over every new round-16 surface, desktop and phone.
const { chromium } = require('playwright');
const fs = require('fs');
const AXE = fs.readFileSync(AXE_FILE, 'utf8');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
(async () => {
  const b = await chromium.launch();
  const all = [];
  async function scan(page, label) {
    await page.evaluate(AXE);
    const r = await page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } })).violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, sample: v.nodes.slice(0, 3).map(n => n.target.join(' ') + ' :: ' + (n.failureSummary || '').split('\n')[1]) })));
    all.push([label, r]);
  }
  for (const [name, w, h] of [['desktop', 1366, 768], ['phone', 390, 844]]) {
    const p = await b.newPage({ viewport: { width: w, height: h } });
    await p.goto(SIM); await p.waitForTimeout(300);
    await p.evaluate(() => showApp('settings')); await p.waitForTimeout(200); await scan(p, `${name}: Settings, new install (two sections open, pills, Getting started)`);
    await p.evaluate(() => document.getElementById('adaptExpand').click()); await p.waitForTimeout(150); await scan(p, `${name}: Settings, every section open`);
    await p.evaluate(() => { document.getElementById('adaptCollapse').click(); state.taken.peek = true; state.taken.list = true; state.gsDone = true; applySuggested(); }); await p.waitForTimeout(150); await scan(p, `${name}: Settings, sections that need a look`);
    await p.evaluate(() => { state.taken.peek = false; state.taken.list = false; closeApp('settings'); });
    await p.evaluate(() => { const t = clToday(), D = DAY; clAdd('Renew the vehicle registration', 1, t - 2 * D, false, 0, -1, null, 'You'); clAdd('Call the applicant about ANDA 216482', 0, t, true, 0, -1, '216482', 'Notes'); clAdd('Update the tracking sheet', 1, t + 3 * D, false, 3, 4, null, 'You'); const r = clAdd('Finish me', 2, null, false, 0, -1, null, 'You'); clSetDone(r.item.id, true, t); openMyTodo(); });
    await p.waitForTimeout(300); await scan(p, `${name}: My to-do list`);
    await p.evaluate(() => { clBeginEdit(clStore.items[0]); document.querySelector('#clRows .ced input[aria-label^="When it is due"]').value = 'not a date'; document.querySelector('#clRows .ced input[aria-label^="When it is due"]').dispatchEvent(new Event('input', { bubbles: true })); clSaveEdit(); });
    await p.waitForTimeout(200); await scan(p, `${name}: My to-do list, editing with a problem`);
    await p.evaluate(() => { closeApp('mytodo'); openNotes("Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. The inspection report was clear."); });
    await p.waitForTimeout(500); await scan(p, `${name}: Notes to my to-do list`);
    await p.evaluate(() => { ntRows[0].f.when.value = 'someday'; ntRows[0].f.when.dispatchEvent(new Event('input', { bubbles: true })); ntAddRows(ntTicked(), false); });
    await p.waitForTimeout(200); await scan(p, `${name}: Notes to my to-do list, a problem beside an item`);
    await p.evaluate(() => { closeApp('notes'); openMenu(); });
    await p.waitForTimeout(150); await scan(p, `${name}: tray menu with My to-do list`);
    await p.close();
  }
  await b.close();
  const merged = {};
  for (const [label, r] of all) for (const v of r) { (merged[v.id] ||= { impact: v.impact, help: v.help, where: new Set(), sample: v.sample }).where.add(label); }
  console.log(`${all.length} scans`);
  if (!Object.keys(merged).length) console.log('NO VIOLATIONS');
  for (const k of Object.keys(merged)) console.log(`${merged[k].impact.toUpperCase()} ${k}: ${merged[k].help}\n   in ${merged[k].where.size} scans, e.g. ${[...merged[k].where][0]}\n   ${merged[k].sample.join('\n   ')}`);
})();
