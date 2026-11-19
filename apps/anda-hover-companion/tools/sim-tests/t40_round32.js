// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 32 in the browser simulation. Part B: a guide of five short steps that opens once at the first start, can be left at any step and
// is opened again from the tray menu. Part A: the Drugs@FDA reference file says when it was saved and how long ago, and is marked STALE
// after a number of days of its own (45 unless the person changes it), separate from the days for the person's own data.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async () => {
    const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300); return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE_FILE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(document.querySelector(s))).violations.map(v => v.id + ': ' + v.nodes.length), sel); };
  const rld = p => p.evaluate(() => [document.getElementById('c-rld').textContent, document.getElementById('c-rld').classList.contains('stale')]);
  const card = async p => { await p.evaluate(() => { APP_WINS.forEach(closeApp); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await settle(p, 400); return rld(p); };
  const look = p => p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; });

  // ====================================================== A. The reference file's own days
  let p = await fresh();
  eq(await p.evaluate(() => [REF_STALE_CHOICES, REF_STALE_DEFAULT, state.refStaleDays, STALE_DEFAULT]), [[14, 30, 45, 60, 90], 45, 45, 7], 'A1 the reference file is stale after 45 days unless another number is chosen; my own data keeps its 7');
  eq((await card(p))[1], false, 'A2 with no reference file chosen nothing is called stale');
  await p.evaluate(() => { showApp('settings'); document.getElementById('dataRefPick').click(); }); await settle(p);
  eq(await p.evaluate(() => document.getElementById('dataRefStatus').textContent), 'Drugs@FDA file loaded (simulated: 1 reference product, file saved Jun 28, 2026 (18 days ago)).', 'A3 Settings says when the file was saved, with the year and how long ago, in the words of the card');
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago)', false], 'A4 the card says where the labeling date is from, when that file was saved and how long ago');
  await p.evaluate(() => document.getElementById('swOldRef').click()); await settle(p);
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved May 17, 2026 (60 days ago) · STALE: older than 45 days', true], 'A5 a file older than its days is marked STALE, in words and by its look');
  eq(await p.evaluate(() => [dataLine().stale, watchFlags('216482').some(f => f.code === 'stale-data')]), [false, false], 'A6 my own data is not called stale because the reference file is');
  await look(p); await p.selectOption('#setRefStale', '90'); await settle(p);
  eq([await p.evaluate(() => document.getElementById('setTimeHint').textContent), await p.evaluate(() => [state.refStaleDays, state.staleDays])], ['A Drugs@FDA file older than 90 days is now marked STALE.', [90, 7]], 'A7 the number is the person' + "'" + 's choice, said plainly, and leaves the days for my data alone');
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved May 17, 2026 (60 days ago)', false], 'A8 with 90 days chosen the same file is not stale');
  await look(p); await p.selectOption('#setRefStale', '14'); await settle(p);
  await p.evaluate(() => document.getElementById('swOldRef').click()); await settle(p);
  eq(await card(p), ['Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago) · STALE: older than 14 days', true], 'A9 with 14 days chosen a file of 18 days is stale');
  await look(p); await p.selectOption('#setTz', 'UTC'); await settle(p);
  eq((await card(p))[0], 'Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago) · STALE: older than 14 days', 'A10 the day is the day in the zone in use');
  await look(p); await p.selectOption('#setTz', ''); await p.selectOption('#setRefStale', '45'); await settle(p);
  // the open card keeps step: a day passes and nobody calls anything but the minute tick
  await card(p);
  eq(await p.evaluate(() => { window.__now = FIXED_NOW + 86400000 + 60000; timeTick(); const t = document.getElementById('c-rld').textContent; delete window.__now; timeTick(); return t; }), 'Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (19 days ago)', 'A11 on an open card the age moves with the clock');
  // settings: carried by a backup; a value that is not a choice becomes 45
  eq(await p.evaluate(() => { state.refStaleDays = 90; const x = JSON.parse(backupText()); const carried = x.settings.refStaleAfterDays; x.settings.refStaleAfterDays = 7; importBackup(JSON.stringify(x)); const a = state.refStaleDays; x.settings.refStaleAfterDays = 60; importBackup(JSON.stringify(x)); const b2 = state.refStaleDays; state.refStaleDays = 45; return [carried, a, b2, document.getElementById('setRefStale').value]; }),
    [90, 45, 60, '60'], 'A12 a settings backup carries the number; 7, which is a choice for my data only, becomes 45');
  eq(await p.evaluate(() => [refSaved(null, 5, 45, 'UTC'), refSaved(1e20, 5, 45, 'UTC'), refSaved(Date.UTC(2026, 10, 19, 21), Date.UTC(2026, 10, 19, 18), 45, 'UTC')].map(x => [x.text, x.stale])),
    [['saved: time not known', false], ['saved: time not known', false], ["saved Nov 19, 2026 (later than now: please check this computer's clock)", false]], 'A13 a file whose time is not known, or is later than now, is not called stale and says why');
  await look(p); await settle(p);
  eq(await axe(p, '#secLook'), [], 'A14 accessibility scan of the part of Settings that holds the three boxes');
  eq(await p.evaluate(() => document.querySelector('label[for=setRefStale]').textContent), 'Mark the Drugs@FDA reference file as stale when it is older than', 'A15 the new box has a label of its own');
  await p.close();
  // a phone-sized window
  { const ctx = await b.newContext({ viewport: { width: 390, height: 780 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.goto(SIM); await q.waitForTimeout(300); await look(q); await settle(q, 250);
    eq(await q.evaluate(() => { const w = document.getElementById('settings'); return [document.documentElement.scrollWidth <= innerWidth + 1, w.scrollWidth <= w.clientWidth + 1, document.getElementById('setRefStale').getBoundingClientRect().right <= innerWidth]; }), [true, true, true], 'A16 nothing runs off the side at phone width');
    await q.close(); }

  // ====================================================== B. First steps: a guide of five steps
  const open = async (query, init) => {
    const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    if (init) await ctx.addInitScript(init);
    const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    q.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await q.goto(SIM + query); await q.waitForTimeout(350); return q;
  };
  const shown = q => q.evaluate(() => { const w = document.getElementById('first'); return w.style.display === 'block' ? [document.getElementById('fsPlace').textContent, document.getElementById('fsTitle').textContent] : null; });
  p = await open('');
  eq(await shown(p), null, 'B1 the suites written before the guide start as they always did: under automation it does not open by itself');
  await p.close();
  // a real first start: a browser that is not driven, with nothing remembered
  p = await open('', () => { Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false }); });
  eq(await shown(p), ['Step 1 of 5', 'What this is'], 'B2 at the first start the guide opens by itself, at its first step');
  eq(await p.evaluate(() => [document.activeElement.id, document.getElementById('first').getAttribute('role'), document.getElementById('first').getAttribute('aria-labelledby'), localStorage.getItem('anda-first-steps-seen')]), ['fsNext', 'dialog', 'fsTitle', '1'], 'B3 the keyboard is on Next, the window is named by its title, and the PC remembers that the guide was shown');
  await p.reload(); await p.waitForTimeout(350);
  eq(await shown(p), null, 'B4 at the next start it does not open by itself again');
  await p.close();
  p = await open('&first=0', () => { Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false }); });
  eq(await shown(p), null, 'B5 an address that says first=0 keeps it shut');
  await p.close();

  p = await open('&first=1');
  eq(await shown(p), ['Step 1 of 5', 'What this is'], 'B6 with first=1 a suite gets the first start');
  eq(await p.evaluate(() => [[...document.querySelectorAll('#fsLines p')].map(x => x.textContent), document.getElementById('fsActRow').hidden, document.getElementById('fsBack').hidden, document.getElementById('fsNext').textContent, document.getElementById('fsLeave').textContent]),
    [['ANDA Companion (Notula) shows a short card for an ANDA number you select in an email or a document: the goal date, the forecast, what needs you, and your notes.', 'It starts with sample data so that you can try it. Every sample record is fictional, and its card says so.', 'Nothing leaves this PC unless you switch a connection on, or press a button that says it goes online.'], true, true, 'Next', 'Skip the guide'],
    'B7 step 1 says what the tool is, that the sample data is fictional, and that nothing leaves the PC; it can be left at once');
  eq(await axe(p, '#first'), [], 'B8 accessibility scan of the guide');
  eq(await p.evaluate(() => ['fsActRow', 'fsAct', 'fsBack'].map(id => document.getElementById(id).getClientRects().length)), [0, 0, 0], 'B8b at step 1 the button of a later step and Back are not on the screen at all');
  const titles = [];
  for (let i = 0; i < 4; i++) { await p.keyboard.press('Enter'); await settle(p, 80); titles.push((await shown(p)).join(': ')); }
  eq(titles, ['Step 2 of 5: Try it on a sample', 'Step 3 of 5: Use your own data', 'Step 4 of 5: What is switched on', 'Step 5 of 5: Where things are'], 'B9 Enter on Next walks the five steps, and no more than five');
  eq(await p.evaluate(() => [document.getElementById('fsNext').textContent, document.getElementById('fsLeave').textContent, document.getElementById('fsBack').hidden, document.getElementById('fsSay').textContent, document.activeElement.id, GUIDE_STEPS.length]), ['Done', 'Close', false, 'Step 5 of 5: Where things are', 'fsNext', 5],
    'B10 the last step says Done and Close; each step is announced; the keyboard stays on Next');
  await p.click('#fsBack'); await settle(p, 80);
  eq([await shown(p), await p.evaluate(() => document.activeElement.id)], [['Step 4 of 5', 'What is switched on'], 'fsBack'], 'B11 Back goes back, and the keyboard stays on Back');
  eq(await p.evaluate(() => document.querySelector('#fsLines p').textContent), "Settings and the app's other large windows say at their foot what is switched on. At the moment: Offline · Local only.", 'B12 step 4 says what is switched on now');
  await p.evaluate(() => { document.getElementById('swAi').click(); }); await settle(p, 150);
  await p.click('#fsBack'); await p.click('#fsNext'); await settle(p, 80);
  ok(/^Settings and the app's other large windows say at their foot what is switched on\. At the moment: Switched on: .+\.$/.test(await p.evaluate(() => document.querySelector('#fsLines p').textContent)), 'B13 and follows what is switched on: ' + await p.evaluate(() => document.querySelector('#fsLines p').textContent));
  await p.evaluate(() => { document.getElementById('swAi').click(); }); await settle(p, 100);
  await p.click('#fsAct'); await settle(p, 200);
  eq(await p.evaluate(() => [document.getElementById('conn').style.display, document.getElementById('first').style.display, document.getElementById('fsStatus').textContent]), ['block', 'block', '“Connections and event log” was opened, in front of this guide.'], 'B14 its button opens Connections and says so; the guide stays open');
  await p.evaluate(() => closeApp('conn')); await p.click('#fsBack'); await settle(p, 80);
  await p.click('#fsAct'); await settle(p, 200);
  eq(await p.evaluate(() => [document.getElementById('settings').style.display, document.getElementById('secFiles').open, document.getElementById('fsStatus').textContent]), ['block', true, 'Settings was opened at “My files and writing”, in front of this guide.'], 'B15 step 3 opens Settings where the file is chosen');
  await p.evaluate(() => closeApp('settings')); await p.click('#fsBack'); await settle(p, 80);
  eq(await p.evaluate(() => document.querySelector('#fsLines p').textContent), 'Select an ANDA number in any window, then press Ctrl+Alt+A. Or press the button below to open the card of the sample ANDA 216482.', 'B16 step 2 names the person' + "'" + 's own shortcut');
  await p.click('#fsAct'); await settle(p, 400);
  eq(await p.evaluate(() => [document.getElementById('card').style.display, document.getElementById('c-fresh').textContent.startsWith('Fictional demo data'), /fictional/i.test(document.getElementById('card').textContent), document.getElementById('first').style.display, document.getElementById('fsStatus').textContent]),
    ['block', true, true, 'block', 'The card of the sample ANDA 216482 was opened. It is fictional, and its card says so. Esc closes it.'], 'B17 its button opens the sample card, which says it is fictional sample data; the guide stays and says what happened');
  await p.evaluate(() => closeCard()); await settle(p, 100);
  await p.click('#fsLeave'); await settle(p, 100);
  eq(await shown(p), null, 'B18 "Skip the guide" leaves it at any step');
  // from Help, as often as wanted
  await p.evaluate(() => openMenu()); await settle(p, 100);
  ok(await p.evaluate(() => [...document.querySelectorAll('#menu button')].some(x => x.textContent.startsWith('Help: first steps…'))), 'B19 the tray menu offers "Help: first steps…"');
  await p.evaluate(() => [...document.querySelectorAll('#menu button')].find(x => x.textContent.startsWith('Help: first steps…')).click()); await settle(p, 150);
  eq(await shown(p), ['Step 1 of 5', 'What this is'], 'B20 which opens the guide again, at its first step');
  for (let i = 0; i < 5; i++) { await p.click('#fsNext'); await settle(p, 60); }
  eq(await shown(p), null, 'B21 Done on the last step closes it');
  await p.evaluate(() => openFirst()); await settle(p, 100); await p.keyboard.press('Escape'); await settle(p, 100);
  eq(await shown(p), null, 'B22 Esc closes it as it closes the other windows');
  eq(await p.evaluate(() => { state.keys.peek = ''; openFirst(); fsGo(1); const t = document.querySelector('#fsLines p').textContent; closeApp('first'); state.keys.peek = 'Ctrl+Alt+A'; return t; }), 'Select an ANDA number in any window, then press the shortcut you chose in Settings. Or press the button below to open the card of the sample ANDA 216482.', 'B23 with no shortcut set the sentence still reads');
  eq(await p.evaluate(() => { const b = JSON.parse(backupText()); return ['firstStepsSeen' in b.settings, JSON.stringify(b).includes('first-steps')]; }), [false, false], 'B24 that the guide was shown belongs to this PC: a settings backup does not carry it');
  await p.close();
  { const ctx = await b.newContext({ viewport: { width: 390, height: 780 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.goto(SIM + '&first=1'); await q.waitForTimeout(350);
    for (let i = 0; i < 3; i++) { await q.click('#fsNext'); await q.waitForTimeout(60); }
    eq(await q.evaluate(() => { const w = document.getElementById('first'), r = w.getBoundingClientRect(); return [document.documentElement.scrollWidth <= innerWidth + 1, w.scrollWidth <= w.clientWidth + 1, r.left >= 0 && r.right <= innerWidth + 1, [...w.querySelectorAll('button')].filter(x => x.getClientRects().length).every(x => x.getBoundingClientRect().height >= 24)]; }), [true, true, true, true], 'B25 at phone width the guide fits and its buttons can be pressed');
    await q.close(); }

  // ====================================================== R. What the separate review found
  p = await open('&first=1');
  await p.click('#fsNext'); await settle(p, 80); await p.click('#fsAct'); await settle(p, 400);
  eq(await p.evaluate(() => [current, document.getElementById('card').classList.contains('mini'), sampleCard, document.getElementById('compact').style.display !== 'block']), ['216482', false, true, true], 'R1 the button opens the full card of the sample 216482, marked as the sample');
  await p.keyboard.press('Escape'); await settle(p, 150);
  eq(await p.evaluate(() => [document.getElementById('card').style.display, document.getElementById('first').style.display]), ['none', 'block'], 'R2 "Esc closes it": Esc closes the sample card, not the guide');
  await p.keyboard.press('Escape'); await settle(p, 150);
  eq(await shown(p), null, 'R3 and a second Esc closes the guide');
  // a compact look is chosen: the guide still opens the full card
  await p.evaluate(() => { state.form = 'strip'; openFirst(); fsGo(1); fsAct(); }); await settle(p, 400);
  eq(await p.evaluate(() => [document.getElementById('card').style.display, document.getElementById('compact').style.display !== 'block']), ['block', true], 'R4 with a compact look chosen the guide still opens the full card');
  await p.evaluate(() => { closeCard(); state.form = 'full'; }); await settle(p);
  // the person's own file is chosen: the sample card still reads as sample data, and their own card as theirs
  await p.evaluate(() => { showApp('settings'); document.getElementById('dataPortfolioPick').click(); closeApp('settings'); fsFront(); fsAct(); }); await settle(p, 400);
  ok((await p.evaluate(() => document.getElementById('c-fresh').textContent)).startsWith('Fictional demo data · extract #412 · as of '), 'R5 with my own file chosen, the sample card still says it is fictional sample data');
  await p.evaluate(() => { closeCard(); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await settle(p, 400);
  ok((await p.evaluate(() => document.getElementById('c-fresh').textContent)).startsWith('YOUR FILE (simulated) · my-portfolio.xlsx · as of '), 'R6 and a card opened any other way says it is from my file');
  await p.evaluate(() => { closeCard(); state.data.portfolio = null; paintData(); }); await settle(p);
  // what the guide opens is in front of the guide
  await p.evaluate(() => { fsGo(1); fsGo(1); fsAct(); }); await settle(p, 250);
  eq(await p.evaluate(() => { const z = id => parseInt(getComputedStyle(document.getElementById(id)).zIndex, 10), r = document.querySelector('#conn h2').getBoundingClientRect(), top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return [z('first') < z('conn'), !!top && !!top.closest('#conn')]; }), [true, true], 'R7 Connections opens in front of the guide: its heading is not covered');
  await p.evaluate(() => { closeApp('conn'); document.getElementById('first').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true })); }); await settle(p);
  eq(await p.evaluate(() => getComputedStyle(document.getElementById('first')).zIndex), '56', 'R8 a press on the guide brings it forward again');
  // Help while the guide is open keeps its place
  await p.evaluate(() => openFirst()); await settle(p);
  eq(await shown(p), ['Step 4 of 5', 'What is switched on'], 'R9 "Help: first steps" while the guide is open keeps its place');
  // a shortcut another program holds is not taught
  eq(await p.evaluate(() => { state.taken.peek = true; fsGo(-1); fsGo(-1); const t = document.querySelector('#fsLines p').textContent; state.taken.peek = false; return t; }), 'Select an ANDA number in any window, then press your shortcut (Ctrl+Alt+A is used by another program at the moment: please choose another in Settings). Or press the button below to open the card of the sample ANDA 216482.', 'R10 step 2 does not teach a shortcut that another program holds');
  await p.evaluate(() => fsClose()); await settle(p);
  // the reference file: Settings and the card name one day and one age; STALE is said in Settings as well
  await p.evaluate(() => { showApp('settings'); document.getElementById('dataRefPick').click(); document.getElementById('secLook').open = true; }); await p.selectOption('#setTz', 'Pacific/Honolulu'); await settle(p);
  const st = await p.evaluate(() => document.getElementById('dataRefStatus').textContent); const cardLine = (await card(p))[0];
  ok(st.includes(cardLine.slice(cardLine.indexOf('saved '))) && /file saved Jun 27, 2026 \(18 days ago\)/.test(st), 'R11 after a change of zone Settings and the card name the same day for the file: ' + st);
  await p.evaluate(() => { showApp('settings'); document.getElementById('swOldRef').click(); }); await settle(p);
  ok(/\(60 days ago\) · STALE: older than 45 days\)\.$/.test(await p.evaluate(() => document.getElementById('dataRefStatus').textContent)), 'R12 Settings says STALE of an old reference file as well');
  await p.evaluate(() => { document.getElementById('swOldRef').click(); document.getElementById('secLook').open = true; }); await p.selectOption('#setTz', ''); await settle(p);
  // a backup written before the three settings existed leaves them as they are
  eq(await p.evaluate(() => { state.tz = 'America/Denver'; state.staleDays = 14; state.refStaleDays = 90; const x = JSON.parse(backupText()); delete x.settings.timeZone; delete x.settings.staleAfterDays; delete x.settings.refStaleAfterDays; importBackup(JSON.stringify(x)); const r = [state.tz, state.staleDays, state.refStaleDays]; state.tz = ''; state.staleDays = 7; state.refStaleDays = 45; return r; }), ['America/Denver', 14, 90], 'R13 a backup from before these settings existed does not set them back');
  await p.close();
  // the sample card stays: it does not hide itself after ten seconds while the guide says it was opened
  { const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.clock.install({ time: new Date('2026-07-16T14:42:00Z') }); await q.goto(SIM + '&first=1'); await q.clock.runFor(400);
    await q.evaluate(() => { fsGo(1); fsAct(); }); await q.clock.runFor(15000);
    eq(await q.evaluate(() => [state.autoHide, document.getElementById('card').style.display]), [10, 'block'], 'R14 the sample card is still there after fifteen seconds, with auto-hide at its usual ten');
    await q.evaluate(() => { closeCard(); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await q.clock.runFor(15000);
    eq(await q.evaluate(() => document.getElementById('card').style.display), 'none', 'R15 (the control: a card opened any other way does hide itself)');
    await q.close(); }
  // a browser that refuses to remember: the guide says so
  p = await open('', () => { Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false }); Storage.prototype.setItem = function () { throw new DOMException('refused', 'QuotaExceededError'); }; });
  eq([await shown(p), await p.evaluate(() => document.getElementById('fsStatus').textContent)], [['Step 1 of 5', 'What this is'], 'This PC did not let the app remember that the guide was shown, so it will open again at the next start. You can also open it from the tray menu: Help: first steps.'], 'R16 where the browser cannot remember, the guide says that it will open again');
  await p.keyboard.press('Escape'); await settle(p, 150);
  eq(await p.evaluate(() => [document.getElementById('first').style.display, document.activeElement !== document.body && document.activeElement !== null]), ['none', true], 'R17 closing the guide at a first start does not leave the keyboard on nothing');
  await p.close();
  // phone width: what the guide opens can be used
  { const ctx = await b.newContext({ viewport: { width: 390, height: 780 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.goto(SIM + '&first=1'); await q.waitForTimeout(350);
    await q.evaluate(() => { fsGo(1); fsGo(1); fsAct(); }); await q.waitForTimeout(300);
    eq(await q.evaluate(() => { const bn = document.getElementById('dataPortfolioPick'); bn.scrollIntoView({ block: 'center' }); const r = bn.getBoundingClientRect(), top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return !!top && (top === bn || bn.contains(top)); }), true, 'R18 at phone width the button that chooses the file is not covered by the guide');
    await q.close(); }

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
