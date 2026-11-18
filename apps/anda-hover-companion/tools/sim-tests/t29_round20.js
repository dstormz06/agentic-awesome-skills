// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 20 in the browser simulation: Glass Sheet as the default at 90%, quick notes on the card, Proofread and Fact-check that work with no AI,
// notes and open items for recall in Review, and the Windows 11 look of the desktop. Attack cases are mixed in.
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
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 800 }, permissions: opts.noClip ? [] : ['clipboard-read', 'clipboard-write'] });
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
  const clip = p => p.evaluate(() => navigator.clipboard.readText());
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + v.nodes.slice(0, 2).map(n => n.target.join(' ')).join(' | ')), sel || null); };

  // ====================================================== G. Glass Sheet is the default, at 90%
  let p = await fresh();
  eq(await p.evaluate(() => [document.documentElement.dataset.dir, state.look, state.op, getComputedStyle(document.documentElement).getPropertyValue('--op').trim()]), ['x', 'x', 90, '90%'], 'G1 a new page opens in a glass look at 90% (X · Graphite Calendar since round 24)');
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p);
  eq(await p.$eval('#setLook label', l => [l.textContent.trim(), l.querySelector('input').checked]), ['X · Graphite Calendar', true], 'G2 it is the first look in Settings, and chosen');
  eq(await p.$eval('#setOp', s => [s.min, s.max, s.value]), ['40', '100', '90'], 'G3 the transparency slider runs 40 to 100 and starts at 90');
  eq(await txt(p, '#setOpV'), '90%', 'G4 and says 90%');
  ok(/The card starts at 90%\./.test(await p.$eval('#secLook', e => e.textContent)), 'G5 the hint names the start');
  await p.evaluate(() => closeApp('settings')); await openFull(p, '216482');
  const bgAt = async () => p.$eval('#card', c => getComputedStyle(c).backgroundColor);
  ok(/0\.9\)$/.test(await bgAt()), `G6 the glass card is drawn at 90% (${await bgAt()})`);
  ok(/blur/.test(await p.$eval('#card', c => getComputedStyle(c).backdropFilter || getComputedStyle(c).webkitBackdropFilter)), 'G7 and blurs what is behind it');
  for (const v of [40, 65, 100]) {
    await p.evaluate(n => { const s = document.getElementById('setOp'); s.value = n; s.dispatchEvent(new Event('input', { bubbles: true })); }, v); await settle(p, 60);
    const want = v === 100 ? /^rgb\(\d+, \d+, \d+\)$|^color\(srgb [\d.]+ [\d.]+ [\d.]+\)$|1\)$/ : new RegExp(String(v / 100).replace('.', '\\.') + '\\)$');
    ok(want.test(await bgAt()), `G8 the slider governs the glass at ${v}% (${await bgAt()})`);
  }
  eq(await p.evaluate(() => { const s = snapSettings(); return [DEFAULT_SETTINGS.appearance, DEFAULT_SETTINGS.opacity, s.opacity]; }), ['x', 90, 100], 'G9 the defaults that Reset and a backup use are X and 90');
  await p.evaluate(() => { const s = document.getElementById('setOp'); s.value = 90; s.dispatchEvent(new Event('input', { bubbles: true })); });
  ok(await p.$eval('.chips-slot', e => /blur/.test(getComputedStyle(e).backdropFilter || '')), 'G10 what scrolls under the action area is blurred, so no text shows through the buttons');
  for (const look of ['t', 'k', 'p']) { await p.evaluate(l => applyLook(l), look); await settle(p, 60); ok(/0\.9\)$/.test(await bgAt()), `G11 the look ${look.toUpperCase()} is at 90% too`); }
  await p.evaluate(() => applyLook('v'));
  await p.keyboard.press('Escape'); await settle(p, 250);

  // ====================================================== N. quick notes on the card
  await openFull(p, '216482');
  eq(await txt(p, '#chipNotes'), 'Add a note · 3 notes', 'N1 the Notes button counts the notes of the record');
  await p.keyboard.press('n'); await settle(p, 250);
  eq(await p.evaluate(() => [document.activeElement.id, card.classList.contains('expanded'), document.getElementById('x-notes-d').open]), ['qnText', true, true], 'N2 N opens the card fully, opens the notes and puts the pointer in the box');
  eq(await txt(p, '#qnLabel'), 'Add a quick note for ANDA 216482', 'N3 the box names the ANDA');
  ok(await p.$eval('#qnSave', x => x.disabled), 'N4 Save note is off while the box is empty');
  await p.keyboard.type('   '); ok(await p.$eval('#qnSave', x => x.disabled), 'N5 and while it holds only spaces');
  await p.fill('#qnText', '');
  await p.keyboard.type('Called the US agent; <b>response</b> will come Jul 28.');
  eq(await txt(p, '#qnCount'), '54/500', 'N6 the count follows the typing');
  // the card's own shortcut letters are just letters while typing
  ok(await vis(p, '#card') && await p.evaluate(() => card.classList.contains('expanded') && !pinnedCard), 'N7 typing letters such as e, r, s and n in the box does not run the card shortcuts');
  await p.keyboard.press('Control+Enter'); await settle(p, 200);
  eq([await txt(p, '#chipNotes'), await txt(p, '#x-notes-n'), await txt(p, '#qnMsg'), await p.inputValue('#qnText')], ['Add a note · 4 notes', '· 4', 'Note saved.', ''], 'N8 Ctrl+Enter saves: the counts go up, the box empties, and it says so');
  eq(await p.$eval('#x-notes .notei', e => [e.classList.contains('mine'), e.querySelector('.who').textContent, e.querySelector('.txt').textContent, e.querySelector('.txt').children.length]),
     [true, 'You · Jul 16, 10:42 AM', 'Called the US agent; <b>response</b> will come Jul 28.', 0], 'N9 the note is first, dated by the clock, and markup in it is shown as text');
  eq(await p.$$eval('#x-notes .notei', n => n.length), 4, 'N10 the three project notes are still below it');
  eq(await p.$$eval('#x-notes .notei:not(.mine) .nact button', n => n.map(x => x.textContent)), ['Copy', 'Copy', 'Copy'], 'N11 a project note can be copied but not changed');
  await p.click('#x-notes .notei.mine .nact button:has-text("Copy")'); await settle(p, 150);
  eq(await clip(p), 'Called the US agent; <b>response</b> will come Jul 28.', 'N12 Copy puts exactly the note on the clipboard');
  // edit
  await p.click('#x-notes .notei.mine .nact button:has-text("Edit")'); await settle(p, 150);
  await p.fill('#x-notes .notei.mine textarea', 'Called the US agent: the response will come Jul 28, not Jul 21.');
  await p.keyboard.press('Control+Enter'); await settle(p, 150);
  eq([await p.$eval('#x-notes .notei.mine .txt', e => e.textContent), await txt(p, '#qnMsg')], ['Called the US agent: the response will come Jul 28, not Jul 21.', 'Note changed.'], 'N13 Edit changes the note');
  await p.click('#x-notes .notei.mine .nact button:has-text("Edit")'); await settle(p, 100);
  await p.fill('#x-notes .notei.mine textarea', '   '); await p.click('#x-notes .notei.mine .nact button:has-text("Save change")'); await settle(p, 100);
  ok(/cannot be empty/.test(await txt(p, '#qnMsg')) && await p.$eval('#qnMsg', m => m.classList.contains('bad')), 'N14 an edit that empties the note is refused, politely');
  await p.keyboard.press('Escape'); await settle(p, 100);
  ok(await vis(p, '#card') && !(await p.$('#x-notes .notei.mine textarea')), 'N15 Escape leaves the edit and keeps the card open');
  // delete and undo
  await p.click('#x-notes .notei.mine .nact button:has-text("Delete")'); await settle(p, 150);
  eq([await txt(p, '#chipNotes'), /^Note deleted\. Undo$/.test(await txt(p, '#qnMsg'))], ['Add a note · 3 notes', true], 'N16 Delete removes it and offers Undo');
  await p.click('#qnMsg button'); await settle(p, 150);
  eq([await txt(p, '#chipNotes'), await txt(p, '#qnMsg'), await p.$eval('#x-notes .notei.mine .txt', e => e.textContent)], ['Add a note · 4 notes', 'Note restored.', 'Called the US agent: the response will come Jul 28, not Jul 21.'], 'N17 Undo brings it back');
  // a draft survives closing the card; Escape in the box leaves the box first
  await p.click('#qnText'); await p.keyboard.type('half a thought');
  await p.keyboard.press('Escape'); await settle(p, 100);
  ok(await vis(p, '#card') && await p.evaluate(() => document.activeElement === card), 'N18 the first Escape leaves the box and keeps the card');
  await p.keyboard.press('Escape'); await settle(p, 250);
  ok(!(await vis(p, '#card')), 'N19 the next Escape closes the card');
  await openFull(p, '214990');
  eq([await txt(p, '#chipNotes'), await p.evaluate(() => document.getElementById('qnText').value)], ['Add a note · 2 notes', ''], 'N20 another ANDA has its own notes and an empty box');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await openFull(p, '216482'); await p.keyboard.press('n'); await settle(p, 250);
  eq(await p.inputValue('#qnText'), 'half a thought', 'N21 the unsaved text is still there when the card is opened again');
  await p.fill('#qnText', ''); await p.keyboard.press('Escape'); await p.keyboard.press('Escape'); await settle(p, 250);
  // limits and odd input, by the functions the buttons call
  eq(await p.evaluate(() => { const out = []; out.push(addQuickNote('999999', 'x')[0]); out.push(addQuickNote('214990', '  \n ')[0]); const long = 'x'.repeat(2000); out.push(addQuickNote('214990', long)[0]); out.push(state.quickNotes['214990'][0].text.length);
    for (let i = 0; i < 60; i++) addQuickNote('214990', 'n' + i); out.push(state.quickNotes['214990'].length); out.push(addQuickNote('214990', 'one more')[1]); return out; }),
    [false, false, true, 500, 50, 'This ANDA already has 50 quick notes. Delete one to add another.'], 'N22 an unknown ANDA and an empty note are refused; a long note is cut at 500; fifty per ANDA at most');
  eq(await p.evaluate(() => { addQuickNote('216482', 'a\u0000b\r\n\r\n\r\n\r\nc =HYPERLINK("x")'); const n = state.quickNotes['216482'].slice(-1)[0].text; return n; }), 'ab\n\nc =HYPERLINK("x")', 'N23 control characters are dropped and a formula stays plain text');
  ok(await p.evaluate(() => !JSON.stringify(watchItems()).includes('US agent') && !backupText().includes('US agent')), 'N24 a note is never in a notice near the clock and never in the settings backup');
  await p.evaluate(() => { openCard('216482', { anchor: { x: 520, y: 60 }, forceFull: false }); }); await settle(p, 450);
  await p.evaluate(() => { card.classList.add('mini'); card.classList.remove('expanded'); }); await settle(p, 100);
  await p.keyboard.press('r'); await settle(p, 200);
  ok(await p.$eval('#replyMenu', m => { const r = m.getBoundingClientRect(), c = card.getBoundingClientRect(); return m.style.display === 'block' && r.left >= c.left && r.left < c.right && r.bottom <= c.bottom + 4 && r.top > 40; }), 'N25a on a peek, the quick replies menu opens at the card, not in a corner of the screen');
  await p.keyboard.press('Escape'); await settle(p, 150);
  await p.keyboard.press('n'); await settle(p, 300);
  eq(await p.evaluate(() => [card.classList.contains('mini'), card.classList.contains('expanded'), document.activeElement.id]), [false, true, 'qnText'], 'N25 on a peek, N opens the full card with the notes and the pointer in the box');
  await p.keyboard.press('Escape'); await p.keyboard.press('Escape'); await settle(p, 250);
  await openFull(p, '216482'); await p.click('#chipNotes'); await settle(p, 250);
  eq(await p.evaluate(() => document.activeElement.id), 'qnText', 'N26 the Notes button does what N does');
  ok(await p.$eval('#card', c => { const r = c.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight + 1 && r.right <= innerWidth + 1; }), 'N27 the open card with notes stays on screen');
  eq(await axe(p, '#card'), [], 'N28 accessibility scan of the card with the notes open');
  eq(await txt(p, '.kbdbar'), 'Esc closeEnter add a noteS copy statusE expandSpace pinR quick replies', 'N29 the key hints say Enter adds a note and S copies the status');
  await p.keyboard.press('Escape'); await p.keyboard.press('Escape'); await settle(p, 250);
  // a refused clipboard is reported
  { const q = await fresh({ noClip: true, init: () => { Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('no')) }, configurable: true }); document.execCommand = () => false; } });
    await openFull(q, '216482'); await q.keyboard.press('n'); await settle(q, 200);
    await q.click('#x-notes .notei .nact button'); await settle(q, 300);
    ok(/did not allow copying/.test(await q.evaluate(() => document.body.innerText)), 'N30 when the browser refuses the copy, the note is shown and nothing says Copied');
    await q.close(); }

  // ====================================================== P. Proofread works with no AI
  await p.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(p, 300);
  eq(await p.$$eval('#draft .acts button', n => n.map(x => [x.textContent, x.disabled])),
     [['Check everything', false], ['Proofread', false], ['Fact-check', false], ['Use the sample draft', false], ['Rewrite', true], ['Proofread with AI', true], ['Fact-check with AI', true]], 'P1 Proofread and Fact-check are on with no model; the AI versions stay off');
  eq(await p.$$eval('#drProof .find', n => n.map(x => x.textContent)), ['“responce” is usually spelled “response”.'], 'P2 the sample draft has one spelling slip, and it is found');
  ok(await vis(p, '#drOut') && await vis(p, '#drProof') && await vis(p, '#drSend'), 'P3 Check everything shows facts, proofreading and the one question');
  await p.click('#drProofBtn'); await settle(p);
  eq([await vis(p, '#drOut'), await vis(p, '#drProof'), await vis(p, '#drSend'), await p.$eval('#drProofBtn', x => x.getAttribute('aria-pressed'))], [false, true, false, 'true'], 'P4 Proofread shows only proofreading');
  const before = await p.inputValue('#drText');
  await p.click('#drCopyFixed'); await settle(p, 200);
  eq([await clip(p), await p.inputValue('#drText') === before, await p.$eval('#drProof .prfoot span', x => x.textContent)], [before.replace('responce', 'response'), true, 'Corrected draft copied. Your own text was not changed.'], 'P5 Copy corrected draft copies the fixed text and leaves the box alone');
  await p.click('#drApplyFixed'); await settle(p, 250);
  eq([await p.inputValue('#drText'), await p.$$eval('#drProof .find', n => n.map(x => x.textContent))], [before.replace('responce', 'response'), ['Proofreading found nothing by its rules: spelling of common words and of names in your data, spacing, punctuation, capitals, brackets and date style.']], 'P6 Apply here fixes the box, and the item is gone');
  // the rules, one by one
  const cases = [
    ['hi team, i recieved the the ammendment for anda 216482 .It is a ANDA for Metfromin tablets,500 mg (cycle 2.', 'Hi team, I received the amendment for ANDA 216482. It is an ANDA for Metformin tablets, 500 mg (cycle 2.', 11],
    ['The applicant will submit the responce tommorow.The team agreed.', 'The applicant will submit the response tomorrow. The team agreed.', 3],
    ['Please see the status ,as discussed ; thanks !', 'Please see the status, as discussed; thanks!', 1],
    ['ANDA 216482: Meridan has not replied;;', 'ANDA 216482: Meridian has not replied;', 2],
    ['The FDA labelling guidance was recieved.  Thanks,,', 'The FDA labeling guidance was received. Thanks,', 4],
  ];
  for (const [input, want, n] of cases) eq(await p.evaluate(x => { const r = proofread(x); return [r.corrected, r.total]; }, input), [want, n], `P7 “${input.slice(0, 38)}…” is corrected, ${n} item${n === 1 ? '' : 's'}`);
  const clean = ['Hi all,\n\nANDA 216482 remains on schedule. The IR response is due Jul 21, 2026, and I will confirm receipt.\n\nThanks,\nDemo author',
    'See eCTD section 3.2.P.1, e.g. the table. No. 5 applies. Dr. Smith agreed, i.e. no change.', 'Tablets and tablet strengths (500 mg and 750 mg) are listed at www.fda.gov; write to a.b@fda.gov.',
    'ANDA\tApplicant\tGoal date\n216482\tFictional Pharma A\tOct 10, 2026', 'The Meridian team and the median value were discussed. It is 3.5 mg.', 'That that is fine. He had had enough.', ''];
  for (const c of clean) eq(await p.evaluate(x => proofread(x).items.map(i => i.text), c), [], `P8 nothing is flagged in “${c.replace(/\s+/g, ' ').slice(0, 44)}…”`);
  eq(await p.evaluate(() => proofread('The plan (see below is ready. He said “yes.').items.map(i => i.text)), ['The draft has 1 opening and 0 closing parentheses.', 'The draft has 1 opening and 0 closing quotation marks.'], 'P9 brackets and quotation marks that do not close are counted');
  eq(await p.evaluate(() => proofread('Goal Oct 3, 2026 and 10/17/2026.').items.map(i => i.text)), ['Dates are written in two ways (“Oct 3, 2026” and “10/17/2026”). One style reads more easily.'], 'P10 two date styles are noted, not changed');
  ok(await p.evaluate(() => { const t = performance.now(); const r = proofread(('teh quick  brown fox ,jumped. ').repeat(4000)); return performance.now() - t < 1500 && r.items.length <= 14 && r.more >= 0; }), 'P11 a 120,000 character draft is handled quickly and the list stays short');
  await p.fill('#drText', '<img src=x onerror=alert(1)> recieve <script>alert(2)</script>'); await settle(p, 400);
  eq(await p.evaluate(() => [document.querySelectorAll('#draft img, #draft script').length, document.querySelector('#drProof .find').textContent]), [0, '“recieve” is usually spelled “receive”.'], 'P12 markup in a draft is text: nothing is built from it');
  await p.fill('#drText', ''); await settle(p, 400);
  eq(await p.$$eval('#drProof .find', n => n.map(x => x.textContent)), ['Nothing to proofread yet. Select your draft and press the shortcut, or paste it above.'], 'P13 an empty box says what to do');
  ok(/short list of fixed rules, not a full spelling or grammar check/.test(await p.evaluate(() => { openDraft(SAMPLE_DRAFT); return document.getElementById('drProof').textContent; })), 'P14 the section says plainly what it is not');
  ok(/Not checked: grammar and wording beyond the short list of rules under Proofreading/.test(await txt(p, '#drOut')), 'P15 the closing line of the fact check no longer says spelling is unchecked');
  ok(await p.evaluate(() => { const r = document.querySelector('#drOut .receipt').getBoundingClientRect(); return [...document.querySelectorAll('#drFacts .find')].every(f => f.getBoundingClientRect().bottom <= r.top + 1); }), 'P15a and it is shown after every fact, including the ones from the sheet and the notes');
  ok(await p.evaluate(() => draftReview(SAMPLE_DRAFT, demoToday()).findings.slice(-1)[0].text === RV_RECEIPT), 'P16 the shared review result itself is unchanged (the app and the extension still match it)');

  // ====================================================== F. Fact-check against the sheet and the notes
  await p.click('#drFactBtn'); await settle(p);
  eq([await vis(p, '#drOut'), await vis(p, '#drFacts'), await vis(p, '#drProof'), await vis(p, '#drSend')], [true, true, false, true], 'F1 Fact-check shows the facts and the one question, not the proofreading');
  eq(await p.$$eval('#drFacts .find', n => n.map(x => [x.className.replace('find ', ''), x.textContent])), [
    ['diff', 'ANDA 216482: the draft says 750 mg, but your data says Metformin HCl ER Tablets, 500 mg.'],
    ['diff', 'ANDA 216482: the draft says cycle 3, but your data says cycle 2.'],
    ['diff', 'ANDA 216482: the draft gives a due date of Jul 28, but your data says the IR response is due Jul 21, 2026.'],
    ['note', '“Jul 28” is also in your note of Jul 16, 10:42 AM on ANDA 216482: “Called the US agent: the response will come Jul 28, not Jul 21.”']], 'F2 strength, cycle and due date are checked against the sheet, and a date is traced to the note it came from');
  const fx = t => p.evaluate(x => { const r = factExtras(x); return [r.diffs.map(d => d.text), factExtrasSummary(r)]; }, t);
  eq(await fx('ANDA 216482 (500 mg, cycle 2): the IR response is due Jul 21.'), [[], 'Also checked 1 strength, 1 review cycle and 1 due date against your sheet: no difference.'], 'F3 a draft that agrees with the sheet says so');
  eq(await fx('2. ANDA 214990 - the 750 mg strength from the same applicant. It shares a manufacturing site with the 500 mg application, so any facility finding on one matters to both.'), [[], 'Also checked 1 strength against your sheet: no difference.'], 'F4 the demo email, which names both strengths in one thought, raises nothing');
  eq(await fx('ANDA 214990 is in its second cycle.'), [['ANDA 214990: the draft says cycle 2, but your data says cycle 3.'], null], 'F5 a cycle written as a word is read');
  eq(await fx('ANDA 999999 is 750 mg in cycle 9, due Jul 1.'), [[], null], 'F6 an ANDA that is not in the data is left to the first check');
  eq(await fx('ANDA\tDrug\tStrength\n216482\tMetformin\t750 mg'), [[], null], 'F7 rows copied from a spreadsheet are not read as sentences');
  eq(await fx('ANDA 216482 and ANDA 214990 are both 750 mg.'), [[], null], 'F8 a sentence about two ANDAs is not guessed at');
  eq(await p.evaluate(() => { const t = 'ANDA 216482 (750 mg) is with the team.'; return beforeSend(t, draftReview(t, demoToday()), factExtras(t)).text; }), 'Is this difference intended? ANDA 216482: the draft says 750 mg, but your data says Metformin HCl ER Tablets, 500 mg.', 'F9 when only the sheet disagrees, that becomes the one question');
  eq(await p.evaluate(() => { const t = 'ANDA 216482 (500 mg) is with the team.'; return beforeSend(t, draftReview(t, demoToday()), factExtras(t)).text; }), 'No open gaps.', 'F10 and a draft that agrees still ends with “No open gaps.”');
  ok(await p.evaluate(() => { const t = performance.now(); factExtras(('ANDA 216482 is 750 mg, due Jul 28. ').repeat(3000)); return performance.now() - t < 2500; }), 'F11 a very long draft is handled in good time');

  // ====================================================== R. notes and open items for recall, closed, at the bottom
  await p.click('#drCheck'); await settle(p);
  eq(await p.$$eval('#drRecall details', n => n.map(d => [d.dataset.anda, d.open, d.querySelector('summary').textContent])),
     [['216482', false, 'ANDA 216482 · 5 notes (latest Jul 16, 10:42 AM) · 2 open items'], ['214990', false, 'ANDA 214990 · 52 notes (latest Jul 16, 10:42 AM) · no open items']], 'R1 one closed row for each ANDA the draft names, with counts');
  ok(await p.evaluate(() => { const r = document.getElementById('drRecall'), d = document.getElementById('draft'); return r.parentElement === d && r.nextElementSibling.id === 'drAiBox' && r.nextElementSibling === d.lastElementChild && !r.nextElementSibling.open; }), 'R2 it is the last thing in the window but the folded AI options');
  await p.click('#drRecall details[data-anda="216482"] summary'); await settle(p);
  const rn = await p.$$eval('#drRecall details[data-anda="216482"] .rn', n => n.map(x => x.textContent));
  ok(rn.length === 5 && /also in your draft: Jul 28/.test(rn[0]) && /4 more|1 more on the card/.test(rn[4]), `R3 opened: the notes the draft touches come first, four at most, then a count (${rn.length})`);
  eq(await p.$$eval('#drRecall details[data-anda="216482"] .ri', n => n.map(x => x.textContent)), ['IR response — Biopharmaceuticsdue Jul 21', 'Labeling carve-out reviewin queue'], 'R4 the open items of the record are listed');
  await p.evaluate(() => { todoStore.items.push({ id: 't900', title: 'Send the dissolution question', number: '216482', note: null, due: '2026-07-20', done: false, doneOn: null, snoozedUntil: null }); clStore.items.push({ id: 'c900', title: 'Call about 216482 <b>today</b>', category: 'Work', done: false, repeat: 0 }); runDraftCheck(); }); await settle(p);
  await p.evaluate(() => { document.querySelector('#drRecall details[data-anda="216482"]').open = true; });
  eq(await p.$$eval('#drRecall details[data-anda="216482"] .ri', n => n.slice(2).map(x => [x.textContent, x.querySelectorAll('b').length])), [['Send the dissolution questiondue Jul 20, 2026 · ANDA to-do list', 0], ['Call about 216482 <b>today</b>My to-do list', 0]], 'R5 open items from both to-do lists join them, as text');
  await p.click('#drRecall details[data-anda="216482"] .rfoot button'); await settle(p, 500);
  ok(await vis(p, '#card') && /216482/.test(await txt(p, '#card')), 'R6 Open the card opens that ANDA');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await p.evaluate(() => openDraft('Hello, no number here. recieve.')); await settle(p, 300);
  eq(await p.$eval('#drRecall', e => e.children.length), 0, 'R7 a draft that names no ANDA of yours has no recall row');
  eq(await p.evaluate(() => recallFor('216482 214990 216482 999999 NDA 216482 N214990').map(g => g.id)), ['216482', '214990'], 'R8 each ANDA once, and never an NDA or an unknown number');
  await p.evaluate(() => openDraft(SAMPLE_DRAFT)); await settle(p, 300);
  eq(await axe(p, '#draft'), [], 'R9 accessibility scan of the Review window');
  await p.evaluate(() => { document.querySelectorAll('#drRecall details').forEach(d => { d.open = true; }); }); await settle(p);
  eq(await axe(p, '#draft'), [], 'R10 and with the recall rows open');
  ok(await p.$eval('#draft', d => { const r = d.getBoundingClientRect(); return r.bottom <= innerHeight && d.scrollHeight > d.clientHeight; }), 'R11 the window stays on screen and scrolls inside itself');
  await p.evaluate(() => closeApp('draft'));
  // the real path: select the draft in Mail, press the shortcut
  await p.click('[data-pickbtn="DRAFT"]'); await settle(p, 200);
  await p.keyboard.press('Control+Alt+Shift+KeyR'); await settle(p, 600);
  ok(await vis(p, '#draft') && /responce/.test(await p.inputValue('#drText')) && (await p.$$('#drRecall details')).length === 2, 'R12 selecting the draft in Mail and pressing the Review shortcut shows facts, proofreading and the recall rows');
  await p.evaluate(() => closeApp('draft'));

  // ====================================================== W. the Windows 11 look of the desktop
  eq(await p.evaluate(() => [document.querySelector('.wall').getAttribute('aria-hidden'), document.getElementById('desk').dataset.wall, document.querySelectorAll('.wall .bloom path').length > 20]), ['true', 'desert', true], 'W1 the wallpaper is a drawing in the page, hidden from screen readers');
  for (const w of ['bloom', 'dusk', 'slate', 'desert']) {
    await p.click(`#wallPick [data-wall="${w}"]`); await settle(p, 120);
    eq(await p.evaluate(() => [document.getElementById('desk').dataset.wall, [...document.querySelectorAll('#wallPick button')].filter(x => x.getAttribute('aria-pressed') === 'true').length]), [w, 1], `W2 the ${w} background can be chosen`);
    eq(await axe(p), [], `W3 accessibility scan of the whole desktop on the ${w} background`);
  }
  eq(await p.evaluate(() => [...document.querySelectorAll('.win .rail, .cmp, .chd, .srch, .icon.deco, #taskbar .mid, #taskbar .wx, .win .tabs, #web .addr')].filter(e => e.getAttribute('aria-hidden') !== 'true').map(e => e.className)), [], 'W4 every added piece of window dressing is hidden from screen readers');
  ok(await p.evaluate(() => { const r = [...document.querySelectorAll('.icon')].map(e => e.getBoundingClientRect()); return r.every((a, i) => r.every((b, j) => i === j || a.bottom <= b.top + 1 || b.bottom <= a.top + 1)); }), 'W5 the desktop icons do not overlap');
  ok(await p.evaluate(() => [...document.querySelectorAll('.apps .win:not([hidden]) .bar .ttl')].every(t => t.scrollWidth <= t.clientWidth + 1)), 'W6 no window title is cut off at 1366 px');
  ok(await p.evaluate(() => !/logo/i.test([...document.querySelectorAll('svg, img')].map(e => e.outerHTML).join('')) && document.querySelectorAll('img').length === 0), 'W7 no image file and no logo is used: everything is drawn in the page');
  // the things people do in the windows still work
  await p.dblclick('#mailBody .pick[data-pick="216482"]'); await settle(p, 200);
  eq(await p.evaluate(() => String(getSelection())), '216482', 'W8 a number in the Mail window can still be selected');
  await p.keyboard.press('Control+Alt+KeyA'); await settle(p, 700);
  ok(await vis(p, '#chip') || await vis(p, '#card'), 'W9 and the shortcut answers on it');
  await p.keyboard.press('Escape'); await settle(p, 250);
  for (const [sel, pick] of [['#chatBody', '214990'], ['#webBody', '216482']]) { await p.dblclick(`${sel} .pick[data-pick="${pick}"]`); await settle(p, 150); eq(await p.evaluate(() => String(getSelection())), pick, `W10 a number in ${sel} can be selected`); }
  await p.evaluate(() => { document.querySelector('#appPick button:last-child').click(); }); await settle(p, 250);
  ok(await vis(p, '#term') && await p.$eval('#termBody', e => getComputedStyle(e).backgroundColor === 'rgb(12, 12, 12)'), 'W11 the terminal is dark, as on Windows');
  eq(await axe(p, '#apps'), [], 'W12 accessibility scan of the four app windows together');
  for (const [w, hgt] of [[1920, 1080], [1366, 768], [1280, 720], [1024, 768], [390, 844]]) {
    const q = await fresh({ viewport: { width: w, height: hgt } });
    eq(await q.evaluate(() => [document.documentElement.scrollWidth <= innerWidth, [...document.querySelectorAll('.apps .win:not([hidden]) .body')].every(b => b.clientHeight > 60)]), [true, true], `W13 at ${w} by ${hgt}: no sideways scroll, and every window has room for its text`);
    if (w >= 1280) ok(await q.evaluate(() => [...document.querySelectorAll('.apps .win:not([hidden]) .bar .ttl')].every(t => t.scrollWidth <= t.clientWidth + 1)), `W14 at ${w}: titles are whole`);
    await q.close();
  }
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
