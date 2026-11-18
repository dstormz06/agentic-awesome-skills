// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const fs = require('fs');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
const OUT = process.argv[2] || TMP;
let fails = 0;
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const CLIENT = '12345678-1234-1234-1234-123456789abc';
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, acceptDownloads: true });
  await ctx.grantPermissions(['clipboard-read', 'clipboard-write']);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  const txt = sel => page.textContent(sel);
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && !e.hidden && e.getBoundingClientRect().width > 0).catch(() => false);
  const openSettings = async () => { await page.click('#openSettingsBtn'); await page.click('#adaptExpand'); await page.waitForTimeout(150); };
  const closeSettings = async () => { await page.click('#setDone'); await page.waitForTimeout(100); };

  await page.click('#chev'); await page.click('#pinBtn2');
  await openSettings();

  // ---- defaults ----
  ok(await page.$eval('input[name=autohide][value="10"]', e => e.checked), 'auto-hide is 10 s by default');
  ok((await page.$eval('#claudeModel', e => e.value)) === 'claude-opus-4-8', 'the Claude model name has its default');

  // ---- go to a section ----
  const options = await page.$$eval('#setJump option', o => o.map(x => x.textContent));
  ok(options.length >= 14, 'the section list has the sections: ' + options.length);
  for (const l of ['Connection · Which API, if any', 'Outlook · Optional, read-only', 'Back up, restore, reset']) ok(options.includes(l), 'section list has ' + l);
  await page.selectOption('#setJump', { label: 'Back up, restore, reset' }); await page.waitForTimeout(250);
  ok(await page.$eval('#bkSave', e => { const r = e.getBoundingClientRect(), s = document.getElementById('settings').getBoundingClientRect(); return r.top >= s.top - 1 && r.bottom <= s.bottom + 1; }), 'choosing a section scrolls it into view');
  ok((await page.$eval('#setJump', e => e.value)) === '-1', 'the list returns to its prompt afterwards');

  // ---- Claude model ----
  await page.fill('#claudeModel', 'bad name!'); await page.dispatchEvent('#claudeModel', 'blur');
  ok((await page.$eval('#claudeModel', e => e.value)) === 'claude-opus-4-8', 'an invalid model name falls back to the default');
  await page.fill('#claudeModel', 'claude-opus-5-5'); await page.dispatchEvent('#claudeModel', 'blur');
  await page.check('input[name=provider][value=claude]');
  ok((await txt('#setConn')).includes('claude-opus-5-5'), 'the connection line names the chosen model');

  // ---- Outlook: off, then a rehearsal ----
  await page.selectOption('#setJump', { label: 'Outlook · Optional, read-only' }); await page.waitForTimeout(150);
  ok(!(await page.$eval('#olOn', e => e.checked)), 'Outlook is off by default');
  ok((await txt('#olStatus')).includes('Outlook is off'), 'the status says it is off');
  ok(await page.$eval('#olClient', e => e.disabled), 'the fields are greyed out while it is off');
  await page.check('#olOn');
  ok((await txt('#olStatus')).includes('Application (client) ID'), 'turned on without an ID, it says what is missing');
  ok(await page.$eval('#olSignIn', e => e.disabled), 'Sign in waits for a valid ID');
  await page.fill('#olClient', 'not-a-guid');
  ok(await page.$eval('#olSignIn', e => e.disabled), 'a malformed ID keeps Sign in off');
  await page.fill('#olClient', CLIENT);
  ok(!(await page.$eval('#olSignIn', e => e.disabled)), 'a valid ID enables Sign in');
  await page.fill('#olTenant', 'bad tenant');
  ok((await txt('#olStatus')).includes('directory (tenant)'), 'a bad directory is explained');
  await page.fill('#olTenant', 'contoso.onmicrosoft.com');
  await page.check('input[name=olmax][value="3"]');
  ok(!(await vis('#olCopy')), 'no code is shown before sign-in');

  // the drawer on the card, while signed out
  await closeSettings();
  await page.click('#pinBtn2').catch(() => {});
  await page.keyboard.press('Control+Alt+Space'); await page.waitForTimeout(250);
  await page.fill('#lkInput', '216482').catch(async () => { await page.fill('#lookup input[type=text]', '216482'); });
  await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  ok(await vis('#card'), 'a card is open');
  ok(await vis('#x-mail-d') || await page.$eval('#x-mail-d', e => !e.hidden), 'the mail drawer appears once Outlook is on');
  await page.click('#expBtn'); await page.click('#pinBtn'); await page.waitForTimeout(150);
  await page.evaluate(() => { document.getElementById('x-mail-d').open = true; });
  ok((await txt('#x-mail')).includes('Not signed in to Outlook'), 'signed out, the drawer says how to sign in');

  // sign in (rehearsal): the card is pinned, so Settings is opened from the tray, as a person would
  await page.click('#appIcon', { button: 'right' }); await page.click('#menu >> text=Settings…'); await page.click('#adaptExpand'); await page.waitForTimeout(150);
  await page.selectOption('#setJump', { label: 'Outlook · Optional, read-only' });
  await page.click('#olSignIn'); await page.waitForTimeout(200);
  ok((await txt('#olStatus')).includes('DEMO-4821') && (await vis('#olCopy')), 'signing in shows the code and a Copy code button');
  await page.click('#olCopy');
  ok((await page.evaluate(() => navigator.clipboard.readText())) === 'DEMO-4821', 'Copy code copies it');
  await page.waitForTimeout(2800);
  ok((await txt('#olStatus')).includes('Signed in to Outlook'), 'the rehearsal completes: signed in');
  ok((await txt('#olSignIn')) === 'Sign in again', 'the button then offers Sign in again');
  const mail = await page.$eval('#x-mail', e => e.innerText);
  ok(mail.includes('RE: ANDA 216482 labeling comments') && mail.includes('made up'), 'the drawer lists made-up messages for the open ANDA');
  ok((await page.$$eval('#x-mail > div', d => d.filter(x => x.style.fontWeight === '600').length)) === 3, 'it shows the chosen number of messages (3)');
  ok(mail.includes('Not saved, not sent to any AI service'), 'the drawer says what happens to the mail');
  await page.click('#olSignOut');
  ok((await txt('#olStatus')).includes('Signed out. Nothing from Outlook is kept.'), 'Sign out says nothing is kept');
  ok((await txt('#x-mail')).includes('Not signed in to Outlook'), 'and the drawer follows');
  await page.uncheck('#olOn');
  ok(await page.$eval('#x-mail-d', e => e.hidden), 'turning Outlook off removes the drawer');

  // ---- backup, restore, reset ----
  await page.check('#olOn');
  await page.check('input[name=autohide][value="20"]');
  await page.check('input[name=tone][value=Warm]');
  await page.fill('#setSignoff', 'Thanks, Pat');
  await page.fill('#setStyle', 'Plain words, under 120 words.');
  await page.selectOption('#setLead', '5');
  const [download] = await Promise.all([page.waitForEvent('download'), page.click('#bkSave')]);
  const file = (OUT || '.') + '/backup-test.json'; await download.saveAs(file);
  const backup = JSON.parse(fs.readFileSync(file, 'utf8'));
  ok(backup.kind === 'anda-hover-companion-backup' && backup.version === 1, 'Save a backup writes a versioned file');
  ok(backup.settings.autoHide === 20 && backup.settings.tone === 'Warm' && backup.settings.signOff === 'Thanks, Pat' && backup.settings.holidayLead === 5 && backup.settings.outlook.clientId === CLIENT, 'it holds the choices made');
  ok(!/access_token|bearer [a-z0-9]|secret|password|DEMO-4821/i.test(fs.readFileSync(file, 'utf8').replace(/"env": ?"[^"]*"/, '')), 'it holds no token, code or secret');
  ok(!/Documents|portfolio|my-portfolio/i.test(fs.readFileSync(file, 'utf8')), 'it holds no file location');
  ok((await page.$eval('#bkText', e => e.value)).includes('anda-hover-companion-backup'), 'the backup text is also shown in case downloads are blocked');

  // change things, then restore
  await page.check('input[name=autohide][value="40"]'); await page.check('input[name=tone][value=Direct]'); await page.fill('#setSignoff', 'Other');
  await page.setInputFiles('#bkFile', file); await page.waitForTimeout(300);
  ok((await txt('#bkStatus')).startsWith('Restored your settings'), 'Restore reports what it restored: ' + (await txt('#bkStatus')).slice(0, 60));
  ok(await page.$eval('input[name=autohide][value="20"]', e => e.checked), 'restore: auto-hide is back to 20 s');
  ok(await page.$eval('input[name=tone][value=Warm]', e => e.checked) && (await page.$eval('#setSignoff', e => e.value)) === 'Thanks, Pat', 'restore: tone and sign-off are back');
  ok((await page.$eval('#olClient', e => e.value)) === CLIENT && (await page.$eval('#setLead', e => e.value)) === '5', 'restore: Outlook ID and holiday lead are back');

  // refusals change nothing
  const bad = (OUT || '.') + '/not-a-backup.json';
  for (const [content, expect] of [['{not json', 'not a backup'], ['{"kind":"something-else","version":1,"settings":{}}', 'not a backup'], [JSON.stringify({ kind: 'anda-hover-companion-backup', version: 9, settings: {} }), 'newer version']]) {
    fs.writeFileSync(bad, content);
    await page.setInputFiles('#bkFile', bad); await page.waitForTimeout(250);
    ok((await txt('#bkStatus')).includes(expect) && (await txt('#bkStatus')).includes('Nothing was changed'), 'a refused file is explained: ' + expect);
  }
  ok(await page.$eval('input[name=autohide][value="20"]', e => e.checked), '...and changed nothing');

  // a repaired backup: out-of-range values fall back to the defaults
  const hostile = JSON.parse(JSON.stringify(backup)); hostile.settings.autoHide = 7; hostile.settings.tone = 'Sarcastic'; hostile.settings.keys.peek = 'A'; hostile.settings.claudeModel = '<script>';
  fs.writeFileSync(bad, JSON.stringify(hostile));
  await page.setInputFiles('#bkFile', bad); await page.waitForTimeout(250);
  ok(await page.$eval('input[name=autohide][value="10"]', e => e.checked), 'a bad auto-hide value falls back to 10 s');
  ok(await page.$eval('input[name=tone][value=""]', e => e.checked), 'an unknown tone falls back to none');
  ok((await page.$eval('#keyBoxPeek', e => e.value)) === 'Ctrl+Alt+A', 'an invalid shortcut set falls back to the defaults');
  ok((await page.$eval('#claudeModel', e => e.value)) === 'claude-opus-4-8', 'an invalid model falls back to the default');

  // restore from pasted text (the hosted page cannot download a file)
  await page.fill('#setSignoff', 'Changed again');
  await page.fill('#bkText', JSON.stringify(backup));
  await page.click('#bkRestoreText');
  ok((await txt('#bkStatus')).startsWith('Restored your settings') && (await page.$eval('#setSignoff', e => e.value)) === 'Thanks, Pat', 'pasted backup text restores too');
  await page.fill('#bkText', 'garbage'); await page.click('#bkRestoreText');
  ok((await txt('#bkStatus')).includes('not a backup') && (await page.$eval('#setSignoff', e => e.value)) === 'Thanks, Pat', 'pasted garbage is refused and changes nothing');

  // reset takes two clicks
  await page.fill('#setSignoff', 'Thanks, Pat'); await page.check('input[name=tone][value=Warm]');
  await page.click('#bkReset');
  ok((await txt('#bkReset')) === 'Click again to reset' && (await page.$eval('#setSignoff', e => e.value)) === 'Thanks, Pat', 'the first click only asks');
  await page.click('#bkReset');
  ok((await txt('#bkReset')) === 'Reset all settings…' && (await page.$eval('#setSignoff', e => e.value)) === '' && await page.$eval('input[name=autohide][value="10"]', e => e.checked), 'the second click resets');
  ok(!(await page.$eval('#olOn', e => e.checked)), 'Outlook is off again after a reset');
  ok(await page.$eval('input[name=provider][value=offline]', e => e.checked), 'the connection is back to Offline only, the default since round 28');

  await page.screenshot({ path: (OUT || '.') + '/r11-settings.png' });
  ok(errors.length === 0, 'no page errors: ' + errors.join(' | '));
  await browser.close();
  console.log(fails ? `\n${fails} FAILED` : '\nALL PASSED');
  process.exit(fails ? 1 : 0);
})();
