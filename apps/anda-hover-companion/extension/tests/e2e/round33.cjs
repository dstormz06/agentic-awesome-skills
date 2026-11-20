// Round 33 end to end, in a real browser engine: the first steps after an installation, the card's data line with its age and its
// STALE mark, the time zone, the line written again as a minute begins, what is switched on, and the version.
const h = require('./harness.cjs');
const path = require('path'), fs = require('fs'), os = require('os');

const PAGE = `<!doctype html><meta charset="utf-8"><title>Huddle prep</title><body style="font:16px sans-serif;padding:40px">
<p>Please check <span id="a1">930001</span> and <span id="demo">216482</span>.</p></body>`;
const DAY = 86400000;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const { server, base } = await h.servePages({ '/': { body: PAGE } });
  const run = await h.launch({ timezoneId: 'America/New_York', keepFirst: true });
  const { context, worker, id } = run;
  const text = async (loc) => (await loc.first().innerText()).replace(/\s+/g, ' ');

  // ---- after an installation the first steps open once, in a tab, at step 1 ----
  const first = run.first;
  h.ok(!!first, 'after an installation the first steps open in a tab');
  h.ok((await text(first.locator('#fsPlace'))).toUpperCase() === 'STEP 1 OF 5' && (await text(first.locator('#fsTitle'))) === 'What this is', 'the guide starts at step 1 of 5, "What this is"');
  h.ok(await first.evaluate(() => document.activeElement && document.activeElement.id === 'fsTitle'), 'focus is on the title of the step');
  h.ok((await text(first.locator('#fsLines'))).includes('FICTIONAL DEMO') && (await text(first.locator('#fsLines'))).includes('makes no network requests'), 'step 1 says the sample data is fictional and that nothing goes over the network');
  h.ok(await first.locator('#fsBack').isDisabled(), 'Back is off on the first step');
  h.ok((await worker.evaluate(async () => (await chrome.storage.local.get('firstSteps')).firstSteps)).seen === true, 'the browser keeps that the guide was shown');
  h.ok(await first.locator('#fsNote').isHidden(), 'and no note about forgetting is shown');
  h.ok((await first.locator('#help').getAttribute('aria-expanded')) === 'true', 'the Help button says the guide is open');

  // step 2: the sample card, always the fictional ANDA
  await first.click('#fsNext');
  h.ok((await text(first.locator('#fsTitle'))) === 'Try it on the sample number' && (await text(first.locator('#fsSay'))) === 'Step 2 of 5: Try it on the sample number', 'Next goes to step 2 and says so to a screen reader');
  const l2 = await text(first.locator('#fsLines'));
  h.ok(/select the number 216482, then (press \S+|right-click it and choose “ANDA Companion \(Notula\): peek at…” \(no shortcut is set at the moment\))\. Or press/.test(l2), 'step 2 names the way to start that really works here: ' + l2.slice(0, 120));
  await first.click('#fsAct');
  await first.locator('#fsCard .card').waitFor({ timeout: 5000 });
  let t = await text(first.locator('#fsCard .card'));
  h.ok(t.includes('ANDA 216482') && t.includes('FICTIONAL DEMO') && t.includes('Metformin HCl ER Tablets, 500 mg'), 'the button shows the sample card on the page, marked FICTIONAL DEMO');
  h.ok(t.includes('Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (sample data, which does not change)'), 'the sample card says where its data is from and that it does not change');
  h.ok((await first.locator('#fsCard .fresh.stale').count()) === 0, 'sample data is never marked STALE');
  h.ok((await text(first.locator('#fsSay'))).includes('fictional sample data'), 'and a screen reader is told the card is shown');
  h.ok(await first.evaluate(() => { const c = document.getElementById('fsCard').shadowRoot.querySelector('.card'); return c && c.getRootNode().activeElement === c; }), 'the sample card takes the keyboard when it is shown');
  h.ok((await first.locator('#fsCard button', { hasText: 'Sister ANDA' }).count()) === 0, 'the sample card offers no sister ANDA: it stays on the sample');
  await first.keyboard.press('e');
  h.ok((await first.locator('#fsCard .card.wide').count()) === 1, 'E expands the sample card where it is, as its key hints say');
  await first.keyboard.press('Escape');
  h.ok((await first.locator('#fsCard').isHidden()) && (await first.locator('#first').isVisible()) && await first.evaluate(() => document.activeElement.id === 'fsAct'), 'Esc closes the sample card only; the guide stays open and the keyboard goes back to its button');
  await first.click('#fsAct'); await first.locator('#fsCard .card').waitFor({ timeout: 5000 });
  h.ok(await first.evaluate(() => getComputedStyle(document.querySelector('.fsnav')).alignItems === 'center'), 'the card\'s styles stay inside the card: the page\'s own rows are as they were');

  // step 3: own data
  await first.click('#fsNext');
  h.ok((await first.locator('#fsCard').isHidden()) && (await text(first.locator('#fsTitle'))) === 'Your own data', 'step 3: the sample card is put away');
  h.ok((await text(first.locator('#fsLines'))).includes('kept in this browser profile on this PC. It is never uploaded.'), 'step 3 says where the file is kept');
  await first.click('#fsAct');
  h.ok(await first.evaluate(() => document.activeElement && document.activeElement.id === 'file'), 'its button moves to "My spreadsheet"');

  // step 4: what is switched on
  await first.click('#fsNext');
  h.ok((await text(first.locator('#fsLines'))).includes('At the moment: Offline · Local only'), 'step 4 says what is switched on now');
  h.ok(await first.locator('#fsAct').isHidden(), 'a step without a button shows none');

  // step 5, Done, Help again, Esc
  await first.click('#fsNext');
  h.ok((await text(first.locator('#fsNext'))) === 'Done' && (await text(first.locator('#fsPlace'))).toUpperCase() === 'STEP 5 OF 5', 'the last step ends with Done');
  await first.click('#fsBack');
  h.ok((await text(first.locator('#fsTitle'))) === 'What is switched on', 'Back goes back one step');
  await first.click('#fsNext'); await first.click('#fsNext');
  h.ok((await first.locator('#first').isHidden()) && await first.evaluate(() => document.activeElement.id === 'help'), 'Done closes the guide and focus goes to "Help: first steps"');
  h.ok((await first.locator('#help').getAttribute('aria-expanded')) === 'false', 'and the Help button says it is closed');
  await first.keyboard.press('Enter');
  h.ok((await first.locator('#first').isVisible()) && (await text(first.locator('#fsPlace'))).toUpperCase() === 'STEP 1 OF 5', '"Help: first steps" opens it again, from step 1');
  await first.click('#fsNext'); await first.click('#help');
  h.ok((await text(first.locator('#fsPlace'))).toUpperCase() === 'STEP 2 OF 5', 'pressing Help while it is open keeps its place');
  await first.keyboard.press('Escape');
  h.ok(await first.locator('#first').isHidden(), 'Esc closes the guide');

  // not again by itself: the Settings page opened later, and the toolbar window's Help
  const opt = await context.newPage();
  await opt.goto(`chrome-extension://${id}/src/options.html`);
  await opt.locator('#conn').waitFor();
  await opt.waitForFunction(() => document.getElementById('conn').textContent.length > 5);
  h.ok(await opt.locator('#first').isHidden(), 'the Settings page opened later does not show the guide by itself');
  const popup = await context.newPage();
  await popup.goto(`chrome-extension://${id}/src/popup.html`);
  const [helped] = await Promise.all([context.waitForEvent('page'), popup.click('#openHelp')]);
  await helped.locator('#first:not([hidden])').waitFor({ timeout: 5000 });
  h.ok(helped.url().endsWith('/src/options.html#first'), 'the toolbar window\'s "Help: first steps" opens the guide');
  await helped.close(); await first.close();

  // ---- what is switched on, and the version ----
  h.ok((await text(popup.locator('#conn'))) === 'Offline · Local only', 'the toolbar window says "Offline · Local only" at its foot');
  h.ok((await text(opt.locator('#conn'))).startsWith('Offline · Local only. The extension makes no network requests'), 'the Settings page says it at its top, in full');
  h.ok((await text(opt.locator('#version'))) === 'Version 0.2.0' && (await text(opt.locator('#changes'))).includes('Version 0.2.0 · Round 33 (installed)'), 'the version and what changed are on the Settings page');

  // ---- the person's file: source, as of with the zone, how long ago, STALE past the days chosen ----
  const csv = path.join(os.tmpdir(), 'Tracker.csv');
  fs.writeFileSync(csv, 'ANDA,Product,Applicant,Goal date,Forecast date,Status\n930001,Alpha tabs,Birch,2031-10-06,2031-10-20,In review\n');
  const saved = Math.floor(Date.now() / 60000) * 60000 - 9 * DAY - 5 * 60000;      // nine days and five minutes before the minute now running, on a minute
  fs.utimesSync(csv, new Date(saved), new Date(saved));
  await opt.setInputFiles('#file', csv);
  await opt.locator('#importStatus.ok').waitFor({ timeout: 8000 });
  const st = await text(opt.locator('#importStatus'));
  h.ok(/^Loaded 1 ANDA from Tracker\.csv \(file saved [A-Z][a-z]{2} \d{1,2}, \d{4}, \d{1,2}:\d\d [AP]M E[SD]T\)\./.test(st), 'the time the file was saved is written with its zone: ' + st);
  h.ok(/^Now: [A-Z][a-z]{2} \d{1,2}, \d{4}, \d{1,2}:\d\d [AP]M E[SD]T$/.test(await text(opt.locator('#timeNow'))), 'the Time section shows the time now, with the zone');
  await popup.reload(); await popup.fill('#num', '930001'); await popup.keyboard.press('Enter');
  await popup.locator('.card').waitFor();
  let line = await text(popup.locator('.card .fresh'));
  h.ok(/^Your portfolio file · Tracker\.csv · as of [A-Z][a-z]{2} \d{1,2}, \d{4}, \d{1,2}:\d\d [AP]M E[SD]T \(9 days ago\) · STALE: older than 7 days$/.test(line), 'the card says where the data is from, as of when, how long ago, and STALE: ' + line);
  h.ok((await popup.locator('.card .fresh.stale').count()) === 1, 'and the line is drawn as stale');
  h.ok((await text(popup.locator('.card'))).includes('This data is 9 days old'), 'the watch line agrees with it');
  await opt.selectOption('#stale', '14');
  await opt.waitForTimeout(300);
  await popup.fill('#num', '930001'); await popup.keyboard.press('Enter');
  await popup.waitForFunction(() => { const f = document.querySelector('.card .fresh'); return f && !f.textContent.includes('STALE'); }, null, { timeout: 5000 });
  line = await text(popup.locator('.card .fresh'));
  h.ok(/\(9 days ago\)$/.test(line) && (await popup.locator('.card .fresh.stale').count()) === 0 && !(await text(popup.locator('.card'))).includes('This data is 9 days old'), 'with 14 days chosen the same data is no longer STALE, on the line and in the watch line');
  h.ok((await worker.evaluate(async () => (await chrome.storage.local.get('settings')).settings)).schema === 2, 'the stored settings carry the number of their form');

  // ---- another zone: written differently, nothing stored changes ----
  const before = await worker.evaluate(async () => (await chrome.storage.local.get('imported')).imported.savedMs);
  await opt.selectOption('#tz', 'UTC');
  await opt.waitForFunction(() => document.getElementById('importStatus').textContent.includes(' UTC)') && document.getElementById('timeHint').textContent.startsWith('Times are now'), null, { timeout: 5000 });
  h.ok((await text(opt.locator('#timeHint'))) === 'Times are now written in UTC time (UTC). No stored time was changed.', 'choosing a zone says what changed and that nothing stored did');
  h.ok(/ UTC$/.test(await text(opt.locator('#timeNow'))), 'the time now is written in the zone chosen');
  h.ok((await worker.evaluate(async () => (await chrome.storage.local.get('imported')).imported.savedMs)) === before && before === saved, 'the stored moment is the same as before');
  await opt.selectOption('#tz', '');
  await opt.waitForFunction(() => /E[SD]T\)/.test(document.getElementById('importStatus').textContent) && document.getElementById('timeHint').textContent.includes('this computer'), null, { timeout: 5000 });
  h.ok(/^Times are now written in this computer's time zone \(America\/New York, E[SD]T\)\. No stored time was changed\.$/.test(await text(opt.locator('#timeHint'))), '"Automatic" goes back to this computer\'s zone and names it');
  await opt.selectOption('#stale', '7');
  await opt.waitForTimeout(300);

  // ---- the line is written again as a minute begins: on a page and in the toolbar window, without the card being drawn again ----
  const page = await context.newPage();
  await page.goto(base + '/'); await page.bringToFront();
  await h.setSettings(worker, { consent: 'full', autoHideSeconds: 0 });
  // if the minute is about to end, wait for the next one, so that both cards are read and compared inside one minute
  if (60000 - (Date.now() % 60000) < 6000) await sleep(60000 - (Date.now() % 60000) + 500);
  await h.select(page, '#a1'); await h.trigger(worker, 'peek');
  await page.locator('.card').waitFor({ timeout: 5000 });
  await popup.fill('#num', '930001'); await popup.keyboard.press('Enter');
  await popup.waitForFunction(() => { const f = document.querySelector('.card .fresh'); return f && f.textContent.includes('STALE'); }, null, { timeout: 5000 });
  const minutesNow = () => Math.floor((Date.now() - saved) / 60000) - 9 * 1440;
  const m0 = minutesNow();
  const pageLine0 = await text(page.locator('.card .fresh'));
  h.ok(pageLine0.includes('(9 days ago) · STALE: older than 7 days'), 'the card on a page carries the same line: ' + pageLine0);
  await page.locator('.card button[aria-label="Expand the card"]').click();
  await page.evaluate(() => { const c = document.querySelector('[data-anda-companion]').shadowRoot.querySelector('.card'); c.dataset.kept = 'yes'; });
  // a file saved 59 minutes before the hour mark makes the visible words change within the test: use a second file for that
  const csv2 = path.join(os.tmpdir(), 'Recent.csv');
  fs.writeFileSync(csv2, 'ANDA,Product,Goal date\n930001,Alpha tabs,2031-10-06\n');
  const saved2 = Math.floor(Date.now() / 60000) * 60000 - 5 * 60000;      // five minutes before the minute now running, on a minute
  fs.utimesSync(csv2, new Date(saved2), new Date(saved2));
  await opt.setInputFiles('#file', csv2);
  await opt.waitForFunction(() => document.getElementById('importStatus').textContent.startsWith('Loaded 1 ANDA from Recent.csv'), null, { timeout: 8000 });
  await popup.fill('#num', '930001'); await popup.keyboard.press('Enter');
  await popup.waitForFunction(() => { const f = document.querySelector('.card .fresh'); return f && f.textContent.includes('Recent.csv'); }, null, { timeout: 5000 });
  const ageNow = () => Math.floor((Date.now() - saved2) / 60000);
  const a0 = ageNow(), p0 = await text(popup.locator('.card .fresh')), clock0 = await text(opt.locator('#timeNow'));
  h.ok(p0.endsWith(`(${a0} minutes ago)`) || p0.endsWith(`(${a0 + 1} minutes ago)`), 'a file saved minutes ago says so: ' + p0);
  const wait = 60000 - (Date.now() % 60000) + 2500;
  await sleep(wait);
  const a1 = ageNow();
  const p1 = await text(popup.locator('.card .fresh')), g1 = await text(page.locator('.card .fresh'));
  h.ok(a1 > a0 && p1.endsWith(`(${a1} minutes ago)`), `in the toolbar window the line was written again as the minute began, with nothing pressed: ${p1}`);
  h.ok(/^Your portfolio file · Tracker\.csv · as of .* \(9 days ago\) · STALE: older than 7 days$/.test(g1), `the card on the page was written again too, for the file it was opened from, although another file was imported meanwhile: ${g1}`);
  h.ok((await text(opt.locator('#timeNow'))) !== clock0, 'the clock under Time on the Settings page moved on with the minute');
  h.ok(await page.evaluate(() => { const c = document.querySelector('[data-anda-companion]').shadowRoot.querySelector('.card'); return c.dataset.kept === 'yes' && c.classList.contains('wide'); }), 'the card on the page was not drawn again: it is the same element, still expanded');
  h.ok((await page.locator('.card .fresh.stale').count()) === 1, 'and it is still drawn as stale: what it shows is the old file\'s data');
  h.ok(m0 === 5 || m0 === 6, 'the first file was nine days and about five minutes old when read');
  await page.keyboard.press('Escape');
  h.ok((await page.locator('.card').count()) === 0, 'Esc closes the card');

  // ---- the desktop app chosen and not allowed: said plainly, nothing sent ----
  await opt.check('input[name=source][value=app]');
  await opt.waitForFunction(() => document.getElementById('conn').classList.contains('silent'), null, { timeout: 5000 });
  h.ok((await text(opt.locator('#conn'))).startsWith('The desktop app on this PC is chosen as the source, and it is not answering, so nothing is sent to it'), 'with the desktop app chosen and not answering, the top of the page says so');
  await popup.reload();
  await popup.waitForFunction(() => document.getElementById('conn').textContent.includes('not answering'), null, { timeout: 5000 });
  h.ok((await text(popup.locator('#conn'))) === 'Desktop app chosen, not answering · nothing is sent', 'and so does the toolbar window');
  await h.setSettings(worker, { dataSource: 'demo', consent: 'whisper', autoHideSeconds: 20 });

  // ---- accessibility scan of the two pages, with the guide open (when the scanner is on this computer) ----
  const axePath = process.env.AXE_PATH;
  if (axePath && fs.existsSync(axePath)) {
    const axe = fs.readFileSync(axePath, 'utf8');
    await opt.reload(); await opt.locator('#conn').waitFor(); await opt.click('#help'); await opt.click('#fsNext'); await opt.click('#fsAct'); await opt.locator('#fsCard .card').waitFor();
    for (const [name, pg] of [['Settings page with the guide and the sample card open', opt], ['toolbar window', popup]]) {
      await pg.evaluate(axe);
      const v = await pg.evaluate(async () => (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] })).violations.map((x) => `${x.id} (${x.nodes.length}): ${x.nodes[0].target}`));
      h.ok(v.length === 0, `accessibility scan, ${name}: ${v.length === 0 ? 'no violations' : v.join('; ')}`);
    }
  } else console.log('NOTE the accessibility scan was not run: AXE_PATH is not set');

  await context.close();

  // ---- a second start with the same profile is not an installation: no guide tab ----
  const again = await h.launch({ timezoneId: 'America/New_York', userData: run.userData, extDir: run.extDir, keepFirst: true, firstWaitMs: 4000 });
  h.ok(again.first === null && !again.context.pages().some((p) => p.url().includes('options.html')), 'starting the browser again does not open the guide');
  await again.context.close();

  server.close();
  process.exit(h.done() ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(2); });
