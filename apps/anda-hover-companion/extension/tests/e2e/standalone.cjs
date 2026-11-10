// End to end, no desktop app: the extension in a real browser engine, on real pages.
const h = require('./harness.cjs');
const path = require('path'), fs = require('fs'), os = require('os');

const PAGE = `<!doctype html><meta charset="utf-8"><title>Huddle prep</title><body style="font:16px sans-serif;padding:40px">
<h1>Huddle prep</h1>
<p>Please check <span id="a1">216482</span> and <span id="a2">214990</span>. Not this one: <span id="nda">NDA 021234</span>. Unknown: <span id="unk">999999</span>. Short: <span id="short">12345</span>.</p>
<p>Later: ANDA 216482 again, and ANDA 888888.</p>
<textarea id="draft" style="width:600px;height:90px">Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. ANDA 123456 needs a call.</textarea>
<div id="compose" contenteditable="true" style="border:1px solid #999;min-height:40px;margin-top:10px">ANDA 214990 is ahead of schedule. Goal Aug 15, forecast Aug 6.</div>
</body>`;

(async () => {
  const { server, base } = await h.servePages({ '/': { body: PAGE }, '/strict': { body: PAGE, headers: { 'content-security-policy': "default-src 'none'; style-src 'none'" } } });
  const { context, worker, id } = await h.launch();
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
  const page = await context.newPage();
  const chip = () => page.locator('.chip:not(.notice)'), notice = () => page.locator('.chip.notice'), card = () => page.locator('.card'), panel = () => page.locator('.panel');
  const text = async (loc) => (await loc.first().innerText()).replace(/\s+/g, ' ');
  const goto = async (u) => { await page.goto(base + u); await page.bringToFront(); };

  h.ok(id === h.EXPECTED_ID, 'the extension id is the one the desktop app allows');

  // ---- whisper is the default: a healthy ANDA gets a small invite, a click opens the full card ----
  await goto('/');
  await h.select(page, '#a2');
  await h.trigger(worker, 'peek');
  await chip().waitFor({ timeout: 5000 });
  h.ok((await text(chip())).includes('ANDA 214990'), 'default (whisper): a healthy ANDA gets the small invite chip');
  h.ok((await card().count()) === 0, '... and no card yet');
  await chip().click();
  await card().waitFor({ timeout: 5000 });
  let t = await text(card());
  h.ok(t.includes('Metformin HCl ER Tablets, 750 mg') && t.includes('OFFLINE BRIEF') && t.includes('FICTIONAL DEMO'), 'clicking the chip opens the full card with the offline label and the demo marker');
  h.ok(t.includes('WATCH') && t.includes('Goal date falls on a weekend: Sat Aug 15'), 'the WATCH line flags the Saturday goal date');
  h.ok(!(t.includes('Opened because')), 'a full card does not claim it opened by itself');
  await page.keyboard.press('e');
  h.ok(await page.locator('.card.wide').count() === 1, 'E expands the card');
  h.ok((await text(card())).includes('Source: U.S. federal calendar (OPM)'), 'the drawer lists each flag with its source');
  await page.keyboard.press('Escape');
  h.ok((await card().count()) === 0, 'Escape closes the card');

  // ---- an at-risk ANDA opens a peek by itself and says why ----
  await h.select(page, '#a1');
  await h.trigger(worker, 'peek');
  await card().waitFor({ timeout: 5000 });
  t = await text(card());
  h.ok(t.includes('Full brief') && !t.includes('NEEDS YOU'), 'whisper + at-risk: a small peek opens by itself');
  h.ok(t.includes('Opened because: Forecast is 14 days after the goal'), 'the peek says what made it open');
  await page.getByRole('button', { name: 'Full brief ↓' }).click();
  t = await text(card());
  h.ok(t.includes('NEEDS YOU') && t.includes('IR response due in 5 days') && t.includes('Goal date falls on a weekend: Sat Oct 3'), 'Full brief shows needs-you and the weekend flag');
  h.ok(t.includes('(+1 more in the drawer)'), 'and points to the second flag');
  await page.locator('button', { hasText: 'Copy status' }).click();
  await page.locator('button', { hasText: /Copied|Copy blocked/ }).first().waitFor({ timeout: 3000 });
  h.ok((await text(page.locator('button', { hasText: /Copied|Copy blocked/ }))).includes('Copied ✓'), 'Copy status says Copied');
  const copied = await page.evaluate(() => navigator.clipboard.readText().catch(() => null));
  h.ok(copied === 'ANDA 216482 (Metformin HCl ER Tablets, 500 mg): Cycle 2, 62% through review; goal Oct 3, 2026; forecast Oct 17, 2026 (+14 d vs goal, moderate confidence); needs action: IR response due in 5 days — Biopharmaceutics.', 'the clipboard holds the status sentence: ' + copied);
  await page.keyboard.press('Escape');

  // ---- what is not an ANDA is not guessed ----
  for (const [sel, expect, label] of [['#nda', 'No ANDA number in the selection', 'NDA 021234'], ['#short', 'No ANDA number in the selection', 'a five-digit number'], ['#unk', '999999 is not in the available records', 'an unknown six-digit number']]) {
    await h.select(page, sel); await h.trigger(worker, 'peek');
    await notice().waitFor({ timeout: 5000 });
    h.ok((await text(notice())).includes(expect), `${label}: ${expect}`);
    await page.keyboard.press('Escape');
  }
  await page.evaluate(() => getSelection().removeAllRanges());
  await h.trigger(worker, 'peek');
  await notice().waitFor({ timeout: 5000 });
  h.ok((await text(notice())).includes('select an ANDA number first'), 'no selection: it says what to do');
  await page.keyboard.press('Escape');

  // ---- right-click text from the browser's own menu is used even when the page selection is gone ----
  await page.evaluate(() => getSelection().removeAllRanges());
  await h.trigger(worker, 'peek', '214990');
  await chip().waitFor({ timeout: 5000 });
  h.ok((await text(chip())).includes('214990'), 'the right-click menu text is used when it is all there is');
  await page.keyboard.press('Escape');

  // ---- consent models and the urgent rule follow the settings ----
  await h.setSettings(worker, { consent: 'peek' });
  await h.select(page, '#a2'); await h.trigger(worker, 'peek');
  await card().waitFor();
  h.ok((await text(card())).includes('Full brief'), 'consent "peek first": a small card for any ANDA');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { consent: 'full' });
  await h.select(page, '#a2'); await h.trigger(worker, 'peek');
  await card().waitFor();
  h.ok((await text(card())).includes('OFFLINE BRIEF'), 'consent "full card": the whole card at once');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { consent: 'whisper', escalateUrgent: false });
  await h.select(page, '#a1'); await h.trigger(worker, 'peek');
  await chip().waitFor();
  h.ok(true, 'whisper with the urgent rule off: the at-risk ANDA waits for a click');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { consent: 'whisper', escalateUrgent: true });

  // ---- check a draft: from a text field, from a selection, from a message being composed ----
  await page.locator('#draft').focus();
  await h.trigger(worker, 'draft');
  await panel().waitFor({ timeout: 5000 });
  t = await text(panel());
  h.ok(t.includes('the draft gives the goal date as Oct 10, 2026, but the record says Oct 3, 2026') && t.includes('sounds on track, but') && t.includes('ANDA 123456 is not in your data'), 'draft check from the field you are typing in finds the three planted mistakes');
  h.ok(t.includes('is not the system of record') && t.includes('Nothing was sent anywhere'), 'and says what it did not check and that nothing was sent');
  await page.keyboard.press('Escape');
  await page.locator('#compose').focus();
  await h.trigger(worker, 'draft');
  await panel().waitFor();
  h.ok((await text(panel())).includes('the draft sounds late or at risk') === false && (await text(panel())).includes('Checked'), 'a message being composed (contenteditable) is checked, and a correct draft is not accused');
  await page.keyboard.press('Escape');

  // ---- scan this page ----
  await page.evaluate(() => getSelection().removeAllRanges());
  await page.locator('body').click({ position: { x: 5, y: 300 } });
  await h.trigger(worker, 'scan');
  await panel().waitFor({ timeout: 5000 });
  t = await text(panel());
  h.ok(t.includes('216482') && t.includes('214990') && t.includes('999999') && t.includes('888888') && !t.includes('021234'), 'scan lists the ANDA numbers on the page, once each, and never the NDA number');
  h.ok(t.includes('Not in your data'), 'numbers the data does not know are marked, not hidden');
  await panel().locator('.item', { hasText: '216482' }).click();
  await card().waitFor();
  h.ok((await text(card())).includes('Metformin HCl ER Tablets, 500 mg'), 'clicking a scanned number opens its card');
  await page.keyboard.press('Escape');

  // ---- a page with a strict content security policy still gets a styled card ----
  await goto('/strict');
  await h.select(page, '#a2'); await h.trigger(worker, 'peek');
  await chip().waitFor({ timeout: 5000 });
  const bg = await chip().evaluate((el) => getComputedStyle(el).backgroundColor);
  h.ok(bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent', 'under a strict style policy the card is still styled (constructable stylesheet): ' + bg);
  await page.keyboard.press('Escape');

  // ---- auto-hide ----
  await goto('/');
  await h.setSettings(worker, { autoHideSeconds: 10, consent: 'full' });
  await h.select(page, '#a2'); await h.trigger(worker, 'peek');
  await card().waitFor();
  await page.mouse.move(5, 5);
  await page.waitForTimeout(11800);
  h.ok((await card().count()) === 0, 'the card hides itself after 10 s idle when the pointer is not on it');
  await h.select(page, '#a2'); await h.trigger(worker, 'peek');
  await card().waitFor();
  await card().hover();
  await page.waitForTimeout(11800);
  h.ok((await card().count()) === 1, 'and never while the pointer is on it');
  await page.keyboard.press('Escape');
  await h.setSettings(worker, { autoHideSeconds: 20, consent: 'whisper' });

  // ---- focus goes back where it was ----
  await page.locator('#draft').focus();
  await h.select(page, '#a2');
  await page.locator('#draft').focus();
  await h.setSettings(worker, { consent: 'full' });
  await h.trigger(worker, 'peek', '214990');
  await card().waitFor();
  await page.keyboard.press('Escape');
  h.ok(await page.evaluate(() => document.activeElement && document.activeElement.id) === 'draft', 'closing the card returns focus to the field you were typing in');
  await h.setSettings(worker, { consent: 'whisper' });

  // ---- fields that hold secrets are never read, even when their text is selected ----
  await goto('/');
  await page.evaluate(() => {
    const mk = (attrs) => { const i = document.createElement('input'); Object.assign(i, attrs); i.value = 'ANDA 216482 hunter2'; document.body.append(i); return i; };
    mk({ type: 'password', id: 'pw' }); mk({ type: 'text', id: 'cc', name: 'cc-number' }); mk({ type: 'text', id: 'otp' }).setAttribute('autocomplete', 'one-time-code'); mk({ type: 'number', id: 'num' });
  });
  for (const id of ['pw', 'cc', 'otp']) {
    await page.locator('#' + id).focus(); await page.evaluate((i) => document.getElementById(i).select(), id);
    await h.trigger(worker, 'peek');
    await notice().waitFor({ timeout: 5000 });
    h.ok((await text(notice())).includes('select an ANDA number first') && (await card().count()) === 0 && (await chip().count()) === 0, `a ${id === 'pw' ? 'password' : id === 'cc' ? 'card-number' : 'one-time-code'} field is not read for a peek, even with its text selected`);
    await page.keyboard.press('Escape');
    await h.trigger(worker, 'draft');
    await notice().waitFor({ timeout: 5000 });
    h.ok((await text(notice())).includes('select some text') && (await panel().count()) === 0, '... nor for a draft check');
    await page.keyboard.press('Escape');
  }
  await page.locator('#draft').focus(); await page.evaluate(() => document.getElementById('draft').select());
  await h.trigger(worker, 'draft');
  await panel().waitFor({ timeout: 5000 });
  h.ok(true, 'an ordinary text box is still read when you ask');
  await page.keyboard.press('Escape');

  // ---- a page script left over from before an extension update is replaced, not trusted ----
  // (A real reload cannot be driven in this headless harness, so the old script's "my connection is gone" state is set directly, in the same isolated world.)
  await h.select(page, '#a2');
  await h.trigger(worker, 'peek');
  await chip().waitFor({ timeout: 5000 });
  await page.keyboard.press('Escape');
  await worker.evaluate(async () => {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => { globalThis.__andaCompanionLoaded = { alive: () => false }; } });
  });
  await h.select(page, '#a2');
  await h.trigger(worker, 'peek');
  await chip().waitFor({ timeout: 5000 });
  h.ok((await page.locator('[data-anda-companion]').count()) === 1, 'a dead page script is cleared away and replaced: one copy of the UI host, and it works');
  await page.keyboard.press('Escape');

  // ---- the toolbar popup, opened as a tab ----
  const popup = await context.newPage();
  await popup.goto(`chrome-extension://${id}/src/popup.html`);
  await popup.fill('#num', 'ANDA 216482'); await popup.keyboard.press('Enter');
  await popup.locator('.card').waitFor();
  h.ok((await popup.locator('.card').innerText()).includes('Metformin HCl ER Tablets, 500 mg'), 'popup: look up a number');
  {
    const t0 = (await popup.locator('.card').innerText()).replace(/\s+/g, ' ');
    h.ok(t0.includes('PSG Jan 14, 2026 · fictional demo date') && t0.includes('RLD LABEL Mar 3, 2026 · fictional demo date'), 'card: always two reference lines, here the fictional demo dates, labeled as fictional');
  }
  await popup.fill('#num', '999999'); await popup.keyboard.press('Enter');
  await popup.locator('#lookMsg').waitFor({ state: 'visible' });
  h.ok((await popup.locator('#lookMsg').innerText()).includes('999999 is not in the available records'), 'popup: an unknown number is said plainly');
  await popup.click('#t-draft');
  await popup.fill('#draftText', 'ANDA 216482 goal date is Oct 3, 2026.');
  await popup.click('#draftGo');
  await popup.locator('.find').first().waitFor();
  h.ok((await popup.locator('#draftOut').innerText()).includes('no differences from the record'), 'popup: a correct draft says what it checked');
  await popup.click('#t-watch');
  await popup.locator('.item').first().waitFor();
  h.ok((await popup.locator('#watchOut .item').first().innerText()).includes('216482'), 'popup: the watch list leads with the at-risk ANDA');
  h.ok((await popup.locator('#source').innerText()).includes('Fictional demo data'), 'popup: says which data it is showing');
  // keyboard: the tabs are one tab stop and the arrow keys move between them (ARIA tabs pattern); Settings is a real button
  await popup.focus('#t-watch'); await popup.keyboard.press('ArrowRight');
  h.ok(await popup.evaluate(() => document.activeElement.id === 't-page' && document.getElementById('t-page').getAttribute('aria-selected') === 'true' && !document.getElementById('p-page').hidden),
    'popup: ArrowRight moves to the next tab and focuses it');
  await popup.keyboard.press('Home');
  h.ok(await popup.evaluate(() => document.activeElement.id === 't-look' && !document.getElementById('p-look').hidden), 'popup: Home goes to the first tab');
  await popup.keyboard.press('ArrowLeft');
  h.ok(await popup.evaluate(() => document.activeElement.id === 't-page'), 'popup: ArrowLeft from the first tab wraps to the last');
  h.ok(await popup.evaluate(() => [...document.querySelectorAll('[role=tab]')].filter((t) => t.tabIndex === 0).length === 1), 'popup: only the selected tab is a tab stop');
  h.ok(await popup.evaluate(() => document.getElementById('openOptions').tagName === 'BUTTON'), 'popup: Settings is a real button (keyboard and screen reader)');
  await popup.click('#t-look');

  // ---- the settings page: importing a spreadsheet (xlsx and csv), mapping, and using it ----
  const opt = await context.newPage();
  await opt.goto(`chrome-extension://${id}/src/options.html`);
  h.ok(await opt.isChecked('input[name=consent][value=whisper]'), 'settings: whisper is the default');
  await opt.setInputFiles('#file', path.resolve(__dirname, '..', 'fixtures', 'portfolio.xlsx'));
  await opt.locator('#importStatus.ok').waitFor({ timeout: 8000 });
  {
    const rows = (await opt.locator('#cmds tr').allInnerTexts()).join(' | ');
    h.ok(rows.includes('Right-click menu') && rows.includes('Toolbar button') && /Peek at the selected ANDA number/.test(rows), 'settings: "Ways to start it" lists the right-click menu, the toolbar button and each shortcut');
    h.ok((await opt.locator('#cmdNote').innerText()).includes('another extension or Edge itself'), 'settings: says why a shortcut can be not set');
    h.ok((await opt.innerText('main')).includes('A middle-click trigger is not offered here on purpose'), 'settings: says plainly why the extension has no middle-click trigger');
  }
  h.ok((await opt.locator('#importStatus').innerText()).startsWith('Loaded 3 ANDAs from portfolio.xlsx'), 'settings: the workbook (hidden sheet skipped, dates converted) loads: ' + (await opt.locator('#importStatus').innerText()));
  h.ok(await opt.isChecked('input[name=source][value=imported]'), 'settings: the imported file becomes the data source');
  await popup.reload(); await popup.fill('#num', '700001'); await popup.keyboard.press('Enter');
  await popup.locator('.card').waitFor();
  t = (await popup.locator('.card').innerText()).replace(/\s+/g, ' ');
  h.ok(t.includes('Warfarin tabs') && t.includes('YOUR FILE') && t.includes('Oct 17, 2026') && t.includes('—') === true && /goal date \(Sat Oct 3\) passed/i.test(t), 'popup: a row from the workbook shows as the person\'s data, with an honest "—" for unknown progress');
  h.ok(t.includes('PSG not available: no PSG date in your data') && t.includes('RLD LABEL not available: no Drugs@FDA file chosen'), 'card: with your own data and no source, both reference lines say briefly that the date is not available');
  await popup.fill('#num', '216482'); await popup.keyboard.press('Enter');
  await popup.locator('#lookMsg').waitFor({ state: 'visible' });
  h.ok((await popup.locator('#lookMsg').innerText()).includes('216482 is not in your portfolio (portfolio.xlsx)'), 'with the sheet as the source, demo numbers are not found (no silent mixing)');
  const csv = path.join(os.tmpdir(), 'Tracker.csv');
  fs.writeFileSync(csv, 'Notes\nANDA,Product,Goal\n910001,Test drug,2027-03-02\n');
  await opt.setInputFiles('#file', csv);
  await opt.waitForFunction(() => document.getElementById('importStatus').textContent.startsWith('Loaded 1 ANDA from Tracker.csv'), null, { timeout: 8000 });
  h.ok(true, 'settings: a CSV with a title row above the header loads');
  const odd = path.join(os.tmpdir(), 'Odd.csv');
  fs.writeFileSync(odd, 'Col A,Col B\n920001,2027-04-05\n');
  await opt.setInputFiles('#file', odd);
  await opt.waitForFunction(() => document.getElementById('importStatus').textContent.includes('No column for the ANDA number'), null, { timeout: 8000 });
  h.ok(true, 'settings: when no column looks like an ANDA number it says so and asks you to choose');
  await opt.selectOption('select[data-key=number]', 'Col A');
  await opt.waitForFunction(() => document.getElementById('importStatus').textContent.startsWith('Loaded 1 ANDA from Odd.csv'), null, { timeout: 8000 });
  h.ok(true, 'settings: choosing the column fixes it');
  await opt.selectOption('select[data-key=goal]', 'Col B');
  await opt.waitForTimeout(300);
  await popup.fill('#num', '920001'); await popup.keyboard.press('Enter');
  await popup.locator('.card').waitFor();
  h.ok((await popup.locator('.card').innerText()).includes('Apr 5, 2027'), 'settings: the chosen mapping is used by the card');
  await opt.setInputFiles('#file', path.resolve(__dirname, '..', '..', 'manifest.json'));
  await opt.locator('#importStatus.bad').waitFor();
  h.ok((await opt.locator('#importStatus').innerText()).includes('not supported'), 'settings: a file of the wrong type is refused in plain words');
  await opt.click('#clearImport').catch(() => {});
  await opt.waitForTimeout(300);
  h.ok(await opt.isChecked('input[name=source][value=demo]'), 'settings: removing the spreadsheet returns to the demo records');

  // ---- the app connection is off by default and says so ----
  await opt.check('input[name=source][value=app]');
  await opt.waitForTimeout(300);
  h.ok((await opt.locator('#sourceStatus').innerText()).includes('connection is turned off'), 'without the connection permission the app source says it is off, and how to turn it on: ' + (await opt.locator('#sourceStatus').innerText()));
  await h.setSettings(worker, { dataSource: 'demo' });

  await context.close(); server.close();
  process.exit(h.done() ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(2); });
