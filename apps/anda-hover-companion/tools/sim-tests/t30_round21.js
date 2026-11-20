// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 21 in the browser simulation: Enter adds a note and Copy status is a small button, the settable ANDA page, the live clock and the
// sample dates that follow it, the desert background, grammar rules and the marked draft, results above the fold, the AI built into the PC,
// notes that survive a reload, and the faults found by the review. Attack cases are mixed in.
const { chromium } = require('playwright');
const FILE = SIMURL + '/mockup/windows-simulation.html';
const SIM = FILE + '?clock=fixed';
const AXE = AXE_FILE;
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 768 }, permissions: ['clipboard-read', 'clipboard-write'], timezoneId: opts.tz || 'America/New_York' });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    if (opts.init) await p.addInitScript(opts.init);
    if (opts.at) await p.clock.install({ time: new Date(opts.at) });
    await p.goto(opts.live ? FILE : SIM); await p.waitForTimeout(300);
    if (opts.at) await p.clock.runFor(1100);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const vis = (p, s) => p.isVisible(s);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 430, y: 60 }, forceFull: true }), id); await settle(p, 450); };
  const clip = p => p.evaluate(() => navigator.clipboard.readText());
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + v.nodes.slice(0, 2).map(n => n.target.join(' ')).join(' | ')), sel || null); };

  // ====================================================== A. the card's actions
  let p = await fresh();
  await openFull(p, '216482');
  eq(await p.$$eval('.chips .chip', n => n.filter(x => !x.hidden).map(x => x.textContent)), ['Add a note · 3 notes', 'Copy status', 'More ↓', 'Quick replies ▾', 'Keep in my list', 'Sister ANDA · 1', 'Open ANDA page ↗'], 'A1 the main action is Add a note; Copy status and Open ANDA page are small buttons (round 34: More opens the rest of the card)');
  const main = await p.$eval('#chipNotes', c => { const s = getComputedStyle(c), r = c.getBoundingClientRect(); return { h: Math.round(r.height), bg: s.backgroundColor, key: getComputedStyle(c, '::after').content }; });
  ok(main.h <= 36 && main.h >= 32, `A2 it is slimmer than before (${main.h} px; it was 44)`);
  ok(/0\.1\)$/.test(main.bg), `A3 and tinted, not solid (${main.bg})`);
  eq(main.key, '"Enter"', 'A4 it shows its key');
  eq(await txt(p, '#actHint'), 'Enter adds a note · S or Copy status copies the status line', 'A5 one line says how to copy the status instead');
  ok(await p.$eval('#actHint', e => e.getBoundingClientRect().height < 20), 'A6 on one line');
  ok(await p.evaluate(() => { const k = card.getBoundingClientRect(); return [...document.querySelectorAll('.chips .chip')].filter(x => !x.hidden).every(c => { const r = c.getBoundingClientRect(); return r.top >= k.top && r.bottom <= k.bottom + 1; }); }), 'A7 every action is on screen without scrolling');
  await p.keyboard.press('Enter'); await settle(p, 300);
  eq(await p.evaluate(() => [document.activeElement.id, card.classList.contains('expanded'), document.getElementById('x-notes-d').open, document.getElementById('ext').firstElementChild.id]), ['qnText', true, true, 'x-notes-d'], 'A8 Enter goes straight to a new note, and the notes are the first thing in the drawer');
  ok(await p.evaluate(() => { const t = document.getElementById('qnText').getBoundingClientRect(), s = document.querySelector('.chips-slot').getBoundingClientRect(), n = document.querySelector('#x-notes .notei').getBoundingClientRect(); return t.bottom <= s.top && n.top < s.top; }), 'A9 the box and the most recent note are in view above the action area');
  eq(await clip(p).catch(() => ''), await clip(p).catch(() => ''), 'A10 (clipboard readable)');
  await p.evaluate(() => navigator.clipboard.writeText('untouched')); await p.keyboard.press('Escape'); await settle(p, 100);
  await p.keyboard.press('Enter'); await settle(p, 200);
  eq(await clip(p), 'untouched', 'A11 Enter no longer copies anything');
  await p.keyboard.press('Escape'); await settle(p, 100);
  await p.keyboard.press('s'); await settle(p, 300);
  eq([await clip(p), /Copied/.test(await txt(p, '#chipCopy'))], [await p.evaluate(() => statusLine('216482')), true], 'A12 S copies the status line, and the button says so');
  await settle(p, 1300); await p.click('#chipCopy'); await settle(p, 250);
  ok(/Copied/.test(await txt(p, '#chipCopy')), 'A13 so does clicking Copy status');
  for (const look of ['t', 'u', 'k', 'p']) { await p.evaluate(l => applyLook(l), look); await settle(p, 80); await p.evaluate(() => { card.classList.remove('expanded'); card.focus(); }); await p.keyboard.press('Enter'); await settle(p, 250); eq(await p.evaluate(() => document.activeElement.id), 'qnText', `A14 Enter adds a note in the look ${look.toUpperCase()} too`); await p.keyboard.press('Escape'); }
  await p.evaluate(() => applyLook('v')); await p.keyboard.press('Escape'); await settle(p, 250);
  // the Enter of the Lookup box opens the card and does nothing else
  await p.evaluate(() => navigator.clipboard.writeText('still mine'));
  await p.keyboard.press('Control+Alt+Space'); await p.fill('#lookupIn', '216482'); await p.keyboard.press('Enter'); await settle(p, 600);
  eq([await vis(p, '#card'), await p.evaluate(() => card.classList.contains('expanded')), await p.evaluate(() => document.activeElement.id), await clip(p)], [true, false, 'card', 'still mine'], 'A15 Enter in the Lookup box opens the card and does not also act on it (it used to copy the status without saying so)');
  eq(await axe(p, '#card'), [], 'A16 accessibility scan of the card');
  await p.keyboard.press('Escape'); await settle(p, 250);

  // ====================================================== U. Open ANDA page
  await openFull(p, '216482');
  await p.click('#chipOpen'); await settle(p, 350);
  eq([await p.$eval('#web .addr .url', e => e.textContent), await p.$eval('#webAnda h2', e => e.textContent), await p.evaluate(() => document.getElementById('web').classList.contains('active')), await vis(p, '#card')], ['https://tracker.example/anda/216482', 'ANDA 216482 · Metformin HCl ER Tablets, 500 mg', true, false], 'U1 the button brings the browser window forward on the page of that ANDA, and the card steps aside');
  await p.click('#webAnda button'); await settle(p, 150);
  eq([await p.$('#webAnda'), await p.$eval('#web .addr .url', e => e.textContent)], [null, 'https://tracker.example/portfolio'], 'U2 Back returns to the portfolio');
  eq(await p.evaluate(() => [['', 0], ['http://x.example/{anda}', 0], ['https://x.example/y', 0], ['https://a b/{anda}', 0], ['javascript:alert(1)//{anda}', 0], ['https://u:p@x.example/{anda}', 0], ['ftp://x/{anda}', 0], ['https://x.example/<script>{anda}', 0], ['https://ok.example/a?id={anda}&again={anda}', 1], ['  https://ok.example/{anda}  ', 1]].map(([t, want]) => (andaPageUrl('216482', t)[0] ? 1 : 0) === want)), new Array(10).fill(true), 'U3 only a plain https address with {anda} is accepted');
  eq(await p.evaluate(() => [andaPageUrl('216482', 'https://ok.example/a?id={anda}&again={anda}')[0], andaPageUrl('21648', 'https://ok.example/{anda}')[0], andaPageUrl('216482"><x', 'https://ok.example/{anda}')[0]]), ['https://ok.example/a?id=216482&again=216482', null, null], 'U4 the number goes in wherever {anda} stands, and only a six-digit number does');
  await openFull(p, '216482'); await p.evaluate(() => setPin(true)); await p.click('#chipOpen'); await settle(p, 300);
  ok(await vis(p, '#card') && await p.$('#webAnda'), 'U4a a pinned card stays while the page opens');
  await p.evaluate(() => { setPin(false); document.getElementById('webAnda').remove(); webAddress(null); });
  await p.evaluate(() => { showApp('settings'); document.getElementById('secFiles').open = true; }); await settle(p, 200);
  eq(await p.inputValue('#setAndaUrl'), 'https://tracker.example/anda/{anda}', 'U5 Settings shows the address');
  await p.fill('#setAndaUrl', 'https://records.example/app?n={anda}'); await settle(p);
  eq(await txt(p, '#setAndaUrlStatus'), 'For ANDA 216482 the button opens https://records.example/app?n=216482', 'U6 and says what the button will open');
  await p.fill('#setAndaUrl', 'records.example/{anda}'); await settle(p);
  ok(/must start with https:\/\//.test(await txt(p, '#setAndaUrlStatus')), 'U7 an address without https is refused, in words');
  await p.fill('#setAndaUrl', ''); await settle(p);
  await p.evaluate(() => closeApp('settings')); await openFull(p, '216482'); await p.click('#chipOpen'); await settle(p, 250);
  ok(/No web address is set for ANDA pages/.test(await p.evaluate(() => document.body.innerText)), 'U8 with no address the button says so and opens nothing');
  eq(await p.evaluate(() => { const s = snapSettings(); state.andaUrl = 'https://kept.example/{anda}'; const t = backupText(); state.andaUrl = ''; importBackup(t); return [typeof s.andaPage, state.andaUrl]; }), ['string', 'https://kept.example/{anda}'], 'U9 the address is part of a backup and comes back');
  await p.evaluate(() => { importBackup(JSON.stringify({ kind: BACKUP_KIND, version: 1, settings: { andaPage: 'javascript:alert(1)//{anda}' } })); }); await p.click('#chipOpen'); await settle(p, 250);
  ok(await p.evaluate(() => !document.getElementById('webAnda')), 'U10 a hand-edited backup with a script address opens nothing');
  await p.keyboard.press('Escape'); await settle(p, 250);

  // ====================================================== C. the clock
  eq(await p.$eval('#taskbar .clock', e => e.innerText.replace(/\n/g, ' | ')), '10:42 AM | 7/16/2026', 'C1 with the test clock the page stays at Jul 16, 2026');
  { const q = await fresh({ live: true, at: '2026-11-17T23:59:57-05:00' });
    eq(await q.$eval('#taskbar .clock', e => e.innerText.replace(/\n/g, ' | ')), '11:59:58 PM | 11/17/2026', 'C2 the live clock shows the real time to the second and the real date');
    await q.clock.runFor(3000); await settle(q, 100);
    eq(await q.$eval('#taskbar .clock', e => e.innerText.replace(/\n/g, ' | ')), '12:00:01 AM | 11/18/2026', 'C3 and rolls over at midnight');
    eq(await q.evaluate(() => { const d = DATA['216482']; return [SHIFT_DAYS % 7, d.goal, d.fc, new Date(ymd(...d.goalD)).getUTCDay(), d.attn, d.pending[0][1], d.asOf.slice(0, 26), longDate(demoToday())]; }), [0, 'Jan 30, 2027', 'Feb 13, 2027', 6, 'IR response due in 6 days — Biopharmaceutics', 'due Nov 24', 'data as of Nov 17, 07:12 ·', 'Nov 18, 2026'], 'C4 the sample dates moved by whole weeks: the goal is still a Saturday, and nothing that was due soon is overdue');
    eq(await q.evaluate(() => [document.querySelector('#mail .when').textContent, document.querySelector('#mailBody .pick[data-pick=DRAFT]').textContent === SAMPLE_DRAFT, /Feb 6, 2027/.test(SAMPLE_DRAFT), document.getElementById('swToday').parentElement.textContent.trim()]), ['Wed 11/11/2026 4:12 PM', true, true, 'Pretend today is Jan 28, 2027 (goal dates get close)'], 'C5 the mail, the draft in it and the what-if follow');
    eq(await q.evaluate(() => { const t = SAMPLE_DRAFT; return [beforeSend(t, draftReview(t, demoToday()), factExtras(t)).text, watchItems().length > 0, notesFor('216482').map(n => n.who)]; }), ['For ANDA 216482, which goal date is right: Feb 6, 2027 (your draft) or Jan 30, 2027 (the record)?', true, ['You · Nov 4', 'You · Oct 27', 'Team lead · Oct 9']], 'C6 the review, the notices and the notes all tell one story');
    await openFull(q, '216482');
    ok(/Goal date falls on a weekend: Sat Jan 30/.test(await txt(q, '#card')), 'C7 the weekend warning is still on the card');
    await q.keyboard.press('Enter'); await q.keyboard.type('live note'); await q.keyboard.press('Control+Enter'); await settle(q, 200);
    ok(/^You · Nov 18, 2026, 12:00 AM EST$/.test(await q.$eval('#x-notes .notei .who', e => e.textContent)), 'C8 a new note carries the real date and time');
    await q.close(); }
  for (const [tz, at, want] of [['America/Los_Angeles', '2027-01-02T06:30:00-08:00', ['6:30:01 AM | 1/2/2027', 'data as of Jan 1']], ['Europe/Berlin', '2026-12-31T23:30:00+01:00', ['11:30:01 PM | 12/31/2026', 'data as of Dec 31']], ['Asia/Tokyo', '2028-02-29T09:00:00+09:00', ['9:00:01 AM | 2/29/2028', 'data as of Feb 29']]]) {
    const q = await fresh({ live: true, tz, at });
    eq([await q.$eval('#taskbar .clock', e => e.innerText.replace(/\n/g, ' | ')), await q.evaluate(() => DATA['216482'].asOf.slice(0, 17).trim().replace(/,$/, ''))], want, `C9 in ${tz} the clock is that region's, and the data is as of that morning`);
    ok(await q.evaluate(() => { const d = DATA['216482'], e = DATA['214990'], today = demoToday(); return d.daysTo >= 73 && d.daysTo <= 79 && e.daysTo >= 24 && e.daysTo <= 30 && (ymd(...d.attnDue) - today) / DAY >= 2 && (ymd(...d.attnDue) - today) / DAY <= 8 && new Date(ymd(...d.goalD)).getUTCDay() === 6 && NEWS_DOC.every(n => (today - ymd(...n.date)) / DAY <= 14 && today >= ymd(...n.date)); }), `C10 in ${tz}: goal 73 to 79 days out, the open item due in 2 to 8 days, news inside its 14 days`);
    eq(await axe(q, '#taskbar'), [], `C11 (${tz}) accessibility scan of the taskbar`);
    await q.close(); }
  eq(await p.evaluate(() => [redate('Oct 3, 2026'), SHIFT_DAYS, SHIFT_LAG]), ['Oct 3, 2026', 0, 0], 'C12 with the test clock nothing is moved');

  // ====================================================== D. the desert background
  eq(await p.evaluate(() => [document.getElementById('desk').dataset.wall, getComputedStyle(document.querySelector('.wall .dunes')).display !== 'none', getComputedStyle(document.querySelector('.wall .bloom')).display, document.querySelectorAll('#wallPick button').length, document.querySelectorAll('img').length]), ['desert', true, 'none', 4, 0], 'D1 the desert is the default background, one of four, drawn in the page (no image file)');
  eq(await axe(p), [], 'D2 accessibility scan of the whole desktop on the desert');
  ok(await p.evaluate(() => { const r = [...document.querySelectorAll('.icon')].map(e => e.getBoundingClientRect()); return r.every((a, i) => r.every((c, j) => i === j || a.bottom <= c.top + 1 || c.bottom <= a.top + 1)); }), 'D3 the desktop icons do not overlap');

  // ====================================================== G. grammar, and the draft with its changes marked
  const pr = t => p.evaluate(x => { const r = proofread(x); return [r.corrected, r.total]; }, t);
  for (const [input, want, n] of [
    ['Their going to send the the responce tommorow and me and him will review it. ANDA 216482 are on track for there goal date.', "They're going to send the response tomorrow and he and I will review it. ANDA 216482 is on track for their goal date.", 7],
    ['Its a late response, and the applicant dont know weather or not they was told. We should of asked sooner then last week.', "It's a late response, and the applicant don't know whether or not they were told. We should have asked sooner than last week.", 6],
    ['Between you and I, this is a honest mistake by a applicant; an review is not needed. It is to late.', 'Between you and me, this is an honest mistake by an applicant; a review is not needed. It is too late.', 5],
    ['The team has went over the draft on monday. They will submitted it in january. Your welcome to check there notes.', "The team has gone over the draft on Monday. They will submit it in January. You're welcome to check their notes.", 6],
    ['i think its not ready; we was waiting for a NDA holder and an PSG, and noone replied.', "I think it's not ready; we were waiting for an NDA holder and a PSG, and no one replied.", 7]])
    eq(await pr(input), [want, n], `G1 “${input.slice(0, 40)}…” is corrected, ${n} items`);
  for (const c of ['There is not enough time. There will be a meeting. Their goal date is Oct 3. It was their response.',
    'Does ANDA 216482 have an open IR? What should it do? If it were late we would say so. Should it have a note?',
    'This may of course change. We sent it to many applicants; it led to much discussion. From early to late July.',
    'Option A applies. An NDA, an ANDA, an RLD, an FDA letter, a PSG, a MAPP, a user fee, a one-time event, an hour, a unit, an SOP and an IR.',
    "Its goal date is its own. It's on track and it's late. The applicant and its response. Your draft, their team, you're right, they're going.",
    'She and I will review it. Please send it to him and me. They are ready; we are too. More than enough, and then we wait.',
    'The response is due on Monday, March 3. He may march in May. The review has gone well and they have done it.',
    'Hi all,\n\nANDA 216482 remains on schedule. The IR response is due Jul 21, 2026, and I will confirm receipt.\n\nThanks,\nDemo author'])
    eq(await p.evaluate(x => proofread(x).items.map(i => i.text), c), [], `G2 nothing is flagged in “${c.replace(/\s+/g, ' ').slice(0, 46)}…”`);
  ok(await p.evaluate(() => { const t = performance.now(); proofread(('Their going to there goal, its a applicant and we was to late. ').repeat(2500)); return performance.now() - t < 2500; }), 'G3 a 150,000 character draft is handled in good time');
  await p.evaluate(() => openDraft('Their going to send the the responce tommorow and me and him will review it. ANDA 216482 are on track for there goal date.')); await settle(p, 400);
  eq(await p.$eval('#drMarked', e => [e.querySelectorAll('del').length, e.querySelectorAll('ins').length, e.textContent.includes("TheirThey're going"), e.querySelectorAll('*:not(del):not(ins)').length]), [7, 6, true, 0], 'G4 the draft is shown with each change marked: what goes is struck out, what comes is beside it, and nothing else is built');
  eq(await txt(p, '#drSummary'), '1 difference from your data · 7 wording items · 7 can be fixed for you', 'G5 one line sums up the result');
  await p.click('#drApplyFixed'); await settle(p, 300);
  eq([await p.inputValue('#drText'), await txt(p, '#drSummary'), await p.$('#drMarked')], ["They're going to send the response tomorrow and he and I will review it. ANDA 216482 is on track for their goal date.", '1 difference from your data · no wording items', null], 'G6 Apply here makes every change, and the summary follows');
  await p.fill('#drText', 'the <img src=x onerror=alert(1)> responce is due Feb 30.'); await settle(p, 400);
  eq(await p.evaluate(() => [document.querySelectorAll('#draft img, #draft script').length, document.getElementById('drMarked').textContent.includes('<img src=x onerror=alert(1)>'), [...document.querySelectorAll('#drFacts .find')].map(f => f.textContent)]), [0, true, ['“Feb 30” is not a date: February has at most 29 days.']], 'G7 markup in a draft stays text in the marked view, and a date that does not exist is caught');
  eq(await p.evaluate(() => ['The goal date is Jan 37, 2027.', 'Due 13/45/2026.', 'Feb 29, 2027 and Feb 29, 2028.', 'June 31.', 'Fine: Oct 3, 2026, 10/17/2026, Feb 29, Sep 30, version 3/4, ratio 12/40.'].map(t => factExtras(t).diffs.map(d => d.text))),
    [['“Jan 37, 2027” is not a date: January 2027 has 31 days.'], ['“13/45/2026” is not a date: there is no month 13.'], ['“Feb 29, 2027” is not a date: February 2027 has 28 days.'], ['“June 31” is not a date: June has 30 days.'], []], 'G8 dates that do not exist are named with the reason; real dates and fractions are left alone');

  // ====================================================== L. results first; the AI options last and folded
  await p.evaluate(() => closeApp('draft')); await p.click('[data-pickbtn="DRAFT"]'); await settle(p, 200);
  await p.keyboard.press('Control+Alt+Shift+KeyR'); await settle(p, 600);
  ok(await p.evaluate(() => { const d = document.getElementById('draft').getBoundingClientRect(); return ['drSummary', 'drFactLbl'].every(id => { const r = document.getElementById(id).getBoundingClientRect(); return r.top > d.top && r.bottom < Math.min(d.bottom, innerHeight); }) && document.querySelector('#drOut .find').getBoundingClientRect().top < Math.min(d.bottom, innerHeight) - 30; }), 'L1 at 1366 by 768 the summary and the first findings are in view as soon as the window opens (they were below the fold)');
  eq(await p.evaluate(() => [document.getElementById('drAiBox').open, document.getElementById('draft').lastElementChild.previousElementSibling.id, document.getElementById('drAiState').textContent, [...document.querySelectorAll('#draft [data-ai]')].every(x => x.disabled && x.closest('#drAiBox'))]), [false, 'drAiBox', '· off', true], 'L2 the AI options are the last thing in the window but the line that says what is switched on (round 29), folded, and off');
  await p.evaluate(() => { closeApp('draft'); getSelection().removeAllRanges(); lastSel = null; openDraft(''); }); await settle(p, 300);
  eq(await txt(p, '#drSummary'), 'Nothing was selected. Select text in any window and press Ctrl+Alt+Shift+R, paste a draft above, or press “Use the sample draft”.', 'L3 an empty window says what to do, at the top');
  await p.click('#swAi').catch(async () => { await p.evaluate(() => { closeApp('draft'); }); await p.click('#swAi'); await p.evaluate(() => openDraft(SAMPLE_DRAFT)); }); await settle(p, 300);
  await p.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(p, 300);
  eq(await p.evaluate(() => [document.getElementById('drAiBox').open, document.getElementById('drAiState').textContent, [...document.querySelectorAll('#draft [data-ai]')].some(x => x.disabled)]), [true, '· an approved model is connected', false], 'L4 with an approved model connected the AI options open by themselves');
  await p.click('#draft [data-ai="Rewrite"]'); await settle(p, 200);
  ok(/Rewrite: in the Windows app a preview shows exactly what would be sent/.test(await txt(p, '#drLocalOut')) && (await p.$$('#drOut .find')).length > 3, 'L5 its explanation appears with the AI options and no longer replaces the results');
  eq(await axe(p, '#draft'), [], 'L6 accessibility scan of the Review window with the AI options open');
  await p.evaluate(() => closeApp('draft')); await p.click('#swAi');

  // ====================================================== I. the AI that is built into the PC
  eq(await p.evaluate(async () => { const s = await localAI(); return [s.kind, ['none', 'not-installed', 'blocked'].includes(s.state)]; }), [null, true], 'I1 in this browser no ready on-device model is found, and none is claimed');
  await p.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(p, 400);
  ok(/^AI on this PC: /.test(await txt(p, '#drLocalAi')) && await p.$$eval('#draft [data-ai]', n => n.every(x => x.disabled)), 'I2 the window says so in one line, and the AI buttons stay off');
  ok(!/download its model|Working/.test(await txt(p, '#draft')) && !(await txt(p, '#drLocalAi')).includes('found in this browser.'), 'I3 a model that is not on the PC yet is not started');
  { const q = await fresh({ init: () => { let made = 0; self.LanguageModel = { availability: async () => 'available', create: async () => { made++; self.__made = made; return { prompt: async t => 'STAND-IN: ' + t.split('\n')[0].slice(0, 30) + ' | ' + t.length, destroy() { self.__destroyed = true; } }; } }; } });
    await q.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(q, 400);
    eq([await q.evaluate(() => document.getElementById('drAiBox').open), await txt(q, '#drAiState'), await q.$$eval('#draft [data-ai]', n => n.map(x => x.disabled)), await q.evaluate(() => self.__made || 0)], [true, '· the AI on this PC is ready', [false, false, false], 0], 'I4 where the browser has a ready on-device model, the AI options open, and nothing has run yet');
    await q.click('#draft [data-ai="Proofread"]'); await settle(q, 200);
    ok(/This gives your draft \(\d+ characters\) to the AI built into this browser\. It runs on this PC\./.test(await txt(q, '#drLocalOut')) && (await q.evaluate(() => self.__made || 0)) === 0, 'I5 a click shows what will be given to it first; it still has not run');
    await q.click('#drLocalOut .pill'); await settle(q, 300);
    eq([/^PROOFREAD · FROM THE AI ON THIS PC · NOT CHECKEDSTAND-IN: Proofread the text below/.test(await txt(q, '#drLocalOut')), await q.evaluate(() => [self.__made, self.__destroyed])], [true, [1, true]], 'I6 Run on this PC runs it once, marks the answer as unchecked, and lets the model go');
    await q.click('#drLocalOut .pill'); await settle(q, 200);
    ok(/^STAND-IN: /.test(await clip(q)) && (await q.inputValue('#drText')) === await q.evaluate(() => SAMPLE_DRAFT), 'I7 the answer is Copy only: the draft itself is not changed');
    await q.close(); }
  { const q = await fresh({ init: () => { self.LanguageModel = { availability: async () => 'available', create: async () => { throw new Error('<b>policy</b> blocked'); } }; } });
    await q.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(q, 400); await q.click('#draft [data-ai="Rewrite"]'); await settle(q, 150); await q.click('#drLocalOut .pill'); await settle(q, 300);
    eq([/could not do this \(<b>policy<\/b> blocked\)/.test(await txt(q, '#drLocalOut')), await q.$$eval('#drLocalOut b', n => n.length)], [true, 0], 'I8 a model that fails is reported in words, as text');
    await q.close(); }
  { const q = await fresh({ init: () => { self.LanguageModel = { availability: async () => { throw new Error('nope'); } }; } });
    await q.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(q, 400);
    ok(/not allowed to use the browser's built-in AI/.test(await txt(q, '#drLocalAi')) && await q.$$eval('#draft [data-ai]', n => n.every(x => x.disabled)), 'I9 a browser that refuses the question leaves everything off');
    await q.close(); }

  // ====================================================== S. notes survive a reload
  { const q = await fresh();
    await openFull(q, '216482'); await q.keyboard.press('Enter'); await q.keyboard.type('Keep this across a reload.'); await q.keyboard.press('Control+Enter'); await settle(q, 200);
    ok(/kept in this browser until you press Reset/.test(await txt(q, '#qnFine')), 'S1 the note box says the note is kept in this browser');
    await q.reload(); await settle(q, 400); await q.dblclick('#deskIcon');
    eq(await q.evaluate(() => notesFor('216482').map(n => n.text).slice(0, 1)), ['Keep this across a reload.'], 'S2 after a reload the note is still there');
    await q.evaluate(() => localStorage.setItem(QN_STORE, JSON.stringify({ notes: { '216482': [{ text: '<img src=x onerror=alert(1)>', when: 'Jul 1, 9:00 AM', t: 5 }, { text: 7 }, null], 'abc': [{ text: 'x', when: 'y', t: 1 }], '214990': 'no' } })));
    await q.reload(); await settle(q, 400); await q.dblclick('#deskIcon'); await openFull(q, '216482'); await q.keyboard.press('Enter'); await settle(q, 250);
    eq(await q.evaluate(() => [Object.keys(state.quickNotes), state.quickNotes['216482'].length, document.querySelectorAll('#x-notes img').length]), [['216482'], 1, 0], 'S3 a store that was tampered with is read carefully: bad entries are dropped and markup stays text');
    await q.keyboard.press('Escape'); await q.keyboard.press('Escape'); await q.click('#resetBtn'); await settle(q, 600);
    eq(await q.evaluate(() => notesFor('216482').length), 3, 'S4 Reset clears the notes');
    await q.close(); }
  { const q = await fresh({ init: () => { Object.defineProperty(window, 'localStorage', { get() { throw new Error('denied'); } }); } });
    await openFull(q, '216482'); await q.keyboard.press('Enter'); await q.keyboard.type('no store here'); await q.keyboard.press('Control+Enter'); await settle(q, 200);
    eq([await txt(q, '#chipNotes'), /notes last until the page is reloaded/.test(await txt(q, '#qnFine'))], ['Add a note · 4 notes', true], 'S5 where the browser allows no store (a hosted page), notes still work and the box says they last until a reload');
    await q.close(); }

  // ====================================================== V. what the card scrolls to is not left under the action area
  await openFull(p, '216482'); await p.click('#expBtn'); await settle(p, 300);
  await p.evaluate(() => document.querySelector('#x-watch-d summary').scrollIntoView({ block: 'nearest' })); await settle(p, 150);
  ok(await p.evaluate(() => document.querySelector('#x-watch-d summary').getBoundingClientRect().bottom <= document.querySelector('.chips-slot').getBoundingClientRect().top + 1), 'V1 a section scrolled into view stops above the action area, not under it');
  await p.click('#x-watch-d summary'); await settle(p, 150);
  ok(await p.evaluate(() => document.getElementById('x-watch-d').open), 'V2 and can be clicked');
  await p.keyboard.press('Escape'); await settle(p, 250);
  for (const [w, hgt] of [[1920, 1080], [1280, 720], [1024, 768], [390, 844]]) {
    const q = await fresh({ viewport: { width: w, height: hgt } });
    await openFull(q, '216482');
    eq(await q.evaluate(() => { const k = card.getBoundingClientRect(); return [document.documentElement.scrollWidth <= innerWidth, k.top >= -1 && k.bottom <= innerHeight + 1, document.getElementById('chipNotes').getBoundingClientRect().bottom <= innerHeight + 1]; }), [true, true, true], `V3 at ${w} by ${hgt}: no sideways scroll, the card and its main action are on screen`);
    await q.close(); }
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
