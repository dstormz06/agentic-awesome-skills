// Writes (WRITE=1) or checks mockup/compare-golden.json: the answers of the browser simulation for "Compare two files", the calendar
// invitation, the usage summary and the reading prompts (round 26). The Windows app's tests (Round26Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node compare-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'compare-golden.json';
const T = (name, headers, rows) => ({ name, headers, rows });
const big = (n, shift) => T(shift ? 'Later' : 'Earlier', ['ID', 'Owner', 'Due', 'Count'], Array.from({ length: n }, (_, i) => ['K' + (1000 + i), 'Owner ' + (i % 7), `2027-01-${String(1 + (i % 28)).padStart(2, '0')}`, String(shift && i % 2 === 0 ? i + 3 : i)]));
const COMPARE = [
  { a: 'SAMPLE_A', b: 'SAMPLE_B' },
  { a: 'SAMPLE_A', b: 'SAMPLE_A' },
  { a: 'SAMPLE_A', b: 'SAMPLE_B', key: 'Product' },
  { a: 'SAMPLE_A', b: 'SAMPLE_B', key: 'applicant' },
  { a: 'SAMPLE_A', b: 'SAMPLE_B', key: 'No such column' },
  { a: T('Week 1', ['Task', 'Owner'], [['Draft', 'Ana'], ['Draft', 'Ben'], ['Send', 'Cy']]), b: T('Week 2', ['Task', 'Owner'], [['Draft', 'Ana'], ['Draft', 'Dee'], ['Send', 'Cy'], ['File', 'Eli']]) },
  { a: T('A', ['Id', 'Value'], [['1', 'x'], ['2', 'y'], ['', 'z'], ['2', 'again']]), b: T('B', ['ID ', ' value'], [['1', 'x'], ['2', 'Y'], ['', 'q'], ['3', 'w']]), key: 'id' },
  { a: T('Old', ['Name', 'Amount', 'When', 'Share'], [['a', '$1,000', '2026-10-03', '50%'], ['b', '12', 'Oct 3, 2026', '5%'], ['c', '7.50', '2/30/2026', ''], ['d', '1e5', 'soon', 'n/a']]),
    b: T('New', ['Name', 'Amount', 'When', 'Share'], [['a', '1000.00', '10/3/2026', '50.0%'], ['b', '15', 'October 17 2026', '4%'], ['c', '7.5', '2/28/2026', '10%'], ['d', '100000', 'later', 'N/A']]) },
  { a: T('Left', ['One', 'Two'], [['1', '2']]), b: T('Right', ['Three', 'Four'], [['1', '2'], ['3', '4']]) },
  { a: T('Full', ['Key', 'V'], [['k1', 'a'], ['k2', 'b']]), b: T('Empty', ['Key', 'V'], []) },
  { a: T('Empty', [], []), b: T('Also empty', [], []) },
  { a: T('Short rows', ['K', 'A', 'B'], [['r1', 'x'], ['r2'], ['r3', 'p', 'q', 'extra']]), b: T('Full rows', ['K', 'A', 'B'], [['r1', 'x', 'new'], ['r2', '', ''], ['r3', 'p', 'q']]) },
  { a: T('Dup heads', ['K', 'Note', 'Note', ''], [['1', 'a', 'b', 'c']]), b: T('Dup heads 2', ['K', 'Note', 'Note', ''], [['1', 'a', 'B', 'C']]) },
  { a: T('Spaces', ['K', 'Text'], [['1', '  two   words  '], ['2', 'Été à Zürich'], ['3', '<b>bold</b>']]), b: T('Tidy', ['K', 'Text'], [['1', 'two words'], ['2', 'Ete a Zurich'], ['3', '<script>alert(1)</script>']]) },
  { a: big(130, false), b: big(130, true) },
  { a: T('Many gone', ['K'], Array.from({ length: 70 }, (_, i) => ['g' + i])), b: T('Many new', ['K'], Array.from({ length: 65 }, (_, i) => ['n' + i])) },
  { a: T('Wide', Array.from({ length: 16 }, (_, i) => i === 0 ? 'Row id' : 'C' + i), [['1', ...Array(15).fill('a')], ['2', ...Array(15).fill('a')]]), b: T('Wide 2', Array.from({ length: 16 }, (_, i) => i === 0 ? 'Row id' : 'C' + i), [['1', ...Array(15).fill('b')], ['2', ...Array(15).fill('a')]]) },
  { a: T('Dates', ['Item', 'Goal'], [['x', '1/15/2027'], ['y', '3/1/2028'], ['z', '12/31/2026']]), b: T('Dates 2', ['Item', 'Goal'], [['x', '1/14/2027'], ['y', '2/29/2028'], ['z', '1/1/2027']]) },
  // the security review (round 26): values that a spreadsheet would read as formulas, and values far longer than a screen
  { a: T('=Plan.csv', ['Id', 'Value'], [['=1+1', 'x'], ['+2', 'y'], ['@gone', 'z'], ['-5', 'k'], ['\t7', 'q']]), b: T('Later', ['Id', 'Value'], [['=1+1', 'X'], ['+2', '=SUM(A1)'], ['-new', 'n'], ['-5', 'k'], ['\t7', 'q2'], ['＝wide', 'w']]) },
  { a: T('Long', ['Id', 'H' + 'h'.repeat(120), ''], [['1', 'a'.repeat(300) + '1', 'é'.repeat(198) + '😀 tail one'], ['K' + 'k'.repeat(400), 'same', ''], ['K' + 'k'.repeat(400) + '-gone', 'x', '']]),
    b: T('Long 2', ['Id', 'H' + 'h'.repeat(120), ''], [['1', 'a'.repeat(300) + '2', 'é'.repeat(198) + '😀 tail two'], ['K' + 'k'.repeat(400), 'changed', ''], ['K' + 'k'.repeat(400) + '-new', 'x', '']]) },
];
const INVITE = [
  'ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo.',
  'Team lunch 12/24/2026 at noon; bring the "good" plates, forks; and napkins\nSecond line with a comma, here.',
  'Kickoff is Feb 30. The real one is on March 3rd.',
  'Nothing here names a day. Version 3/4 of the plan, ratio 10/12.',
  'ANDA 216482 and ANDA 214990 both go out on 2027-01-05.',
  'Leap day review on Feb 29',
  'Renew by Jul 16',
  'Old news from Jul 15',
  'A very long first line ' + 'word '.repeat(40) + 'ends on Dec 1, 2026.',
  'Backslash \\ and emoji 😀 on Aug 9, 2026 ' + 'x'.repeat(900),
  '',
  '   ',
  // the security review (round 26): a selection that tries to add lines of its own to the file
  'Review on Dec 1, 2026\r\nATTENDEE:mailto:someone@example.invalid' + String.fromCharCode(0x2028) + 'BEGIN:VALARM' + String.fromCharCode(0x2029) + 'ACTION:PROCEDURE' + String.fromCharCode(0x85) + 'END:VEVENT\rORGANIZER:x\nX-BAD;PARAM=1:2'
    + String.fromCharCode(0, 7, 27) + '[31m' + String.fromCharCode(127, 11, 12) + ' end',
  String.fromCharCode(0, 1, 2) + String.fromCharCode(0x2028) + String.fromCharCode(27),
];
const USAGE = [
  { days: {}, today: '2026-11-18' },
  { days: { '2026-11-18': { card_open: 3, draft_check: 1 }, '2026-11-12': { card_open: 2, compare_files: 1 }, '2026-11-11': { card_open: 5 }, '2026-10-20': { card_open: 1, ask_open: 4 }, '2026-10-19': { ask_open: 9 }, '2026-10-01': { card_open: 100 } }, today: '2026-11-18' },
  { days: { '2026-11-19': { card_open: 7 }, 'not a day': { card_open: 7 }, '2026-11-18': { card_open: 1200, unknown_event: 5, note_added: -3, invitation_saved: 2.5, reading_prompt: 2 } }, today: '2026-11-18' },
];
(async () => {
  const b = await chromium.launch(); const p = await b.newPage(); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(([compare, invite, usageCases]) => {
    const table = t => t === 'SAMPLE_A' ? CMP_SAMPLE_A : t === 'SAMPLE_B' ? CMP_SAMPLE_B : t;
    return {
      about: 'Shared cases for round 26: Compare two files, the calendar invitation, the usage summary and the reading prompts. Written by the browser simulation with its clock fixed at Jul 16, 2026. The Windows app must give the same answers.',
      today: '2026-07-16',
      compare: compare.map(c => { const a = table(c.a), b2 = table(c.b), r = cmpTables(a, b2, c.key || null);
        return { a, b: b2, key: c.key || null, result: { key: r.key, keyWhy: r.keyWhy, shared: r.shared, same: r.same, changedRows: r.changedRows, changedCells: r.changedCells, added: r.added, addedCount: r.addedCount, removed: r.removed, removedCount: r.removedCount,
          blankA: r.blankA, blankB: r.blankB, dupA: r.dupA, dupB: r.dupB, colsOnlyA: r.colsOnlyA, colsOnlyB: r.colsOnlyB, byColumn: r.byColumn.map(x => x[0] + ' = ' + x[1]) },
          summary: cmpSummary(r, a.name, b2.name), report: cmpReport(r, a.name, b2.name), paste: cmpForPaste(cmpReport(r, a.name, b2.name)), digest: cmpDigest(r, a.name, b2.name) }; }),
      invite: invite.map((text, i) => { const r = icsFor(text, demoToday(), '20260716T120000Z', `case-${i}@notula`); return { text, result: r }; }),
      usage: usageCases.map(u => ({ days: u.days, today: u.today, lines: usageLines(u.days, u.today) })),
      usageEvents: USAGE_EVENTS.map(e => e[0] + ' = ' + e[1]),
      readPrompts: READ_PROMPTS.map(x => ({ name: x[0], template: x[1] })),
      aiTemplate: CMP_AI_TEMPLATE,
    };
  }, [COMPARE, INVITE, USAGE]);
  const text = JSON.stringify(doc) + '\n';
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${doc.compare.length} comparisons, ${doc.invite.length} invitations, ${doc.usage.length} usage cases, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${doc.compare.length} comparisons, ${doc.invite.length} invitations, ${doc.usage.length} usage cases, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with compare-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
