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
  const page = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  await page.evaluate(() => { const r = document.querySelector('input[name=consent][value=whisper]'); r.checked = true; r.dispatchEvent(new Event('change')); });   // round 34: the shortcut opens the card by default; this suite checks the whisper path, so it chooses Whisper
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0).catch(() => false);
  const items = async () => page.$$eval('#ctxmenu button', bs => bs.map(b => b.textContent));
  const rightClick = async sel => { await page.click(sel, { clickCount: 2 }); await page.click(sel, { button: 'right' }); };

  // urgent ANDA: a peek by itself
  await rightClick('#webBody [data-pick="216482"]');
  ok(await vis('#ctxmenu'), 'right-click in the Browser window shows the extension menu');
  const it = await items();
  ok(it.some(x => x.startsWith('ANDA Companion (Notula): peek at “216482”')) && it.includes('ANDA Companion (Notula): check this text as a draft') && it.includes('ANDA Companion (Notula): scan this page for ANDA numbers'), 'the menu offers peek, check as a draft and scan: ' + JSON.stringify(it));
  await page.screenshot({ path: OUT + '/s12-ctx-menu.png' });
  await page.click('#ctxmenu button:has-text("peek at")');
  await page.waitForTimeout(300);
  ok(await vis('#card') && await page.$eval('#card', e => e.classList.contains('mini')), 'peek at the selection: the at-risk ANDA opens a peek by itself (whisper default + urgent rule)');

  await page.keyboard.press('Escape');

  // healthy ANDA: whisper invite
  await rightClick('#webBody [data-pick="214990"]');
  await page.click('#ctxmenu button:has-text("peek at")');
  await page.waitForTimeout(250);
  ok(await vis('#chip') && (await page.textContent('#chipTxt')) === 'ANDA 214990' && !(await vis('#card')), 'a healthy ANDA gets the small invite');
  await page.keyboard.press('Escape');

  // an NDA and an unknown number are not guessed
  await rightClick('#webBody [data-pick="NDA 021234"]');
  await page.click('#ctxmenu button:has-text("peek at")');
  ok((await page.textContent('#chipTxt')).startsWith('No ANDA number in the selection'), 'NDA 021234: no ANDA number, no card');
  await page.keyboard.press('Escape');
  await rightClick('#webBody [data-pick="999999"]');
  await page.click('#ctxmenu button:has-text("peek at")');
  ok((await page.textContent('#chipTxt')).includes('ANDA 999999 is not in your data'), 'an unknown six-digit number is said plainly');
  await page.keyboard.press('Escape');

  // no selection: only the scan item
  await page.evaluate(() => getSelection().removeAllRanges());
  await page.click('#webBody h2', { button: 'right' });
  const none = await items();
  ok(!none.some(x => x.includes('peek at')) && none.includes('ANDA Companion (Notula): scan this page for ANDA numbers'), 'with nothing selected only "scan this page" is offered');

  // scan this page
  await page.click('#ctxmenu button:has-text("scan this page")');
  await page.waitForTimeout(200);
  ok(await vis('#wlist'), 'scan opens the list');
  const list = await page.textContent('#wlRows');
  ok(list.includes('216482') && list.includes('214990') && list.includes('999999') && list.includes('Not in your data') && !list.includes('021234'), 'it lists ANDA numbers on the page, marks the unknown one, and never the NDA number');
  await page.screenshot({ path: OUT + '/s13-scan.png' });
  await page.click('#wlRows .row:has-text("216482")');
  await page.waitForTimeout(250);
  ok(await vis('#card') && (await page.textContent('#c-id')).includes('216482'), 'a row opens the card');
  await page.keyboard.press('Escape');

  // check as a draft from the menu
  await page.click('[data-pickbtn="DRAFT"]');
  await page.dblclick('#mailBody [data-pick="DRAFT"]');
  // the draft menu item exists only in the Browser window, so select text there: use the portfolio row text
  await page.evaluate(() => { const el = document.querySelector('#webBody td'); const r = document.createRange(); r.selectNodeContents(el.closest('tr')); const s = getSelection(); s.removeAllRanges(); s.addRange(r); });
  await page.click('#webBody tr td', { button: 'right' });
  await page.click('#ctxmenu button:has-text("as a draft")');
  await page.waitForTimeout(250);
  ok(await vis('#draft'), 'check as a draft opens the draft window with the selection');
  await page.keyboard.press('Escape');

  // the app's Watch list: since round 34 it opens from Today (a click on the tray icon), with Show all
  await page.click('#chev'); await page.click('#pinBtn2');
  await page.click('#appIcon'); await page.waitForTimeout(150);
  await page.click('#tdAll');
  await page.waitForTimeout(200);
  const rows = await page.$$eval('#wlRows .row .n', ns => ns.map(n => n.textContent));
  ok(rows[0] === '216482' && rows.length === 2, 'tray: the Watch list leads with the at-risk ANDA: ' + JSON.stringify(rows));
  ok((await page.textContent('#wlNote')).includes('2 of 2 ANDAs'), 'and says how many have something worth a look');
  await page.screenshot({ path: OUT + '/s14-watchlist.png' });
  await page.keyboard.press('Escape');
  ok(!(await vis('#wlist')), 'Escape closes it');

  // the menu is only in the Browser window
  await page.click('#mailBody', { button: 'right' });
  ok(!(await vis('#ctxmenu')), 'right-click in the Mail window does not show the extension menu (it works in Edge pages only)');

  console.log('errors:', JSON.stringify(errors));
  console.log(fails ? `FAILED: ${fails}` : 'ALL PASS');
  await browser.close();
})();
