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
  await ctx.grantPermissions(['clipboard-read', 'clipboard-write']);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0).catch(() => false);
  const txt = sel => page.textContent(sel);
  const openSettings = async () => { await page.click('#openSettingsBtn'); await page.click('#adaptExpand'); await page.waitForTimeout(150); };
  const closeSettings = async () => { await page.click('#setDone'); await page.waitForTimeout(100); };
  const pressInBox = async (box, combo) => { await page.click(box); await page.keyboard.press(combo); await page.waitForTimeout(80); };

  await page.click('#chev'); await page.click('#pinBtn2');
  // ---------- the settings the user said were missing are there ----------
  await openSettings();
  const labels = await page.$$eval('#settings .lbl', els => els.map(e => e.textContent.trim()));
  for (const l of ['SHORTCUTS · CLICK A BOX, THEN PRESS THE KEYS', 'OTHER WAYS TO START IT', 'MY DATA · YOUR OWN FILES, READ-ONLY, ON THIS PC', 'MY WRITING · TONE, STYLE, SIGN-OFF, QUICK REPLIES', 'CONNECTION · WHICH API, IF ANY'])
    ok(labels.includes(l), 'Settings has the section: ' + l);
  ok(await page.$eval('#keyBoxLook', e => e.value) === 'Ctrl+Alt+Space', 'the lookup shortcut shows its current keys');

  // ---------- rebinding: the shortcut that "did not work" can be changed ----------
  await pressInBox('#keyBoxLook', 'Control+Alt+KeyK');
  ok(await page.$eval('#keyBoxLook', e => e.value) === 'Ctrl+Alt+K', 'click the box, press new keys: Ctrl+Alt+K is stored');
  ok((await txt('#keyStatus')).includes('✓ Lookup box: Ctrl+Alt+K is active'), 'the status line says the new shortcut is active');
  ok((await page.$eval('#keyLook', e => e.textContent)) === 'Ctrl+Alt+K', 'the guide shows the new shortcut');
  ok((await txt('#keyLook')) === 'Ctrl+Alt+K', 'the on-screen button shows the new shortcut');
  await closeSettings();
  await page.keyboard.press('Control+Alt+Space'); await page.waitForTimeout(200);
  ok(!(await vis('#lookup')), 'the old shortcut no longer opens the lookup box');
  await page.keyboard.press('Control+Alt+KeyK'); await page.waitForTimeout(200);
  ok(await vis('#lookup'), 'the new shortcut opens the lookup box');
  await page.keyboard.press('Escape');

  await openSettings();
  await pressInBox('#keyBoxLook', 'Alt+KeyK');
  ok((await txt('#keyStatus')).includes('at least two modifiers'), 'one modifier is refused with the reason');
  ok(await page.$eval('#keyBoxLook', e => e.value) === 'Ctrl+Alt+K', '...and the stored shortcut is unchanged');
  await pressInBox('#keyBoxLook', 'Meta+Control+KeyK');
  ok((await txt('#keyStatus')).includes('Windows key'), 'the Windows key is refused');
  await pressInBox('#keyBoxLook', 'Control+Alt+KeyA');
  ok((await txt('#keyStatus')).includes('cannot use the same shortcut'), 'a duplicate is refused');
  await page.click('[data-keyreset=look]');
  ok(await page.$eval('#keyBoxLook', e => e.value) === 'Ctrl+Alt+Space', 'Reset restores the default');
  await closeSettings();

  // ---------- another program owns the lookup shortcut (the case that was reported) ----------
  await page.check('#swConflictLook'); await page.waitForTimeout(250);
  ok((await txt('#chipTxt')).includes('Ctrl+Alt+Space is already used by another program'), 'the notice names the shortcut another program owns');
  await page.keyboard.press('Control+Alt+Space'); await page.waitForTimeout(200);
  ok(!(await vis('#lookup')) && (await txt('#keys')).includes('Another program handled this key'), 'a taken shortcut never reaches the app, and the overlay says so');
  await page.click('#appIcon', { button: 'right' }); await page.waitForTimeout(150);
  ok((await txt('#menu')).includes('Ctrl+Alt+Space ✗ in use'), 'the tray line marks it "in use"');
  await page.keyboard.press('Escape');
  await openSettings();
  ok((await txt('#keyStatus')).includes('✗ Lookup box: Ctrl+Alt+Space is already used by another program'), 'Settings says which shortcut is taken and what to do');
  await closeSettings();
  await page.click('#appIcon'); await page.waitForTimeout(150);
  ok(await vis('#today') && await vis('#tdSearch'), 'the tray icon still opens Today, whose box finds an ANDA without the shortcut (round 34: Today is home)');
  await page.evaluate(() => closeApp('today'));
  await page.keyboard.press('Escape');
  await page.uncheck('#swConflictLook');

  // ---------- middle-click on selected text ----------
  const pickBox = async () => page.$eval('[data-pick="216482"]', e => { const r = e.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
  await openSettings();
  ok(!(await page.isChecked('#setMiddle')), 'middle-click is off by default');
  ok(await page.isChecked('input[name=midmod][value=Ctrl]'), 'Ctrl is the preselected key');
  await page.check('#setMiddle'); await closeSettings();
  await page.dblclick('[data-pick="216482"]'); await page.waitForTimeout(100);
  let p = await pickBox();
  await page.mouse.click(p.x, p.y, { button: 'middle' }); await page.waitForTimeout(250);
  ok(!(await vis('#card')) && !(await vis('#chip') && (await txt('#chipTxt')).includes('216482')), 'a plain middle-click is ignored while Ctrl is required');
  await page.keyboard.down('Control'); await page.mouse.click(p.x, p.y, { button: 'middle' }); await page.keyboard.up('Control'); await page.waitForTimeout(300);
  ok(await vis('#card'), 'Ctrl + middle-click on the selected number opens the peek (216482 is at risk)');
  ok((await txt('#keys')).includes('Middle-click'), 'the overlay shows the middle-click, not a keyboard shortcut');
  await page.keyboard.press('Escape');
  await openSettings(); await page.uncheck('#setMiddle'); await closeSettings();
  await page.dblclick('[data-pick="216482"]'); p = await pickBox();
  await page.keyboard.down('Control'); await page.mouse.click(p.x, p.y, { button: 'middle' }); await page.keyboard.up('Control'); await page.waitForTimeout(250);
  ok(!(await vis('#card')), 'switched off: middle-click does nothing');

  // ---------- the two reference lines ----------
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Lookup ANDA…');
  await page.fill('#lookupIn', '216482'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  ok((await txt('#c-psg')) === 'Jan 14, 2026 · fictional demo date' && (await txt('#c-rld')) === 'Mar 3, 2026 · fictional demo date', 'demo data: both reference lines, labelled as fictional demo dates');
  await page.click('#chipMore'); await page.waitForTimeout(150);   // round 34: the reference lines sit under More
  ok(await vis('#psgSlot') && await vis('#rldSlot'), 'both lines are visible on the full card once More is pressed');
  await page.screenshot({ path: OUT + '/r10-card-refs.png' });
  await page.click('#pinBtn'); await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand'); await page.waitForTimeout(150);
  await page.click('#dataPortfolioPick'); await page.waitForTimeout(150);
  ok((await txt('#dataPortfolioStatus')).includes('Loaded 3 ANDAs'), 'choosing a portfolio (simulated) shows its status');
  ok((await txt('#c-psg')).startsWith('not available: no PSG date in your data') && (await txt('#c-rld')).startsWith('not available: no Drugs@FDA file chosen'), 'with your own data and no source, both lines say briefly that the date is not available');
  await page.click('#dataRefPick'); await page.waitForTimeout(150);
  ok((await txt('#c-rld')) === 'Sep 17, 2025 · Drugs@FDA (simulated file), saved Jun 28, 2026 (18 days ago)', 'with a Drugs@FDA file chosen the RLD line shows its date and source');
  ok((await txt('#c-psg')).startsWith('not available'), '...while the PSG line still says it has no source');
  await page.fill('#dataUrl', 'http://insecure.example/f.zip'); await page.click('#dataUpdate');
  ok((await txt('#dataRefStatus')).includes('must start with https://'), 'Update now refuses an address that is not https');
  await page.fill('#dataUrl', 'https://files.example.test/drugsfda.zip'); await page.click('#dataUpdate');
  ok((await txt('#dataRefStatus')).includes('Simulation: nothing was fetched'), 'Update now says plainly that the simulation fetches nothing');
  await page.click('#dataPortfolioClear'); await page.click('#dataRefClear');
  ok((await txt('#c-psg')).includes('fictional demo date'), 'clearing the files returns to the demo dates');
  await closeSettings(); await page.click('#pinBtn'); await page.keyboard.press('Escape');

  // ---------- connection: never claims to be connected ----------
  await openSettings();
  ok((await txt('#setBannerLabel')) === 'OFFLINE · no API connected', 'the Settings banner says OFFLINE · no API connected');
  ok((await page.$eval('#appIcon', e => e.title)).includes('OFFLINE · no API connected'), 'the tray icon tooltip says it too');
  await page.check('input[name=provider][value=claude]');
  ok((await txt('#setBannerLabel')) === 'CLAUDE · set up, not tested', 'choosing Claude says "set up, not tested", not connected');
  await page.check('input[name=provider][value=myapi]');
  ok(await page.$eval('#myApi', e => e.open), 'choosing My API opens its form');
  await page.fill('#apiEndpoint', 'http://insecure.example/v1');
  ok((await txt('#setConn')).includes('https://'), 'an http address is refused in words');
  await page.fill('#apiEndpoint', 'https://api.example.test/v1/chat'); await page.fill('#apiEnv', 'sk-live-abc123');
  ok((await txt('#setConn')).includes('NAME of the variable'), 'a pasted token where the variable NAME goes is refused');
  await page.fill('#apiEnv', 'MY_TEST_TOKEN');
  await page.fill('#apiTemplate', '{"messages": [ {{user}} ');
  ok((await txt('#setConn')).includes('not valid JSON'), 'a broken request body is caught');
  await page.fill('#apiTemplate', '{"model":"{{model}}","max_tokens":{{max_tokens}},"messages":[{"role":"user","content":"{{user}}"}]}');
  await page.fill('#apiPath', 'choices[0.message');
  ok((await txt('#setConn')).includes('reply path is not valid'), 'a bad reply path is caught');
  await page.fill('#apiPath', 'choices[0].message.content'); await page.fill('#apiName', 'Division API');
  ok((await txt('#setBannerLabel')) === 'DIVISION API · set up, not tested', 'a valid My API shows its own name, set up, not tested');
  await page.click('#setTest'); await page.waitForTimeout(700);
  ok((await txt('#setConn')).includes('no request was made, so nothing is connected'), 'Test in the simulation says nothing was sent and nothing is connected');
  ok(!(await txt('#setBannerLabel')).startsWith('CONNECTED'), 'the banner never says CONNECTED');
  await page.screenshot({ path: OUT + '/r10-settings-connection.png' });
  await page.check('input[name=provider][value=offline]');
  ok((await txt('#setBannerLabel')) === 'OFFLINE · no API connected', 'Offline only returns to OFFLINE');
  await closeSettings();
  await page.check('#swAi');
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', '214990'); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  ok((await txt('#c-conn-t')).startsWith('SIMULATED CONNECTION') && !(await txt('#c-conn-t')).startsWith('CONNECTED'), 'the card pill says SIMULATED CONNECTION when the what-if is on');
  await page.uncheck('#swAi'); await page.waitForTimeout(200);
  ok((await txt('#c-conn-t')) === 'OFFLINE · no API connected', 'and OFFLINE · no API connected when it is off');
  await page.keyboard.press('Escape');

  // ---------- quick replies ----------
  await page.keyboard.press('Control+Alt+Space'); await page.fill('#lookupIn', '216482'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  ok(await vis('#chipReplies'), 'the card has a Quick replies button');
  await page.click('#chipReplies'); await page.waitForTimeout(150);
  const items = await page.$$eval('#replyMenu button', bs => bs.map(b => b.textContent));
  ok(items.length === 3 && items.includes('Status for my lead'), 'the menu lists the three starter replies: ' + items.join(', '));
  await page.click('#replyMenu >> text=Status for my lead'); await page.waitForTimeout(200);
  const copied = await page.evaluate(() => navigator.clipboard.readText());
  ok(copied.startsWith('Hi, quick status on ANDA 216482 (Metformin HCl ER Tablets, 500 mg). It is 62% through Cycle 2. Goal date is Oct 3, 2026 and the forecast is Oct 17, 2026 (+14 d vs goal, moderate confidence). Needs action: IR response due in 5 days'), 'the reply is copied filled in from the card: ' + copied.slice(0, 60));
  ok((await txt('#chipReplies')).includes('Copied'), 'the button says Copied');
  await page.keyboard.press('r'); await page.waitForTimeout(150);
  ok(await vis('#replyMenu'), 'the R key opens the menu');
  await page.keyboard.press('Escape'); await page.waitForTimeout(100);
  ok(!(await vis('#replyMenu')), 'Esc closes the menu and not the card'); 
  // manager
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Quick replies…'); await page.waitForTimeout(150);
  ok(await vis('#replies'), 'the tray menu opens Quick replies');
  await page.click('#rpNew'); await page.fill('#rpName', 'Holiday check'); await page.fill('#rpText', 'ANDA {number}: PSG {psg} | RLD {rld} | {nothing} | {forecast}');
  ok((await txt('#rpPreview')).includes('Jan 14, 2026 · fictional demo date') && (await txt('#rpPreview')).includes('{nothing}'), 'the preview fills placeholders and leaves unknown ones as typed');
  await page.click('#rpSave'); ok((await txt('#rpStatus')) === 'Saved.', 'a new reply is saved');
  await page.fill('#rpName', ''); await page.click('#rpSave'); ok((await txt('#rpStatus')).includes('Give the reply a name'), 'a reply needs a name');
  await page.fill('#rpName', 'Holiday check'); await page.click('#rpDel'); ok((await txt('#rpStatus')) === 'Deleted.', 'a reply can be deleted');
  await page.click('#rpClose');

  // ---------- my writing ----------
  await openSettings();
  await page.check('input[name=tone][value=Warm]'); await page.fill('#setSignoff', 'Thanks, Pat'); await page.fill('#setStyle', 'Under 120 words.');
  await page.click('#repliesManage'); await page.waitForTimeout(100); ok(await vis('#replies') && await vis('#settings'), 'Quick replies opens from Settings without closing it'); await page.click('#rpClose');
  await closeSettings();
  await page.keyboard.press('Escape');
  await page.click('[data-pickbtn="DRAFT"]'); await page.keyboard.press('Control+Alt+Shift+KeyR'); await page.waitForTimeout(300);
  await page.check('#swAi'); await page.click('#draft [data-ai="Rewrite"]');
  const ai = await txt('#drLocalOut');
  ok(ai.includes('Tone: warm.') && ai.includes('Under 120 words.') && ai.includes('sign-off exactly: Thanks, Pat'), 'the AI preview carries the tone, style and sign-off');
  await page.uncheck('#swAi'); await page.keyboard.press('Escape');

  // ---------- phone width: nothing overflows with the new sections ----------
  await page.setViewportSize({ width: 390, height: 760 }); await page.waitForTimeout(150);
  await openSettings();
  ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'phone width: no horizontal page scroll with Settings open');
  await page.screenshot({ path: OUT + '/r10-settings-phone.png' });

  console.log('console/page errors:', JSON.stringify(errors));
  console.log(fails ? `FAILED: ${fails}` : 'ALL PASS');
  await browser.close();
})();
