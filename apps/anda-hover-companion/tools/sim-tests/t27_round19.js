// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 19 in the browser simulation: the N and A prefixes, the smaller answer figure, the new default shortcuts, card details with Copy,
// notices near the clock (watch-back and division news), News and alerts, and one question before sending. Attack cases at the end.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const AXE = AXE_FILE;
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 800 }, permissions: ['clipboard-read', 'clipboard-write'], hasTouch: !!opts.touch });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    if (opts.init) await p.addInitScript(opts.init);
    await p.goto(SIM + (opts.all ? '#all' : '')); await p.waitForTimeout(300);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const vis = (p, s) => p.isVisible(s);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 520, y: 60 }, forceFull: true }), id); await settle(p, 450); };
  const openSettings = async p => { await p.evaluate(() => { showApp('settings'); document.querySelectorAll('#settings details.sec').forEach(d => { d.open = true; }); }); await settle(p, 100); };
  const closeSettings = p => p.evaluate(() => closeApp('settings'));
  const clip = p => p.evaluate(() => navigator.clipboard.readText());

  // ====================================================== A. N is an NDA, A is an ANDA
  let p = await fresh();
  eq(await p.evaluate(() => ['N210793', 'n216482', 'N-216482', 'A216482', 'a216482', 'ANDA 216482', 'IN216482', 'N021234 and A216482'].map(extractNumber)), [null, null, null, '216482', '216482', '216482', '216482', '216482'], 'A1 an N prefix is refused and an A prefix is read as the ANDA number');
  await p.evaluate(() => openLookup({ x: 400, y: 300 })); await settle(p, 100);
  await p.fill('#lookupIn', 'N216482'); await p.keyboard.press('Enter'); await settle(p, 100);
  eq(await txt(p, '#lookupErr'), 'N216482 is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.', 'A2 the lookup box explains an N number');
  await p.fill('#lookupIn', 'A216482'); await p.keyboard.press('Enter'); await settle(p, 500);
  ok(await vis(p, '#card') || await vis(p, '#chip'), 'A3 an A number opens the ANDA as usual');
  ok(/216482/.test(await p.evaluate(() => document.body.innerText)), 'A4 and the number that was read is shown');
  await p.keyboard.press('Escape'); await settle(p, 200);
  const draft = await p.evaluate(() => draftReview('N216482 is on track. A214990 is running late.', demoToday()).findings.map(f => f.text).join(' | '));
  ok(/ANDA 214990: the draft sounds late/.test(draft) && !/216482/.test(draft), 'A5 Review reads A214990 and leaves the NDA N216482 alone');
  await p.close();

  // ====================================================== B. the answer figure is smaller; the new default shortcuts
  p = await fresh();
  await openFull(p, '216482');
  const hero = await p.evaluate(() => ({ n: parseFloat(getComputedStyle(document.getElementById('c-hero-n')).fontSize), u: parseFloat(getComputedStyle(document.getElementById('c-hero-u')).fontSize), text: document.getElementById('c-hero').innerText.replace(/\s+/g, ' ') }));
  ok(hero.n === 30 && hero.u === 15, `B1 the figure is 30 px beside 15 px words (was 46 and 16): ${hero.n}, ${hero.u}`);
  ok(/^14 days later than the goal Goal Oct 3, 2026 · Forecast Oct 17, 2026 · moderate confidence 79 DAYS TO GOAL$/.test(hero.text), 'B2 and still reads as one answer with its dates and confidence (and, since round 24, the days to the goal date beside it)');
  await p.keyboard.press('Escape'); await settle(p, 200);
  eq(await p.evaluate(() => [state.keys.draft, state.keys.list]), ['Ctrl+Alt+Shift+R', 'Ctrl+Alt+Shift+L'], 'B3 Review and My to-do list default to shortcuts Word does not use');
  await p.keyboard.press('Control+Alt+Shift+R'); await settle(p, 300);
  ok(await vis(p, '#draft'), 'B4 Ctrl+Alt+Shift+R opens Review');
  await p.keyboard.press('Escape'); await settle(p, 150);
  await p.keyboard.press('Control+Alt+D'); await settle(p, 300);
  ok(!(await vis(p, '#draft')), 'B5 Ctrl+Alt+D is left to other programs');
  await p.close();

  // ====================================================== C. card details
  p = await fresh();
  await openFull(p, '216482');
  ok(!(await vis(p, '#x-details')), 'C1 the details are not on the card until it is expanded');
  await p.keyboard.press('e'); await settle(p, 300);
  ok(await vis(p, '#x-details'), 'C2 expanding shows the details');
  ok(await p.evaluate(() => { const c = document.getElementById('card').getBoundingClientRect(), d = document.getElementById('x-details-d').getBoundingClientRect(); return d.top >= c.top && d.top < c.bottom; }), 'C3 and brings them into view');
  const rows = await p.$$eval('#x-details .drow', rs => rs.map(r => [r.querySelector('.k').textContent, r.querySelector('.v').textContent, !!r.querySelector('button')]));
  eq(rows.map(r => r[0]), ['Applicant', 'Established name', 'Project description', 'US agent', 'US agent email', 'Applicant email', 'My notes', 'Latest 356h', 'Module 3.2.P.1'], 'C4 the nine default fields, in a fixed order');
  eq(rows[0][1], 'Meridian Pharmaceuticals, Inc. (fictional) (Meridian)', 'C5 the applicant carries its short name from the sheet in parentheses');
  eq(rows.filter(r => r[1] === 'not in your sheet').map(r => [r[0], r[2]]), [['Latest 356h', false], ['Module 3.2.P.1', false]], 'C6 a column the sheet lacks says so and has no Copy button');
  eq(await txt(p, '#x-details-n'), '· 7 of 9 in your sheet', 'C7 the heading counts what the sheet has');
  await p.click('#x-details [data-copy=agentEmail]'); await settle(p, 200);
  eq(await clip(p), 'us.agent@northgate-agents.example', 'C8 Copy puts exactly that value on the clipboard');
  eq(await txt(p, '#x-details-status'), 'Copied: US agent email.', 'C9 and says what was copied (round 33: a name keeps its capitals)');
  await p.click('#x-details-all'); await settle(p, 200);
  const all = await clip(p);
  ok(all.startsWith('ANDA 216482\nApplicant: Meridian Pharmaceuticals, Inc. (fictional) (Meridian)\n') && all.includes('\nUS agent email: us.agent@northgate-agents.example') && !all.includes('not in your sheet') && all.split('\n').length === 8, 'C10 Copy all gives one labeled line per field the sheet has, and skips the missing ones');
  ok(await p.evaluate(() => [...document.querySelectorAll('#x-details button')].every(x => /^Copy /.test(x.getAttribute('aria-label')))), 'C11 every Copy button names its field for a screen reader');
  await p.keyboard.press('Escape'); await settle(p, 200);
  // the other record: different gaps
  await openFull(p, '214990'); await p.keyboard.press('e'); await settle(p, 300);
  const r2 = await p.$$eval('#x-details .drow', rs => Object.fromEntries(rs.map(r => [r.querySelector('.k').textContent, r.querySelector('.v').textContent])));
  ok(r2['Applicant email'] === 'not in your sheet' && r2['My notes'] === 'not in your sheet' && /356h_2026-06-12\.pdf/.test(r2['Latest 356h']), 'C12 another record shows its own gaps and its own document locations');
  await p.keyboard.press('Escape'); await settle(p, 200);
  // My fields
  await openSettings(p);
  eq(await p.$$eval('#setFields input', cs => cs.length), 12, 'C13 Settings offers twelve columns');
  await p.uncheck('#setFields input[data-field=myNotes]'); await p.check('#setFields input[data-field=fei]'); await p.check('#setFields input[data-field=teamLead]');
  await closeSettings(p);
  await openFull(p, '216482'); await p.keyboard.press('e'); await settle(p, 300);
  const labels = await p.$$eval('#x-details .drow .k', ks => ks.map(k => k.textContent));
  ok(!labels.includes('My notes') && labels.includes('Team lead') && labels.includes('Facility FEI') && labels.indexOf('Team lead') < labels.indexOf('Facility FEI'), 'C14 choosing columns changes the card, in the fixed order');
  await p.keyboard.press('Escape'); await settle(p, 150);
  // backup and restore
  const bk = await p.evaluate(() => { const j = backupText(), o = JSON.parse(j); const before = [...state.cardFields]; state.cardFields = []; importBackup(j); return { has: Array.isArray(o.settings ? o.settings.cardFields : o.cardFields) || /"cardFields"/.test(j), same: JSON.stringify(state.cardFields) === JSON.stringify(before), json: j.length }; });
  ok(bk.has && bk.same, 'C15 the chosen columns travel in a backup and come back');
  const hostile = await p.evaluate(() => { const o = JSON.parse(backupText()); const set = o.settings || o; set.cardFields = ['fei', '<img src=x onerror=window.__pwn=1>', 'nope', 'applicant', 42]; set.noticesPerDay = 999; set.watchBack = 'yes'; set.divisionNews = true; importBackup(JSON.stringify(o)); return { f: state.cardFields, cap: state.watchCap, w: state.watchOn, n: state.newsOn, pwn: window.__pwn || 0 }; });
  eq([hostile.f, hostile.cap, hostile.w, hostile.n, hostile.pwn], [['applicant', 'fei'], 3, false, false, 0], 'C16 a hand-edited backup keeps only known columns, a known limit, and cannot switch division news on without a document');
  await p.close();

  // ====================================================== D. notices near the clock: watch-back
  p = await fresh();
  ok(!(await vis(p, '#whisper')), 'D1 nothing speaks until it is switched on');
  eq(await p.evaluate(() => [state.watchOn, state.newsOn, state.watchCap]), [false, false, 3], 'D2 both are off by default, with a limit of three a day');
  await openSettings(p);
  await p.click('#watchNow'); await settle(p, 200);
  ok(!(await vis(p, '#whisper')) && (await txt(p, '#watchStatus')) === 'Notices are off.', 'D3 Check now says they are off and shows nothing');
  await p.check('#setWatch'); await settle(p, 300);
  ok(await vis(p, '#whisper'), 'D4 switching watch-back on shows the first notice');
  // round 42: the notices from the data follow the timeline checks, most urgent first, and say whether they are immediate or longer term
  eq([await txt(p, '#whKind'), await txt(p, '#whText')], ['FROM YOUR DATA · IMMEDIATE', await p.evaluate(() => watchItems()[0].text)], 'D5 it is the most urgent item: the number and a short reason');
  ok((await txt(p, '#whText')).startsWith('ANDA 216482: '), 'D5b the notice starts with its ANDA number');
  ok(await p.evaluate(() => document.activeElement.id === 'setWatch' && !document.getElementById('whisper').contains(document.activeElement)), 'D6 the notice does not take the keyboard');
  ok(!/dissolution|Ask for/.test(await txt(p, '#whisper')), 'D7 and never shows the person\'s notes');
  const box = await p.evaluate(() => { const r = document.getElementById('whisper').getBoundingClientRect(); return { right: innerWidth - r.right, bottom: innerHeight - r.bottom, top: r.top }; });
  ok(box.right >= 8 && box.right <= 20 && box.bottom >= 48 && box.bottom <= 64, `D8 it sits bottom right, above the taskbar (${Math.round(box.right)} px from the right, ${Math.round(box.bottom)} px from the bottom)`);
  await closeSettings(p);
  await p.click('#whSnooze'); await settle(p, 800);
  ok(await vis(p, '#whisper') && !/at risk/.test(await txt(p, '#whText')), 'D9 Snooze hides that item and the next one follows');
  await p.click('#whClose'); await settle(p, 800);
  ok(await vis(p, '#whisper'), 'D10 Dismiss moves on to the third');
  await p.click('#whClose'); await settle(p, 800);
  ok(!(await vis(p, '#whisper')), 'D11 after three in one day nothing more is shown');
  await openSettings(p);
  ok(/^Today's limit of 3 notices is reached\. \d+ more (is|are) in News and alerts\.$/.test(await txt(p, '#watchStatus')), 'D12 and Settings says the limit was reached and where the rest are: ' + await txt(p, '#watchStatus'));
  await p.click('#watchNow'); await settle(p, 300);
  ok(!(await vis(p, '#whisper')), 'D13 Check now cannot get around the daily limit');
  // a new day (the demo date moves): the count starts again and the snoozed item returns
  await closeSettings(p);
  await p.check('#swToday'); await settle(p, 400);
  ok(await vis(p, '#whisper'), 'D14 on a new day the notices start again');
  ok(/216482|214990/.test(await txt(p, '#whText')), 'D15 with what the data shows on that day: ' + await txt(p, '#whText'));
  // open the card from a notice
  await p.click('#whOpen'); await settle(p, 500);
  ok((await vis(p, '#card') || await vis(p, '#chip')) && !(await vis(p, '#whisper')), 'D16 Open the card opens that ANDA and puts the notice away');
  await p.keyboard.press('Escape'); await settle(p, 200);
  // auto-hide
  await p.uncheck('#swToday'); await settle(p, 300);
  await p.close();
  p = await fresh({ init: () => { window.__fastWhisper = true; } });
  await p.evaluate(() => { state.watchOn = true; state.watchCap = 1; watchCheck(); }); await settle(p, 200);
  ok(await vis(p, '#whisper'), 'D17 a notice is shown');
  const ms = await p.evaluate(() => WHISPER_MS);
  ok(ms >= 8000 && ms <= 15000, `D18 it hides by itself after ${ms / 1000} seconds`);
  await p.evaluate(() => hideWhisper(true)); await settle(p, 700);
  ok(!(await vis(p, '#whisper')), 'D19 with a limit of one a day, no second notice follows');
  await p.evaluate(() => exitApp()); await settle(p, 100);
  eq(await p.evaluate(() => { state.wSeen.clear(); state.wCount = 0; return watchCheck(); }), 0, 'D20 after Exit nothing is checked');
  await p.close();

  // ====================================================== E. division news
  p = await fresh();
  await openSettings(p);
  await p.click('#setNews'); await settle(p, 200);
  ok(!(await p.isChecked('#setNews')) && (await txt(p, '#watchStatus')) === "Please choose the division's news document first.", 'E1 division news cannot be switched on without a document');
  await p.click('#newsPick'); await settle(p, 100);
  ok(/Division-News\.docx/.test(await p.inputValue('#newsFile')), 'E2 choosing the document names it');
  await p.check('#setNews'); await settle(p, 300);
  ok(await vis(p, '#whisper'), 'E3 with the document chosen, news is announced');
  eq([await txt(p, '#whKind'), await txt(p, '#whText'), await p.isVisible('#whSnooze'), await txt(p, '#whOpen')], ['DIVISION NEWS', 'The Thursday huddle moves to 2:00 PM today, same room.', false, 'Open News and alerts'], 'E4 newest first, labeled as division news, with no Snooze');
  await closeSettings(p);
  await p.click('#whOpen'); await settle(p, 300);
  ok(await vis(p, '#hub'), 'E5 Open shows News and alerts');
  const hub = await p.evaluate(() => ({ news: [...document.querySelectorAll('#hubNews .hrow')].map(r => r.innerText.replace(/\s+/g, ' ')), data: document.querySelectorAll('#hubData .hrow').length, note: document.getElementById('hubNewsNote').textContent }));
  eq(hub.news.length, 3, 'E6 the three lines of the last 14 days are listed');
  ok(/^Thu Jul 16 The Thursday huddle moves/.test(hub.news[0]) && hub.news.every(n => /NEW$/.test(n)), 'E7 newest first, each with its date, marked new the first time: ' + hub.news[0]);
  ok(hub.data >= 3 && /^From Division-News\.docx/.test(hub.note), 'E8 with what the data shows beneath, and the source named');
  await p.evaluate(() => { closeApp('hub'); openHub(); }); await settle(p, 200);
  ok(await p.evaluate(() => document.querySelectorAll('#hubNews .new').length === 0), 'E9 the new mark is shown once');
  await p.evaluate(() => closeApp('hub'));
  // a colleague adds a line
  await p.evaluate(() => { hideWhisper(false); state.wCount = 0; });
  await p.click('#newsPush'); await settle(p, 400);
  eq([await vis(p, '#whisper'), await txt(p, '#whText')], [true, 'Building access is limited to the north entrance until 3:00 PM.'], 'E10 a line added to the document is announced');
  // old news is not announced
  eq(await p.evaluate(() => { NEWS_DOC.push({ date: [2026, 6, 1], text: 'Old line' }); NEWS_DOC.push({ date: [2026, 12, 25], text: 'Future line' }); return newsItems().map(n => n.text).filter(t => /Old line|Future line/.test(t)); }), [], 'E11 lines older than 14 days, or dated in the future, are left out');
  // news off: the demo button explains itself
  await p.evaluate(() => { hideWhisper(false); setNewsOn(false); }); await settle(p, 100);
  await p.click('#newsPush'); await settle(p, 300);
  ok(!(await vis(p, '#whisper')) && /Division news is off/.test(await p.evaluate(() => document.body.innerText)), 'E12 with division news off, an added line is not shown and the page says why');
  await p.close();

  // ====================================================== F. one question before sending
  p = await fresh();
  const ask = async text => { await p.evaluate(t => openDraft(t), text); await settle(p, 200); const o = await p.evaluate(() => { const bx = document.querySelector('#drSend .bsend'); return bx ? [bx.querySelector('.q').textContent, bx.classList.contains('gap')] : null; }); await p.evaluate(() => closeApp('draft')); return o; };
  eq(await ask('ANDA 216482 has a goal date of Oct 10, 2026.'), ['For ANDA 216482, which goal date is right: Oct 10, 2026 (your draft) or Oct 3, 2026 (the record)?', true], 'F1 a date that differs from the record becomes the one question');
  eq(await ask('ANDA 216482 is on track.'), ["Is ANDA 216482 really on track? The record's forecast is 14 days later than the goal.", true], 'F2 on-track wording against a late forecast');
  eq(await ask('ANDA 214990 is running late.'), ["Is ANDA 214990 really late or at risk? The record's forecast is 9 days ahead of the goal.", true], 'F3 late wording against an early forecast');
  eq(await ask('ANDA 999999 is on track.'), ['Is 999999 the right ANDA number? It is not in your data.', true], 'F4 a number that is not in the data');
  eq(await ask('ANDA 216482: the goal date is Oct 3, 2026. The summary table is attached.'), ['Is the file attached? The draft mentions an attachment, and this tool cannot see attachments.', true], 'F5 a clean draft that mentions an attachment');
  eq(await ask('ANDA 216482: the goal date is Oct 3, 2026 and the forecast is Oct 17, 2026.'), ['No open gaps.', false], 'F6 a clean draft says No open gaps.');
  eq(await ask(''), null, 'F7 an empty draft asks nothing');
  const order = await ask('ANDA 999999 is on track. ANDA 216482 has a goal date of Oct 10, 2026. See attached.');
  ok(order && /^For ANDA 216482, which goal date/.test(order[0]), 'F8 a difference with the record outranks an unknown number and an attachment');
  const wk = await ask('ANDA 216482: please reply by Monday, July 21, 2026.');
  eq(wk, ['In “Monday, July 21, 2026”, which is right: the day or the date? July 21, 2026 is a Tuesday.', true], 'F9 a weekday that does not match its date is asked about');
  await p.evaluate(() => openDraft('ANDA 216482 is on track.')); await settle(p, 200);
  ok(/Whether to send, and to whom, stays your call\./.test(await txt(p, '#drSend')), 'F10 it says the decision stays with the person');
  await p.fill('#drText', 'ANDA 216482: the goal date is Oct 3, 2026.'); await p.click('#drCheck'); await settle(p, 200);
  ok(/No open gaps\./.test(await txt(p, '#drSend')) && /cannot see recipients or attachments/.test(await txt(p, '#drSend')), 'F11 and a clean result still says what it cannot see');
  eq(await p.evaluate(() => draftReview('ANDA 216482 is on track.', demoToday()).findings.some(f => /ONE QUESTION|No open gaps/.test(f.text))), false, 'F12 the shared review result itself is unchanged (the app and the extension still match it)');
  await p.close();

  // ====================================================== G. menus, reset, sizes, accessibility
  p = await fresh();
  await p.evaluate(() => openMenu()); await settle(p, 150);
  ok(/News and alerts…/.test(await txt(p, '#menu')), 'G1 the tray menu has News and alerts in the focused edition');
  await p.keyboard.press('Escape');
  await p.evaluate(() => { state.newsFile = 'x.docx'; state.watchOn = true; state.newsOn = true; state.watchCap = 5; state.cardFields = ['fei']; watchCheck(); }); await settle(p, 200);
  await p.evaluate(() => { document.getElementById('bkReset').click(); document.getElementById('bkReset').click(); }); await settle(p, 300);
  eq(await p.evaluate(() => [state.watchOn, state.newsOn, state.newsFile, state.watchCap, state.cardFields.length, getComputedStyle(document.getElementById('whisper')).display]), [false, false, '', 3, 9, 'none'], 'G2 Reset all settings switches the notices off, forgets the document and restores the default columns');
  await p.close();
  for (const vp of [{ width: 390, height: 844 }, { width: 1024, height: 768 }, { width: 1366, height: 768 }]) {
    p = await fresh({ viewport: vp, touch: vp.width < 500 });
    await p.evaluate(() => { state.watchOn = true; watchCheck(); }); await settle(p, 250);
    const r = await p.evaluate(() => { const w = document.getElementById('whisper').getBoundingClientRect(); return { l: w.left, r: w.right, t: w.top, b: w.bottom, vw: innerWidth, vh: innerHeight, sx: document.documentElement.scrollWidth }; });
    ok(r.l >= 0 && r.r <= r.vw && r.t >= 0 && r.b <= r.vh && r.sx <= r.vw, `G3 at ${vp.width} px the notice is fully on screen with no sideways scroll`);
    await p.evaluate(() => { hideWhisper(false); openHub(); }); await settle(p, 200);
    const h = await p.evaluate(() => { const w = document.getElementById('hub').getBoundingClientRect(); return w.left >= 0 && w.right <= innerWidth + 1; });
    ok(h, `G4 at ${vp.width} px News and alerts fits the screen`);
    await p.evaluate(() => closeApp('hub'));
    await p.evaluate(() => { openCard('216482', { anchor: { x: 200, y: 60 }, forceFull: true }); }); await settle(p, 450);
    await p.evaluate(() => toggleExpand()); await settle(p, 300);
    const d = await p.evaluate(() => [...document.querySelectorAll('#x-details .drow')].every(row => { const r = row.getBoundingClientRect(), c = document.getElementById('card').getBoundingClientRect(); return r.left >= c.left - 1 && r.right <= c.right + 1; }));
    ok(d, `G5 at ${vp.width} px every detail row stays inside the card`);
    await p.close();
  }
  // largest text: nothing overflows
  p = await fresh();
  await openSettings(p); await p.check('input[name=ts][value=xl]'); await closeSettings(p);
  await openFull(p, '216482'); await p.evaluate(() => toggleExpand()); await settle(p, 300);
  ok(await p.evaluate(() => [...document.querySelectorAll('#x-details .drow .v, #x-details .drow .k')].every(e => e.scrollWidth <= e.clientWidth + 1)), 'G6 at the largest text size no detail is cut off');
  await p.close();
  // axe on the new surfaces
  p = await fresh();
  await p.addScriptTag({ path: AXE });
  const scan = async name => { const r = await p.evaluate(async () => (await axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21aa', 'best-practice'] })).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + (v.nodes[0] ? v.nodes[0].target.join(' ') : ''))); ok(r.length === 0, `G7 accessibility scan, ${name}: ${r.length ? r.join('; ') : 'no violations'}`); };
  await openFull(p, '216482'); await p.evaluate(() => toggleExpand()); await settle(p, 300); await scan('expanded card with details');
  await p.keyboard.press('Escape'); await settle(p, 200);
  await p.evaluate(() => { state.watchOn = true; watchCheck(); }); await settle(p, 250); await scan('a notice near the clock');
  await p.evaluate(() => { hideWhisper(false); state.newsFile = 'Division-News.docx'; state.newsOn = true; openHub(); }); await settle(p, 250); await scan('News and alerts');
  await p.evaluate(() => { closeApp('hub'); openDraft('ANDA 216482 is on track.'); }); await settle(p, 250); await scan('Review with the question');
  await p.evaluate(() => closeApp('draft')); await openSettings(p); await scan('Settings, all sections open');
  await p.close();

  // ====================================================== H. attack cases
  p = await fresh();
  const atk = await p.evaluate(() => {
    const evil = '<img src=x onerror="window.__pwn=1"><script>window.__pwn=2</scr' + 'ipt> ' + 'A'.repeat(5000);
    NEWS_DOC.unshift({ date: [2026, 7, 16], text: evil });
    DATA['216482'].details.project = evil; DATA['216482'].details.applicant = '=HYPERLINK("http://evil.example","x")';
    state.newsFile = 'x.docx'; state.newsOn = true; state.watchOn = false; watchCheck();
    const w = document.getElementById('whText');
    openCard('216482', { anchor: { x: 520, y: 60 }, forceFull: true }); toggleExpand();
    openHub();
    return { len: w.textContent.length, kids: w.children.length, imgs: document.querySelectorAll('#whisper img, #hub img, #x-details img, #whisper script, #hub script, #x-details script').length, pwn: window.__pwn || 0,
      first: w.textContent.slice(0, 20), hubRaw: [...document.querySelectorAll('#hubNews .t')].some(t => t.textContent.startsWith('<img src=x')), detail: document.querySelector('#x-details .drow .v').textContent };
  });
  await settle(p, 400);
  eq([atk.len <= 240, atk.kids, atk.imgs, await p.evaluate(() => window.__pwn || 0)], [true, 0, 0, 0], 'H1 markup in the news document or the sheet is shown as text, never run, and a news line is cut to 240 characters');
  // round 42: lines of the same day are in a fixed order (as in the app), so the hostile line need not be the first notice; it is looked for in News and alerts
  ok(atk.hubRaw, 'H2 the reader sees the raw text');
  ok(atk.detail.startsWith('=HYPERLINK('), 'H3 a formula in a cell is shown as text');
  await p.evaluate(() => { document.querySelector('#x-details [data-copy=applicant]').click(); }); await settle(p, 200);
  eq(await clip(p), '=HYPERLINK("http://evil.example","x") (Meridian)', 'H4 and is copied as that same text');
  const many = await p.evaluate(() => { hideWhisper(false); state.wSeen.clear(); state.wCount = 0; state.watchCap = 5; for (let i = 0; i < 200; i++) NEWS_DOC.unshift({ date: [2026, 7, 16], text: 'Flood line ' + i }); watchCheck(); let shown = 0; for (let i = 0; i < 300; i++) { if (document.getElementById('whisper').style.display === 'block') { shown++; hideWhisper(false); } showNextWhisper(); } return { shown, count: state.wCount }; });
  ok(many.shown <= 5 && many.count <= 5, `H5 two hundred new lines in the document still produce at most five notices a day (${many.shown})`);
  await p.close();
  // clipboard refused: the details say so
  p = await fresh({ init: () => { Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true }); document.execCommand = () => false; } });
  await openFull(p, '216482'); await p.evaluate(() => toggleExpand()); await settle(p, 300);
  await p.click('#x-details [data-copy=established]'); await settle(p, 300);
  eq(await txt(p, '#x-details-status'), 'Your browser did not allow copying. Please select the text and press Ctrl+C.', 'H6 a refused copy is reported, never called Copied');
  await p.close();

  ok(errs.length === 0, `no page errors or console warnings (${JSON.stringify(errs.slice(0, 3))})`);
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close();
  process.exit(fails ? 1 : 0);
})();
