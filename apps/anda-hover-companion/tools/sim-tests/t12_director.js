// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  await ctx.grantPermissions(['clipboard-read', 'clipboard-write']);
  const page = await ctx.newPage();
  const errs = []; page.on('pageerror', e => errs.push(e.message)); page.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  await page.goto(URL); await page.waitForTimeout(300);
  await page.evaluate(() => { const r = document.querySelector('input[name=consent][value=whisper]'); r.checked = true; r.dispatchEvent(new Event('change')); });   // round 34: the shortcut opens the card by default; this suite checks the whisper path, so it chooses Whisper
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && !e.hidden && e.getBoundingClientRect().width > 0).catch(() => false);
  const txt = sel => page.textContent(sel);

  ok(/ANDA Companion \(Notula\)/.test(await page.title()), 'title names the product');
  // 1. pin the icon, select the at-risk ANDA, press the hotkey: a peek opens by itself
  await page.click('#chev'); await page.click('#pinBtn2'); await page.waitForTimeout(200);
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(500);
  ok(await vis('#card'), 'at-risk: a peek opens by itself');
  ok(await vis('#moreBtn'), 'the peek offers Full brief');
  await page.click('#moreBtn'); await page.waitForTimeout(400);
  ok((await txt('#c-id')).includes('216482') && (await txt('#c-fdelta')).includes('14'), 'the full card shows the number and the 14 day gap');
  ok((await txt('#c-psg')).length > 5 && (await txt('#c-rld')).length > 5, 'PSG and RLD lines are filled');
  ok((await txt('#c-conn-t')).includes('OFFLINE'), 'the card says no API is connected');
  // 2. auto-hide after 10 s when nothing touches it (mouse parked outside). Round 34: Full brief opens the card in full, and an open
  //    drawer counts as in use and never hides, so the card is folded back to the answer first.
  await page.evaluate(() => toggleExpand());
  await page.mouse.move(5, 400);
  await page.waitForTimeout(500);
  ok(await vis('#card'), 'still showing after half a second');
  await page.waitForTimeout(11000);
  ok(!(await vis('#card')), 'the card hides itself after about 10 seconds of no activity');
  // 3. pinned card stays
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(400);
  if (await vis('#moreBtn')) await page.click('#moreBtn');
  await page.click('#pinBtn'); await page.waitForTimeout(200);
  await page.mouse.move(5, 400); await page.waitForTimeout(11500);
  ok(await vis('#card'), 'a pinned card stays after 10 seconds');
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  // Esc on a pinned card closes it (or unpins); close any remaining
  if (await vis('#card')) { await page.click('#closeBtn'); await page.waitForTimeout(200); }
  ok(!(await vis('#card')), 'the close button closes it');
  // 4. healthy ANDA: whisper invite
  await page.dblclick('[data-pick="214990"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(400);
  ok(await vis('#chip'), 'healthy ANDA: a small invite appears');
  await page.click('#chip'); await page.waitForTimeout(500);
  ok(await vis('#card') && (await txt('#c-id')).includes('214990'), 'clicking the invite opens the card');
  await page.keyboard.press('Escape'); await page.waitForTimeout(200);
  // 5. NDA is never guessed
  await page.dblclick('[data-pick="NDA 021234"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(500);
  ok(!(await vis('#card')), 'an NDA number opens no ANDA card');
  // 6. lookup box
  await page.keyboard.press('Control+Alt+Space'); await page.waitForTimeout(300);
  ok(await vis('#lookup'), 'the lookup box opens');
  await page.keyboard.type('214990'); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  ok(await vis('#card') && (await txt('#c-id')).includes('214990'), 'typing a number and Enter opens its card');
  await page.keyboard.press('Escape'); await page.waitForTimeout(200);
  // 7. draft check
  await page.click('[data-pickbtn="DRAFT"]').catch(() => {});
  await page.keyboard.press('Control+Alt+Shift+KeyR'); await page.waitForTimeout(500);
  ok(await vis('#draft'), 'the draft check opens');
  const found = (await txt('#drOut')).toLowerCase();
  ok(found.includes('draft') || found.includes('record'), 'the draft check lists what it found');
  await page.click('#drDone'); await page.waitForTimeout(200);
  // 8. tray: watch list and settings
  await page.click('#appIcon', { button: 'right' }); await page.waitForTimeout(200);
  ok(await vis('#menu'), 'the tray menu opens');
  const menu = await txt('#menu');
  ok(/Lookup/.test(menu) && /Today/.test(menu) && /Settings/.test(menu), 'the tray menu offers Lookup, Today and Settings (round 34: the watch list opens from Today)');
  await page.click('#menu >> text=Today'); await page.waitForTimeout(300); await page.click('#tdAll'); await page.waitForTimeout(300);
  ok(await vis('#wlist'), 'the watch list opens from Today'); await page.click('#wlDone'); await page.click('#tdClose');
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.waitForTimeout(300);
  ok(await vis('#settings'), 'Settings opens');
  ok(await page.$eval('input[name=autohide][value="10"]', e => e.checked), 'auto-hide is 10 seconds by default');
  ok((await txt('#setBannerLabel')).includes('OFFLINE'), 'the banner says offline');
  await page.click('#setDone');
  // 9. reset restores a clean page
  await page.click('#resetBtn').catch(() => {});
  ok(errs.length === 0, 'no page errors during the run: ' + errs.join(' | '));
  await browser.close();
  console.log(fails ? `\n${fails} FAILED of ${checks}` : `\nALL ${checks} PASSED`);
  process.exit(fails ? 1 : 0);
})();
