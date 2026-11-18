// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
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

  // tray: icon hidden until pinned
  ok(!(await vis('#appIcon')), 'tray icon starts hidden in the overflow');
  await page.click('#chev'); ok(await vis('#flyout'), 'chevron opens the overflow flyout');
  await page.screenshot({ path: OUT + '/s1-flyout.png' });
  await page.click('#pinBtn2'); ok(await vis('#appIcon'), 'Pin puts the icon on the taskbar');
  ok(!(await page.$('#steps')) && !(await page.$('#gs')), 'there is no ten-step list and no Getting started card');

  // peek via real keyboard hotkey, after a real mouse double-click selection
  await page.dblclick('[data-pick="216482"]');
  await page.keyboard.press('Control+Alt+KeyA');
  await page.waitForTimeout(500);
  ok(await vis('#card') && await page.$eval('#card', e => e.classList.contains('mini')), 'default (whisper) + at-risk 216482: a peek opens by itself, no invite');
  ok((await page.textContent('#c-id')).includes('216482'), 'peek shows ANDA 216482');
  ok(await inside('#card'), 'peek is fully on screen');
  ok(!(await vis('#watchSlot')), 'the peek does not show the WATCH line');
  await page.click('#moreBtn');
  ok((await page.textContent('#c-tag')) === 'OFFLINE BRIEF', 'Full brief: labelled OFFLINE BRIEF by default');
  await page.screenshot({ path: OUT + '/s2-card-a.png' });

  // clipboard-restored line + keycaps
  ok(await vis('#keys'), 'keystroke overlay shows the hotkey');

  // appearance switch via tray menu
  await page.keyboard.press('Escape');
  ok(!(await vis('#card')), 'Esc closes the card');
  await page.click('#appIcon', { button: 'right' });
  ok(await vis('#menu'), 'right-click opens the tray menu');
  const menuText = await page.textContent('#menu');
  ok(menuText.includes('Hotkeys: Ctrl+Alt+A ✓   Ctrl+Alt+Space ✓   Ctrl+Alt+Shift+R ✓'), 'menu states all three hotkeys are live');
  await page.click('#menu >> text=Settings…'); ok(await vis('#settings'), 'Settings opens'); await page.click('#adaptExpand');
  await page.screenshot({ path: OUT + '/s3-settings.png' });
  ok(await page.isChecked('input[name=consent][value=whisper]'), 'Settings: whisper invite is the default consent model');
  ok(await page.isChecked('#setUrgent'), 'Settings: urgent items open a peek is on by default');
  ok(await page.$eval('#setLead', e => e.value) === '3', 'Settings: holiday window defaults to 3 business days');
  await page.check('input[name=consent][value=full]');
  await page.check('input[name=look][value=k]');
  ok(await page.evaluate(() => document.documentElement.dataset.dir) === 'k', 'selecting K changes the appearance');
  await page.fill('#setOp', '88'); await page.dispatchEvent('#setOp', 'input');
  ok((await page.textContent('#setOpV')) === '88%', 'transparency slider label updates');
  await page.click('#setDone');

  await page.dblclick('[data-pick="214990"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(400);
  ok(await vis('.p-line'), 'appearance K shows its progress line');
  ok(await page.$eval('#c-attn', e => e.parentElement.style.display === 'none'), 'healthy record shows no needs-attention line');
  await page.screenshot({ path: OUT + '/s4-card-c-healthy.png' });
  await page.click('#expBtn'); await page.waitForTimeout(250);
  ok(await inside('#card'), 'expanded card stays on screen');
  await page.keyboard.press('Escape');

  // NDA is not guessed: lookup opens, no card
  await page.click('[data-pickbtn="NDA 021234"]'); await page.click('#keyPeek'); await page.waitForTimeout(300);
  ok(await vis('#lookup') && !(await vis('#card')), 'NDA 021234 opens the lookup box and no card');

  await page.keyboard.press('Escape');

  // lookup flow with a bad then good number
  await page.keyboard.press('Control+Alt+Space'); ok(await vis('#lookup'), 'Ctrl+Alt+Space opens lookup');
  await page.fill('#lookupIn', '12345'); await page.keyboard.press('Enter');
  ok(await vis('#lookupErr'), 'bad number shows the inline error');
  await page.fill('#lookupIn', '999999'); await page.keyboard.press('Enter'); await page.waitForTimeout(200);
  ok((await page.textContent('#chipTxt')).includes('ANDA 999999 is not in your data'), 'unknown six-digit number gets the notice, not a card');
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', 'ANDA 214990'); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
  ok(await vis('#card') && (await page.textContent('#c-id')).includes('214990'), 'lookup opens the card');
  await page.keyboard.press('Escape');

  // terminal guard
  await page.click('#appPick [data-app="term"]'); await page.waitForTimeout(100);
  await page.click('[data-pickbtn="TERM"]'); await page.click('#keyPeek'); await page.waitForTimeout(300);
  ok((await page.textContent('#chipTxt')).startsWith('Paused in terminals'), 'terminal in front: paused notice');
  ok(await vis('#lookup'), 'terminal notice still offers the lookup box');
  await page.screenshot({ path: OUT + '/s5-terminal.png' });
  await page.keyboard.press('Escape');
  await page.click('#mailBody');

  // consent: whisper + peek
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand');
  await page.check('input[name=consent][value=whisper]'); await page.uncheck('#setUrgent'); await page.click('#setDone');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(300);
  ok(await vis('#chip') && (await page.textContent('#chipTxt')) === 'ANDA 216482' && !(await vis('#card')), 'whisper with the urgent rule off: invite chip first, no card');
  await page.screenshot({ path: OUT + '/s6-whisper.png' });
  await page.click('#chip'); await page.waitForTimeout(300); ok(await vis('#card'), 'clicking the chip opens the card');
  await page.keyboard.press('Escape');
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand');
  await page.check('input[name=consent][value=peek]'); await page.check('#setUrgent'); await page.click('#setDone');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(300);
  ok(await vis('#card') && await page.$eval('#card', e => e.classList.contains('mini')) && await vis('#moreBtn'), 'peek-first: compact card with Full brief button');
  await page.screenshot({ path: OUT + '/s7-peek.png' });
  await page.click('#moreBtn'); ok(!(await page.$eval('#card', e => e.classList.contains('mini'))), 'Full brief expands it');
  await page.keyboard.press('Escape');

  // Select & Peek off => lookup, no keystroke
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand');
  await page.uncheck('#setCapture'); await page.click('#setDone');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(300);
  ok(await vis('#lookup') && !(await vis('#card')), 'capture off: hotkey opens lookup instead');
  await page.keyboard.press('Escape');
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand'); await page.check('#setCapture'); await page.click('#setDone');

  // hotkey conflict what-if
  await page.check('#swConflict'); await page.waitForTimeout(200);
  ok((await page.textContent('#chipTxt')).includes('Ctrl+Alt+A is already used by another program'), 'conflict: plain-language notice');
  await page.click('#appIcon', { button: 'right' });
  ok((await page.textContent('#menu')).includes('Ctrl+Alt+A ✗ in use'), 'conflict: tray line says in use'); await page.keyboard.press('Escape');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(250);
  ok(!(await vis('#card')) && !(await vis('#lookup')), 'conflict: pressing the taken hotkey does nothing in the app');
  await page.uncheck('#swConflict');

  // sample brief what-if + honest label
  await page.check('#swAi'); await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand');
  await page.check('input[name=consent][value=full]'); await page.click('#setDone');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(2600);
  ok((await page.textContent('#c-verify')).includes('no model is called'), 'sample brief is labelled as simulated');
  await page.screenshot({ path: OUT + '/s8-sample-brief.png' });
  await page.keyboard.press('Escape');

  // exit and relaunch
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Exit');
  ok(!(await vis('#appIcon')), 'Exit removes the tray icon');
  await page.dblclick('[data-pick="216482"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(250);
  ok(!(await vis('#card')), 'hotkeys do nothing after Exit');
  await page.dblclick('#deskIcon'); await page.waitForTimeout(200);
  ok(await vis('#appIcon'), 'the desktop icon relaunches the app (there is no launcher script)');

  console.log('console/page errors:', JSON.stringify(errors));
  console.log(fails ? `FAILED: ${fails}` : 'ALL PASS');
  await browser.close();
})();
