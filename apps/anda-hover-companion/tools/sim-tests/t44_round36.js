// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 36 in the browser simulation: Today begins with what has moved since the person last looked (kept when Today closes: two dates and a
// fingerprint of the open item per ANDA, never its words), and from Thursday noon to Sunday it also shows next week. The rules are held to
// mockup/round36-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round36-golden.json'), 'utf8'));
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
  const reopen = async () => { await p.evaluate(() => { closeApp('today'); openToday(); }); await settle(); };
  const axe = async sel => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };

  // ---- G. the page gives the shared answers ----
  eq(await p.evaluate(texts => texts.map(t => fingerprint(t)), GOLD.fingerprints.map(f => f.text)), GOLD.fingerprints.map(f => f.print), 'G1 the fingerprint of an open item');
  eq(await p.evaluate(at => lookNow(DATA, Date.parse(at)), GOLD.demoLook.at), GOLD.demoLook, 'G2 the look kept of the demo records');
  const words = { '216482': 'IR response due in 5 days — Biopharmaceutics', '300001': 'Labeling comments due' };
  eq(await p.evaluate(([cases, w]) => cases.map(c => changesSince(c.before, c.now, id => w[id] || null)), [GOLD.cases, words]), GOLD.cases.map(c => c.result), 'G3 what moved, for every pair of looks');

  // ---- S. since you last looked ----
  await p.evaluate(() => openToday()); await settle();
  let t = await listText();
  eq([t[0], t[1].startsWith('Compared with what this PC kept when you last closed Today: Jul 15, 2026, 5:05 PM EDT.'), t[1].endsWith('(A fictional earlier look, for the demo.)')], ['SINCE YOU LAST LOOKED', true, true], 'S1 Today begins with what moved, says against when, and says the demo\'s earlier look is fictional');
  ok(t.includes('Forecast moved from Sat Oct 10 to Sat Oct 17 (7 days later). New item needs you: IR response due in 5 days — Biopharmaceutics.'), 'S2 an ANDA\'s changes are one row: the forecast that moved and the new item');
  eq(await p.evaluate(() => { const g = document.querySelector('#tdList .td-gone'); return g ? [g.tagName, g.textContent] : null; }), ['DIV', '209998No longer in your data.'], 'S3 an ANDA no longer in the data is listed as text, not a button');
  ok(t.indexOf('NEEDS A LOOK') > t.indexOf('SINCE YOU LAST LOOKED'), 'S4 the groups of round 33 follow');
  // the keyboard stays on the row pressed, in its own group
  // 216482 is under SINCE and again under NEEDS A LOOK: pressing the second keeps the keyboard there, not on the first
  await p.click('#tdList .td-it[data-grp="NEEDS A LOOK"][data-id="216482"]'); await settle();
  eq(await p.evaluate(() => [document.activeElement.dataset.grp, document.activeElement.dataset.id]), ['NEEDS A LOOK', '216482'], 'S5 pressing a row keeps the keyboard on that row, in its own group');
  eq(await axe('#today'), [], 'S6 accessibility scan of Today with the new groups');
  // closing keeps the look; nothing has moved since
  await p.evaluate(() => { window.__now = Date.UTC(2026, 6, 16, 15, 0); });
  await reopen(); t = await listText();
  eq([t[1], t[2]], ['Compared with what this PC kept when you last closed Today: Jul 16, 2026, 11:00 AM EDT.', 'Nothing has moved since you last looked.'], 'S7 closing Today keeps the look; the next time nothing has moved');
  ok(await p.evaluate(() => { const k = JSON.stringify(state.lastLook); return !k.includes('IR response') && !k.includes('Metformin') && /"item":"[0-9a-f]{8}"/.test(k); }), 'S8 the look kept holds two dates and a fingerprint, never the item\'s words or anything else of the record');
  // a change that comes in while Today is open is listed the next time, not taken into the look kept
  await p.evaluate(() => { DATA['214990'].fcD = [2026, 8, 20]; closeApp('today'); DATA['214990'].fcD = [2026, 8, 6]; openToday(); }); await settle(); t = await listText();
  eq(t[2], 'Nothing has moved since you last looked.', 'S8a what is kept is the look Today showed');
  await p.evaluate(() => { DATA['214990'].fcD = [2026, 8, 13]; closeApp('today'); openToday(); }); await settle(); t = await listText();
  ok(t.includes('Forecast moved from Thu Aug 6 to Thu Aug 13 (7 days later).'), 'S8b a change made while Today was open shows the next time it opens');
  await p.evaluate(() => { closeApp('today'); DATA['214990'].fcD = [2026, 8, 6]; openToday(); closeApp('today'); }); await settle();
  // the dot of a row no longer in the data has no color
  eq(await p.evaluate(() => { state.lastLook = undefined; openToday(); const d = document.querySelector('#tdList .td-gone .td-dot'); return d.className; }), 'td-dot', 'S8d the row of an ANDA no longer in the data has no colored mark');
  eq(await p.evaluate(() => [LOOK_SAID.more(1), LOOK_SAID.more(2)]), ['1 more change; the card of each ANDA shows its own.', '2 more changes; the card of each ANDA shows its own.'], 'S8e "1 more change", not "1 more changes"');
  await p.evaluate(() => { window.__now = Date.UTC(2026, 6, 16, 15, 0); closeApp('today'); }); await settle();
  // a change in the data shows the next time
  await p.evaluate(() => { closeApp('today'); DATA['214990'].fcD = [2026, 8, 13]; openToday(); }); await settle(); t = await listText();   // the data changes while Today is closed
  ok(t.includes('Forecast moved from Thu Aug 6 to Thu Aug 13 (7 days later).'), 'S9 a forecast that moved since is listed the next time');
  await p.evaluate(() => { closeApp('today'); DATA['214990'].fcD = [2026, 8, 6]; openToday(); }); await settle();
  // a first start: nothing kept yet
  await p.evaluate(() => { closeApp('today'); state.lastLook = null; openToday(); }); await settle(); t = await listText();
  eq([t[0], t[1]], ['SINCE YOU LAST LOOKED', 'From now on, what moves in your data between two looks is listed here.'], 'S10 on a first start Today says what the group will hold');

  // ---- W. next week ----
  const week = async ms => { await p.evaluate(m => { window.__now = m; }, ms); await reopen(); return (await listText()).find(x => x.startsWith('NEXT WEEK')) || null; };
  eq(await week(Date.UTC(2026, 6, 16, 15, 59)), null, 'W1 Thursday 11:59 AM: no next week yet');
  eq(await week(Date.UTC(2026, 6, 16, 16, 0)), 'NEXT WEEK · MON JUL 20 TO FRI JUL 24', 'W2 Thursday at noon: next week appears, Monday to Friday');
  eq(await week(Date.UTC(2026, 6, 19, 18, 0)), 'NEXT WEEK · MON JUL 20 TO FRI JUL 24', 'W3 Sunday: still shown');
  eq(await week(Date.UTC(2026, 6, 20, 14, 0)), null, 'W4 Monday: gone again');
  await week(Date.UTC(2026, 10, 20, 20, 0)); t = await listText();
  ok(t.includes('NEXT WEEK · MON NOV 23 TO FRI NOV 27') && t.includes('Thu Nov 26: Thanksgiving Day (federal holiday)') && t.includes('Nothing of yours falls due next week.'), 'W5 a week with a federal holiday names it, and says when nothing of the person\'s falls due');
  await week(Date.UTC(2026, 6, 31, 20, 0)); t = await listText();
  ok(t.includes('NEXT WEEK · MON AUG 3 TO FRI AUG 7') && t.includes('Forecast Thu Aug 6'), 'W6 a forecast that falls next week is listed under its ANDA');
  eq(await p.evaluate(rows => rows.map(w => nextWeek(dayOfIso(w.today), w.hour, DATA)), GOLD.weeks), GOLD.weeks.map(w => w.result), 'W7 every day of the shared answers');

  // ---- F. what changed ----
  eq(await p.evaluate(() => CHANGES.find(c => c.round === 36).items), GOLD.changes.items, 'F1 what changed in round 36 is listed');
  eq(errs, [], 'F2 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
