// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const fs = require('fs');
const AXE = fs.readFileSync(AXE_FILE, 'utf8');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
(async () => {
  const b = await chromium.launch();
  const all = [];
  async function scan(page, label) {
    await page.evaluate(AXE);
    const r = await page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] } })).violations.map(v => ({ id: v.id, impact: v.impact, n: v.nodes.length, help: v.help, sample: v.nodes.slice(0, 3).map(n => n.target.join(' ') + ' :: ' + (n.any[0]?.message || n.all[0]?.message || '').slice(0, 140)) })));
    all.push([label, r]);
  }
  for (const [name, w, h] of [['desktop', 1366, 768], ['phone', 390, 844]]) {
    const p = await b.newPage({ viewport: { width: w, height: h } });
    await p.goto(SIM); await p.waitForTimeout(300);
    // settings with every new editor open: a hotkey on an action, an agent switched on and its connection expanded, a problem line showing
    await p.evaluate(() => { state.actions[0].hotkey = 'Ctrl+Alt+T'; state.actions[2].template = ''; state.agents[0].enabled = true; renderActions(); renderAgents(); document.querySelectorAll('#agList details').forEach(d => d.open = true); });
    await p.evaluate(() => { document.getElementById('settings').style.display = 'block'; });
    await p.waitForTimeout(150); await scan(p, `${name}: settings with actions, agents and to-do`);
    await p.evaluate(() => { document.getElementById('settings').style.display = 'none'; state.actions = starterActions(); state.agents = presetAgents(); renderActions(); renderAgents(); });
    // to-do list with items of every kind
    await p.evaluate(() => { todoAdd('Call the applicant', '214990', null, ymd(2026, 7, 17), 'You'); const r = todoAdd('Finish me', null, null, null, 'You'); todoSetDone(r.item.id, true); todoAdd('Snooze me', null, null, null, 'You'); todoSnooze('t3', ymd(2026, 7, 20)); openTodo(); });
    await p.waitForTimeout(300); await scan(p, `${name}: to-do list`);
    await p.evaluate(() => closeApp('todo'));
    // action window: preview, result with suggestions, and a failure
    await p.evaluate(() => { state.ai = true; executeAction(state.actions[2], '216482', '216482', '216482', { x: 200, y: 100 }); });
    await p.waitForTimeout(200); await scan(p, `${name}: action window preview`);
    await p.evaluate(() => awSend()); await p.waitForTimeout(900); await scan(p, `${name}: action window with reply`);
    await p.evaluate(() => closeApp('actwin'));
    // agents drawer on the expanded card: not asked, answered, failed
    await p.evaluate(() => { state.agents[0].enabled = true; state.agents[0].api.endpoint = 'https://alerthub.example.test/ask'; openCard('216482', { anchor: { x: 300, y: 80 }, forceFull: true }); document.getElementById('expBtn').click(); document.getElementById('x-agents-d').open = true; });
    await p.waitForTimeout(500); await scan(p, `${name}: agents drawer, not asked`);
    await p.evaluate(() => askAgent(state.agents[0], '216482', null, true)); await p.waitForTimeout(1200); await scan(p, `${name}: agents drawer, answered`);
    await p.evaluate(() => { state.agentFail = true; return askAgent(state.agents[0], '216482', null, true); }); await p.waitForTimeout(1200); await scan(p, `${name}: agents drawer, failed`);
    await p.evaluate(() => { state.agentFail = false; closeCard(); });
    // tray menu with the actions submenu open
    await p.evaluate(() => { state.actions[0].hotkey = 'Ctrl+Alt+T'; openMenu(); [...document.querySelectorAll('#menu button')].find(x => x.textContent.startsWith('My actions')).click(); });
    await p.waitForTimeout(150); await scan(p, `${name}: tray menu, my actions open`);
    await p.close();
  }
  await b.close();
  const merged = {};
  for (const [label, r] of all) for (const v of r) { const k = v.id; (merged[k] ||= { impact: v.impact, help: v.help, where: new Set(), sample: v.sample }).where.add(label); }
  const keys = Object.keys(merged);
  console.log(`${all.length} scans`);
  if (!keys.length) console.log('NO VIOLATIONS');
  for (const k of keys) console.log(`${merged[k].impact.toUpperCase()} ${k}: ${merged[k].help}\n   in ${merged[k].where.size} scans, e.g. ${[...merged[k].where][0]}\n   ${merged[k].sample.join('\n   ')}`);
})();
