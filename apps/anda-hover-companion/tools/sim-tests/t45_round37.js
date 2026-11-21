// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 37 in the browser simulation: a switch in Settings for Today's "Since you last looked". Off: Today has no such group, nothing is
// kept, and what was kept is removed. Its words are held to mockup/round37-golden.json, which the Windows app is also tested against.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round37-golden.json'), 'utf8'));
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
  // round 42's group (most urgent first) now opens Today and is checked in t50; here Today is read from the group after it
  const listText = () => p.evaluate(() => { const out = []; let skip = false;
    for (const el of document.getElementById('tdList').children) {
      if (el.classList.contains('td-grp')) skip = el.textContent === TL_SAID.todayTitle;
      if (!skip) out.push(...el.innerText.split('\n').map(s => s.trim()).filter(Boolean));
    }
    return out; });
  const axe = async sel => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };

  // ---- W. the words ----
  eq(await p.evaluate(() => [document.getElementById('setLastLook').parentElement.textContent.trim(), document.getElementById('lastLookHint').textContent, document.getElementById('setLastLook').checked]),
    [GOLD.label, GOLD.hint, true], 'W1 the switch has the shared label and hint, and is on at first');

  // ---- O. off ----
  await p.evaluate(() => openToday()); await settle();
  ok((await listText())[0] === 'SINCE YOU LAST LOOKED', 'O1 (before) Today begins with what moved');
  await p.evaluate(() => { showApp('settings'); document.getElementById('secStart').open = true; }); await settle();
  await p.focus('#setLastLook'); await p.keyboard.press('Space'); await settle();   // by keyboard: Today may lie over Settings
  eq([await p.textContent('#lastLookStatus'), await p.evaluate(() => [state.lastLookOn, state.lastLook])], [GOLD.off, [false, null]], 'O2 turning it off says so and removes what was kept');
  ok(!(await listText()).includes('SINCE YOU LAST LOOKED'), 'O3 an open Today drops the group at once');
  eq(await axe('#settings'), [], 'O4 accessibility scan of Settings with the switch');
  await p.evaluate(() => { closeApp('settings'); closeApp('today'); }); await settle();
  eq(await p.evaluate(() => state.lastLook), null, 'O5 closing Today keeps nothing while it is off');
  await p.evaluate(() => openToday()); await settle();
  ok(!(await listText()).includes('SINCE YOU LAST LOOKED') && await p.evaluate(() => document.querySelector('#tdList .td-grp').textContent === TL_SAID.todayTitle && !!document.querySelector('#tdList .td-it[data-grp="urgent"]')),
    'O6 Today opened again has no such group; the rest is as it was');
  await p.evaluate(() => closeApp('today'));

  // ---- B. a backup ----
  eq(await p.evaluate(() => { const t = backupText(); const was = state.lastLookOn; state.lastLookOn = true; importBackup(t); return [JSON.parse(t).settings.sinceLastLook, was, state.lastLookOn, document.getElementById('setLastLook').checked]; }),
    [false, false, false, false], 'B1 off is part of a backup and comes back');

  // ---- N. on again ----
  await p.evaluate(() => { showApp('settings'); document.getElementById('secStart').open = true; }); await settle();
  await p.focus('#setLastLook'); await p.keyboard.press('Space'); await settle();   // by keyboard: Today may lie over Settings
  eq(await p.textContent('#lastLookStatus'), GOLD.on, 'N1 turning it on says when the first look is kept');
  await p.evaluate(() => { closeApp('settings'); openToday(); }); await settle();
  let t = await listText();
  eq([t[0], t[1]], ['SINCE YOU LAST LOOKED', 'From now on, what moves in your data between two looks is listed here.'], 'N2 Today starts again from nothing: no earlier look, not even the demo\'s');
  await p.evaluate(() => closeApp('today')); await p.evaluate(() => openToday()); await settle(); t = await listText();
  eq(t[2], 'Nothing has moved since you last looked.', 'N3 closing Today keeps a look again');
  await p.evaluate(() => closeApp('today'));

  // ---- R. added after the review of round 37 ----
  eq(await p.evaluate(() => { state.lastLookOn = true; state.lastLook = lookNow(DATA, nowMs()); const t = JSON.parse(backupText()); t.settings.sinceLastLook = false; importBackup(JSON.stringify(t)); return [state.lastLookOn, state.lastLook]; }),
    [false, null], 'R1 importing a backup set to off removes what was kept');
  eq(await p.evaluate(() => { const t = JSON.parse(backupText()); delete t.settings.sinceLastLook; importBackup(JSON.stringify(t)); return [state.lastLookOn, document.getElementById('setLastLook').checked, document.getElementById('lastLookStatus').textContent]; }),
    [false, false, ''], 'R2 a backup from before round 37 keeps the choice the person has now, as the app does, and the status does not contradict the box');
  eq(await p.evaluate(() => { state.lastLookOn = true; state.lastLook = lookNow(DATA, nowMs()); const b = document.getElementById('bkReset'); b.click(); b.click(); return [state.lastLook, state.lastLookOn]; }),
    [null, true], 'R3 Reset (pressed twice, as asked) removes what was kept, as the app does, and the switch is on again');
  // Today opened while off, then turned on: closing it keeps a look
  await p.evaluate(() => { state.lastLookOn = false; state.lastLook = null; syncSettingsUI(); openToday(); showApp('settings'); document.getElementById('secStart').open = true; }); await settle();
  await p.focus('#setLastLook'); await p.keyboard.press('Space'); await settle();
  ok((await listText()).includes('SINCE YOU LAST LOOKED'), 'R4 turned on while Today is open, the group comes back at once');
  await p.evaluate(() => { closeApp('settings'); closeApp('today'); }); await settle();
  ok(await p.evaluate(() => !!(state.lastLook && state.lastLook.items)), 'R5 closing that Today keeps a look');
  eq(await p.evaluate(() => document.getElementById('setLastLook').getAttribute('aria-describedby')), 'lastLookHint', 'R6 the hint is read with the switch');

  // ---- F. what changed ----
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 37).items, APP_VERSION]), [GOLD.changes.items, V29], 'F1 what changed in round 37 is listed, and the version is the one the app is held to');
  eq(GOLD.backupKey, 'sinceLastLook', 'F2 the backup names the switch');
  eq(errs, [], 'F3 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
