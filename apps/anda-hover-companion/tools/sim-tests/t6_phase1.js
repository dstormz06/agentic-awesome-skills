// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const OUT = process.argv[2] || TMP;
let fails = 0;
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0).catch(() => false);
  const inside = async sel => page.$eval(sel, e => { const r = e.getBoundingClientRect(); const tb = document.getElementById('taskbar').getBoundingClientRect(); return r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= tb.top + 1; });

  // ---- the calendar port against OPM's published 2026-2027 federal holidays (same dates as the C# test) ----
  const cal = await page.evaluate(() => {
    const d = (y, m, dd) => ymd(y, m, dd);
    const closed = [[2026,1,1],[2026,7,3],[2026,11,11],[2026,12,25],[2027,1,1],[2027,6,18],[2027,7,5],[2027,11,11],[2027,12,24]];
    return {
      closed: closed.map(([y, m, dd]) => [y, m, dd, kindOf(d(y, m, dd))]),
      sat: kindOf(d(2026, 10, 3)), nextAfterSat: shortDate(nextBiz(d(2026, 10, 3))), prevBeforeSat: shortDate(prevBiz(d(2026, 10, 3))),
      columbus: holidayOn(d(2026, 10, 12)) && holidayOn(d(2026, 10, 12)).name,
      nearFri: evaluateDate(d(2026, 10, 9), 3), nearTue: evaluateDate(d(2026, 10, 13), 3), farWeek: evaluateDate(d(2026, 9, 22), 3), lead0: evaluateDate(d(2026, 10, 9), 0),
    };
  });
  ok(cal.closed.every(c => c[3] === 'holiday'), 'federal holidays (incl. observed Fri/Mon) are closed days: ' + JSON.stringify(cal.closed.filter(c => c[3] !== 'holiday')));
  ok(cal.sat === 'weekend' && cal.nextAfterSat === 'Mon Oct 5' && cal.prevBeforeSat === 'Fri Oct 2', 'Sat Oct 3, 2026 is a weekend; nearest business days Fri Oct 2 and Mon Oct 5');
  ok(cal.columbus === 'Columbus Day', 'Mon Oct 12, 2026 is Columbus Day');
  ok(cal.nearFri.near === 1 && cal.nearFri.before === true && cal.nearFri.name === 'Columbus Day', 'Fri Oct 9 is 1 business day before Columbus Day');
  ok(cal.nearTue.near === 1 && cal.nearTue.before === false, 'Tue Oct 13 is 1 business day after Columbus Day');
  ok(cal.farWeek.near === null && cal.farWeek.kind === 'business', 'a plain week is not flagged');
  ok(cal.lead0.near === null, 'a holiday window of 0 flags only weekends and holidays themselves');

  // ---- WATCH on the card ----
  await page.evaluate(() => { const r = document.querySelector('input[name=consent][value=whisper]'); r.checked = true; r.dispatchEvent(new Event('change')); });   // round 34: Full is the default; the lookup check below is about whisper mode
  await page.click('#chev'); await page.click('#pinBtn2');
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Lookup ANDA…');
  await page.fill('#lookupIn', '216482'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  ok(await vis('#card') && !(await page.$eval('#card', e => e.classList.contains('mini'))), 'lookup (typing the number) opens the full card even in whisper mode');
  ok(await vis('#watchSlot'), 'the full card shows a WATCH line');
  const w1 = await page.textContent('#c-watch');
  ok(w1 === 'Goal date falls on a weekend: Sat Oct 3. Nearest business days: Fri Oct 2 and Mon Oct 5.', 'WATCH says the goal date is on a weekend: ' + w1);
  ok((await page.textContent('#c-watch-more')) === '(+1 more)', 'WATCH says one more flag is in the drawer (round 34: shorter tail)');

  await page.screenshot({ path: OUT + '/s9-watch-card.png' });
  await page.click('#expBtn'); await page.waitForTimeout(250);
  await page.click('#x-watch-d summary'); await page.waitForTimeout(150);
  const drawer = await page.textContent('#x-watch');
  ok((await page.textContent('#x-watch-n')).trim() === '· 2', 'drawer header counts 2 flags');
  ok(drawer.includes('Forecast date falls on a weekend: Sat Oct 17') && drawer.includes('Source: U.S. federal calendar (OPM)') && drawer.includes('this tool does not decide'), 'drawer lists the evidence, the source and that the tool does not decide');
  ok(await inside('#card'), 'the expanded card with the watch list stays on screen');
  await page.screenshot({ path: OUT + '/s10-watch-drawer.png' });
  await page.keyboard.press('Escape');

  // the healthy one has just its goal flag; the clock what-if turns a near date into the top flag
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', '214990'); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
  ok((await page.textContent('#c-watch')).startsWith('Goal date falls on a weekend: Sat Aug 15') && (await page.textContent('#c-watch-more')) === '', 'healthy ANDA: one weekend flag, nothing else');
  await page.keyboard.press('Escape');
  await page.check('#swToday');
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', '216482'); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
  ok((await page.textContent('#c-watch')) === 'The goal date (Sat Oct 3) is in 2 days.', 'pretend today is Oct 1: "goal date in 2 days" leads: ' + (await page.textContent('#c-watch')));
  await page.keyboard.press('Escape');
  await page.uncheck('#swToday');

  // ---- whisper (a choice since round 34): urgent auto-peek; healthy ANDA gets the invite ----
  await page.evaluate(() => { const r = document.querySelector('input[name=consent][value=whisper]'); r.checked = true; r.dispatchEvent(new Event('change')); });
  await page.dblclick('[data-pick="214990"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(300);
  ok(await vis('#chip') && (await page.textContent('#chipTxt')) === 'ANDA 214990' && !(await vis('#card')), 'whisper: the healthy ANDA gets the small invite');

  await page.click('#chip'); await page.waitForTimeout(250);
  ok(await vis('#card') && !(await page.$eval('#card', e => e.classList.contains('mini'))), 'clicking the invite opens the full card');
  await page.keyboard.press('Escape');
  const urgency = await page.evaluate(() => [isUrgent('216482'), isUrgent('214990'), firstStep('216482'), firstStep('214990')]);
  ok(JSON.stringify(urgency) === '[true,false,"peek","invite"]', 'urgency port: ' + JSON.stringify(urgency));

  // ---- draft check ----
  await page.click('[data-pickbtn="DRAFT"]');
  await page.keyboard.press('Control+Alt+Shift+KeyR'); await page.waitForTimeout(400);
  ok(await vis('#draft'), 'Ctrl+Alt+Shift+R opens the draft window');
  ok((await page.inputValue('#drText')).startsWith('Hi all, quick status. ANDA 216482 is on track'), 'the selected draft is carried into the window');

  const out = await page.textContent('#drOut');
  ok(out.includes('ANDA 216482: the draft gives the goal date as Oct 10, 2026, but the record says Oct 3, 2026.'), 'flags the wrong goal date');
  ok(!out.includes('the draft gives the forecast date'), 'the right forecast date is not flagged');
  ok(out.includes('ANDA 216482: the draft sounds on track, but the record\'s forecast is 14 days later than the goal.'), 'flags "on track" against a late forecast');
  ok(out.includes('ANDA 214990: the draft sounds late or at risk, but the record\'s forecast is 9 days ahead of the goal.'), 'flags "late" against an early forecast');
  ok(out.includes('ANDA 123456 is not in your data'), 'flags an ANDA that is not in the data');
  ok(out.includes('is not the system of record'), 'always says what it did not check');
  ok(await page.$$eval('#draft [data-ai]', bs => bs.every(b => b.disabled)), 'Proofread / Rewrite / Fact-check are off until a model is approved');
  await page.screenshot({ path: OUT + '/s11-draft.png' });
  ok(await inside('#draft'), 'the draft window is on screen');
  await page.fill('#drText', 'ANDA 216482 goal date is Oct 3, 2026.'); await page.waitForTimeout(450);
  { const o = await page.textContent('#drOut'); ok(o.includes('Checked 1 ANDA number, 1 goal or forecast date against the record') && o.includes('nothing found that needs a second look') && o.includes('Not checked: grammar and wording beyond the short list of rules under Proofreading'), 'a correct draft says what was checked and what was not'); }
  await page.fill('#drText', 'Nothing about any application here.'); await page.waitForTimeout(450);
  ok((await page.textContent('#drOut')).includes('No ANDA numbers found in the draft'), 'no number means nothing is claimed');
  await page.fill('#drText', ''); await page.waitForTimeout(450);
  ok((await page.textContent('#drOut')).includes('Nothing to check yet'), 'an empty draft is refused politely');
  await page.check('#swAi'); await page.waitForTimeout(100);
  ok(await page.$$eval('#draft [data-ai]', bs => bs.every(b => !b.disabled)), 'with a model approved the three AI buttons turn on');
  await page.click('#draft [data-ai="Proofread"]');
  ok((await page.textContent('#drLocalOut')).includes('nothing leaves until you press Send') && (await page.textContent('#drLocalOut')).includes('never calls a model'), 'AI action explains the preview and that the simulation calls no model');
  await page.keyboard.press('Escape'); ok(!(await vis('#draft')), 'Esc closes the draft window');
  await page.uncheck('#swAi');

  // the same window from the tray menu
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Review a draft…'); await page.waitForTimeout(200);
  ok(await vis('#draft'), 'tray: "Review a draft…" opens it');
  await page.click('#drSample'); await page.waitForTimeout(150);
  ok((await page.textContent('#drOut')).includes('ANDA 123456 is not in your data'), 'the sample draft button fills and checks it');
  await page.click('#drDone');

  // narrow (phone) width: nothing overflows horizontally
  await page.setViewportSize({ width: 390, height: 760 });
  await page.waitForTimeout(150);
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', '216482'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'phone width: no horizontal page scroll with the WATCH line');
  await page.screenshot({ path: OUT + '/m7-watch-phone.png' });

  console.log('console/page errors:', JSON.stringify(errors));
  console.log(fails ? `FAILED: ${fails}` : 'ALL PASS');
  await browser.close();
})();
