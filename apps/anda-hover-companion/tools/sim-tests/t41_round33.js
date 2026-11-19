// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
// Round 33 in the browser simulation. The rules of five features the Windows app takes over (card details with Copy, text size and
// spacing, recent and kept ANDAs, One Bar, Today) were separated from their windows so that one answer file can hold both programs to
// them. This suite checks that the windows still show exactly what the rules say, and the one thing that changed: in Today, what needs a
// look is listed with the most pressing first.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce', permissions: ['clipboard-read', 'clipboard-write'] });
  const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
  await p.goto(SIM); await p.waitForTimeout(300);
  const settle = (ms = 200) => p.waitForTimeout(ms);
  const openCardOf = async id => { await p.evaluate(i => { APP_WINS.forEach(closeApp); openCard(i, { anchor: { x: 700, y: 120 }, forceFull: true, stay: true }); }, id); await settle(400); };

  // ---- A. card details: the window shows what the rule says ----
  await openCardOf('214990');
  await p.evaluate(() => { if (!document.getElementById('card').classList.contains('expanded')) toggleExpand(); }); await settle(300);
  const shown = await p.evaluate(() => [...document.querySelectorAll('#x-details .drow')].map(r => [r.querySelector('.k').textContent, r.querySelector('.v').textContent, !!r.querySelector('[data-copy]'), r.querySelector('[data-copy]') ? r.querySelector('[data-copy]').getAttribute('aria-label') : null]));
  const rule = await p.evaluate(() => detailRows('214990', state.cardFields));
  eq(shown, rule.rows.map(r => [r.label, r.text, r.copy, r.copy ? r.copyName : null]), 'A1 the rows of the details are the rule\'s rows: label, value or "not in your sheet", and Copy only where there is a value');
  eq(await p.evaluate(() => [document.getElementById('x-details-n').textContent, document.getElementById('x-details-all').hidden]), [rule.count, !rule.copyAll], 'A2 the count beside the heading and whether "Copy all" is offered are the rule\'s');
  eq([rule.have, rule.rows.length, rule.rows.filter(r => !r.copy).map(r => r.key)], [7, 9, ['applicantEmail', 'myNotes']], 'A3 this sample ANDA has seven of the nine usual columns, and the two it lacks are said to be missing');
  await p.click('#x-details [data-copy="agentEmail"]'); await settle();
  eq([await p.evaluate(() => navigator.clipboard.readText()), await p.textContent('#x-details-status')], ['us.agent@northgate-agents.example', 'Copied: us agent email.'], 'A4 Copy puts the value on the clipboard and says what was copied');
  await p.click('#x-details-all'); await settle();
  eq([await p.evaluate(() => navigator.clipboard.readText()), await p.textContent('#x-details-status')], [rule.block, 'Copied: all the details shown.'], 'A5 "Copy all" copies the rule\'s block: the ANDA and only the columns that have a value');
  await p.evaluate(() => { state.cardFields = []; paintDetails('214990'); });
  eq(await p.evaluate(() => [document.querySelector('#x-details').textContent, document.getElementById('x-details-n').textContent, document.getElementById('x-details-all').hidden]), ['No columns are chosen. Choose them in Settings, My files and writing.', '', true], 'A6 with no column chosen the card says so and offers no "Copy all"');
  eq(await p.evaluate(() => [detailRows('214990', null).rows.length, detailRows('214990', 'applicant').rows.length, detailRows('214990', ['fei', 'applicant', 'nothing']).rows.map(r => r.key)]), [0, 0, ['applicant', 'fei']], 'A7 what is not a list of columns chooses none, and the order is the fixed one whatever order was chosen');

  // ---- B. recent and kept ANDAs ----
  await p.evaluate(() => { state.cardFields = ['applicant']; state.recents = []; state.pins = []; });
  await openCardOf('216482'); await openCardOf('214990');
  eq(await p.evaluate(() => state.recents), ['214990', '216482'], 'B1 opening a card puts its number at the front of the recent ones');
  eq(await p.evaluate(() => [document.getElementById('chipPin').textContent, document.getElementById('chipPin').getAttribute('aria-pressed')]), ['Keep in my list', 'false'], 'B2 the card offers "Keep in my list"');
  await p.click('#chipPin'); await settle();
  eq(await p.evaluate(() => [document.getElementById('chipPin').textContent, document.getElementById('chipPin').getAttribute('aria-pressed'), state.pins]), ['Remove from my list', 'true', ['214990']], 'B3 one press keeps it, and the button says how to remove it');
  await p.evaluate(() => { state.pins = ['100001', '100002', '100003', '100004', '100005']; paintPinChip('214990'); });
  await p.click('#chipPin'); await settle(300);
  eq(await p.evaluate(() => [state.pins.length, state.pins.includes('214990'), document.getElementById('chipTxt').textContent === 'Your list holds 5 ANDAs. Remove one to keep another.' && document.getElementById('chipSay').textContent === document.getElementById('chipTxt').textContent]), [5, false, true], 'B4 a sixth is refused in words, and the five are left as they are');
  eq(await p.evaluate(() => [pinAfter(['216482'], '12a456').result, pinAfter(['216482'], '216482').pins, recentAfter(['216482'], '214990', false), recentAfter(['216482'], 'abc', true)]), ['refused', [], ['216482'], ['216482']], 'B5 only six-digit numbers are kept, and nothing is remembered while remembering is off');
  await p.evaluate(() => { state.pins = ['214990']; APP_WINS.forEach(closeApp); closeCard && closeCard(); openLookup({ x: 500, y: 300 }); }); await settle(300);
  eq(await p.evaluate(() => [...document.querySelectorAll('#lookupRecent button')].map(x => [x.textContent, x.getAttribute('aria-label')])), [['214990', 'ANDA 214990, kept in my list'], ['216482', 'ANDA 216482']], 'B6 the lookup window offers the kept one first, then the recent one, each named for a screen reader');

  // ---- C. One Bar and the lookup messages ----
  await p.evaluate(() => { state.lookupStyle = 'bar'; document.getElementById('lookupIn').value = '216482'; paintLookup(); }); await settle();
  const rows = await p.evaluate(() => [...document.querySelectorAll('#lkSug .lk-row')].map(r => [r.querySelector('b').textContent, r.querySelector('small').textContent]));
  eq(rows, (await p.evaluate(() => barRows('216482', selectionText()))).map(r => [r.t, r.s]), 'C1 One Bar shows the rule\'s rows for what is typed');
  eq(rows[0], ['ANDA 216482 · Metformin HCl ER Tablets, 500 mg', '14 days later than the goal · needs you: IR response due in 5 days'], 'C2 the first row names the ANDA and says its answer in one sentence');
  eq(await p.evaluate(() => document.getElementById('card').style.display === 'block' || document.getElementById('draft').style.display === 'block'), false, 'C3 typing opens nothing by itself');
  eq(await p.evaluate(() => ['N210793', 'NDA 021234', '216 482', 'hello'].map(lookupError)), ['N210793 is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.', 'NDA, BLA, IND and similar numbers are not ANDA numbers. Please enter a six-digit ANDA number.', 'Please enter the six digits without spaces or dashes.', 'Please enter a six-digit ANDA number.'], 'C4 the lookup window says why what was typed is not an ANDA number');
  await p.evaluate(() => { state.lookupStyle = 'box'; document.getElementById('lookupIn').value = 'N210793'; paintLookup(); submitLookup(); }); await settle();
  eq(await p.textContent('#lookupErr'), 'N210793 is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.', 'C5 and the window shows that sentence');

  // ---- D. Today: the most pressing first ----
  await p.evaluate(() => { APP_WINS.forEach(closeApp); state.pins = ['214990']; state.recents = ['216482', '214990']; tdSel = null; openToday(); }); await settle(300);
  const groups = await p.evaluate(() => todayGroups(state.pins, state.recents));
  eq(groups[0].rows.map(r => [r.id, r.mark]), [['216482', 'r'], ['214990', 'w']], 'D1 what needs a look is listed with the most pressing first: the urgent ANDA before the one that is only worth a look');
  eq(await p.evaluate(() => [...document.querySelectorAll('#tdList .td-grp, #tdList .td-it')].map(x => x.classList.contains('td-grp') ? x.textContent : x.querySelector('b').textContent + ' ' + x.querySelector('small').textContent)),
    groups.flatMap(g => g.ids.length ? [g.title, ...g.rows.map(r => r.id + ' ' + r.line)] : []), 'D2 the window lists the rule\'s three groups and rows');
  eq(await p.evaluate(() => tdSel), '216482', 'D3 the most pressing ANDA is the one shown on the right when the window opens');
  const main = await p.evaluate(() => todayMain('216482'));
  eq(await p.evaluate(() => [document.querySelector('#tdMain .meta').textContent, document.querySelector('#tdMain .hl b').textContent, document.querySelector('#tdMain .hl span').textContent, document.querySelector('#tdMain .hint').textContent]), [main.meta, main.figure, main.words, main.dates], 'D4 the right-hand side says what the rule says about that ANDA');
  await p.click('#tdMain .acts .pill'); await settle();
  eq(await p.evaluate(() => navigator.clipboard.readText()), main.status, 'D5 Copy status copies the status line');

  // ---- E. text size and spacing ----
  eq(await p.evaluate(() => { const out = []; for (const [ts, d] of [['large', 'comfortable'], ['xl', 'compact'], ['standard', 'comfortable']]) { state.ts = ts; state.density = d; applyDisplay(); const c = getComputedStyle(document.documentElement); out.push([+c.getPropertyValue('--ts') || 1, +c.getPropertyValue('--sp') || 1]); } return out; }), [[1.15, 1], [1.3, 0.72], [1, 1]], 'E1 larger text is 1.15 and 1.3 times as large, and compact spacing keeps 0.72 of the space');

  // ---- F. the version ----
  eq(await p.evaluate(() => [APP_VERSION, CHANGES[0].round, CHANGES[0].items.length]), ['Prototype, round 33', 33, 3], 'F1 the version is round 33, with what changed');
  eq(errs, [], 'F2 no error in the page');

  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
