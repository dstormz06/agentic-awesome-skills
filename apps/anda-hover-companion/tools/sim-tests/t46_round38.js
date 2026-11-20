// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 38 in the browser simulation: Today's next week also lists the person's unfinished to-do items due that week, from both lists. The
// rule is held to mockup/round38-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round38-golden.json'), 'utf8'));
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

  // ---- G. the page gives the shared answers ----
  eq(await p.evaluate(g => { const q = s => s ? s.split('-').map(Number) : null, data = {}; Object.keys(g.records).forEach(id => { data[id] = { goalD: q(g.records[id].goal), fcD: q(g.records[id].forecast) }; });
    return g.sets.map(c => nextWeek(dayOfIso(g.today), g.hour, c.noRecords ? {} : data, [...c.tasks].reverse())); }, GOLD), GOLD.sets.map(c => c.result), 'G1 every set of to-do items, given in any order');
  eq(await p.evaluate(ns => ns.map(n => weekTasksMore(n)), GOLD.more.map(m => m.n)), GOLD.more.map(m => m.text), 'G2 how many more, one and many');

  // ---- T. in Today, from the two lists ----
  // a Friday: next week is Mon Nov 30 to Fri Dec 4, 2026
  await p.evaluate(() => { window.__now = Date.UTC(2026, 10, 27, 15, 0);
    todoAdd('Fictional: confirm the meeting room', null, null, Date.UTC(2026, 11, 2), 'You');
    todoAdd('Fictional: finished item', null, null, Date.UTC(2026, 11, 2), 'You');
    todoStore.items.find(i => i.title === 'Fictional: finished item').done = true;
    clAdd('Fictional: send the weekly status update', 2, Date.UTC(2026, 11, 1), false, 0, -1, null, 'You');
    clAdd('Fictional: after the week', 2, Date.UTC(2026, 11, 8), false, 0, -1, null, 'You');
    openToday(); }); await settle();
  let t = await listText();
  const w = t.indexOf('NEXT WEEK · MON NOV 30 TO FRI DEC 4');
  ok(w >= 0, 'T1 on a Friday, Today shows next week');
  eq(t.slice(w + 1).filter(x => x.includes('to-do list:')), ['Tue Dec 1 · My to-do list: Fictional: send the weekly status update', 'Wed Dec 2 · ANDA to-do list: Fictional: confirm the meeting room'],
    'T2 the unfinished items due that week, from both lists, by date; a finished item and one after the week are not listed');
  ok(!t.includes('Nothing of yours falls due next week.'), 'T3 with to-do items due, the week does not say nothing falls due');
  eq(await p.evaluate(() => [...document.querySelectorAll('#tdList .td-task')].map(e => e.tagName)), ['DIV', 'DIV'], 'T4 the items are text, not buttons');
  eq(await axe('#today'), [], 'T5 accessibility scan of Today with the items');
  // twenty-two items: twenty, then how many more
  await p.evaluate(() => { closeApp('today'); for (let i = 0; i < 21; i++) clAdd('Fictional item ' + String(i).padStart(2, '0'), 2, Date.UTC(2026, 11, 3), false, 0, -1, null, 'You'); openToday(); }); await settle();
  t = await listText();
  eq([t.filter(x => x.includes('to-do list:')).length, t.includes('3 more to-do items due next week; the to-do lists show them all.')], [20, true], 'T6 twenty items are listed, then how many more');
  // the lists are only read: nothing new is kept
  ok(await p.evaluate(() => !JSON.stringify(state.lastLook || {}).includes('Fictional')), 'T7 the look kept holds no to-do item');
  await p.evaluate(() => closeApp('today'));

  // ---- F. what changed ----
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 38).items, APP_VERSION]), [GOLD.changes.items, V29], 'F1 what changed in round 38 is listed, and the version is the one the app is held to');
  eq(errs, [], 'F2 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
