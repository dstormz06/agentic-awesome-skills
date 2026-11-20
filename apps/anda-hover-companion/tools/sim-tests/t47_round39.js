// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 39 in the browser simulation: a switch for the to-do items in Today's next week. Off: neither list is read for Today and no title
// shows. Its words are held to mockup/round39-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round39-golden.json'), 'utf8'));
const V29 = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round29-golden.json'), 'utf8')).version;
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
  const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
  await p.goto(SIM); await p.waitForTimeout(300);
  const settle = (ms = 200) => p.waitForTimeout(ms);
  const listText = () => p.evaluate(() => document.getElementById('tdList').innerText.split('\n').map(s => s.trim()).filter(Boolean));
  const axe = async sel => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };
  const TITLE = 'Tue Dec 1 · My to-do list: Fictional: send the weekly status update';

  eq(await p.evaluate(() => [document.getElementById('setWeekTodos').parentElement.textContent.trim(), document.getElementById('weekTodosHint').textContent, document.getElementById('setWeekTodos').checked, document.getElementById('setWeekTodos').getAttribute('aria-describedby')]),
    [GOLD.label, GOLD.hint, true, 'weekTodosHint'], 'W1 the switch has the shared label and hint, is on at first, and its hint is read with it');
  await p.evaluate(() => { window.__now = Date.UTC(2026, 10, 27, 15, 0); clAdd('Fictional: send the weekly status update', 2, Date.UTC(2026, 11, 1), false, 0, -1, null, 'You'); openToday(); showApp('settings'); document.getElementById('secStart').open = true; }); await settle();
  ok((await listText()).includes(TITLE), 'O1 (before) next week lists the to-do item');
  await p.focus('#setWeekTodos'); await p.keyboard.press('Space'); await settle();
  let t = await listText();
  ok(!t.includes(TITLE) && t.includes('Nothing of yours falls due next week.'), 'O2 off: an open Today drops the item at once, and the week says nothing falls due');
  eq(await p.evaluate(() => { let read = 0; const a = todoStore.items, c = clStore.items; const spy = arr => new Proxy(arr, { get(o, k) { read++; return o[k]; } });
    todoStore.items = spy(a); clStore.items = spy(c); const r = weekTasksNow(); todoStore.items = a; clStore.items = c; return [r, read]; }), [[], 0], 'O3 off: neither list is read for Today');
  eq(await axe('#settings'), [], 'O4 accessibility scan of Settings with the second switch');
  eq(await p.evaluate(() => { const t = backupText(); state.weekTodosOn = true; importBackup(t); return [JSON.parse(t).settings.weekTodos, state.weekTodosOn]; }), [false, false], 'B1 off is part of a backup and comes back');
  eq(await p.evaluate(() => { const t = JSON.parse(backupText()); delete t.settings.weekTodos; importBackup(JSON.stringify(t)); return state.weekTodosOn; }), false, 'B2 a backup from before round 39 keeps the choice the person has now');
  await p.focus('#setWeekTodos'); await p.keyboard.press('Space'); await settle();
  ok((await listText()).includes(TITLE), 'N1 on again: the item comes back at once');
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 39).items, APP_VERSION]), [GOLD.changes.items, V29], 'F1 what changed in round 39 is listed, and the version is the one the app is held to');
  eq(GOLD.backupKey, 'weekTodos', 'F2 the backup names the switch');
  eq(errs, [], 'F3 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
