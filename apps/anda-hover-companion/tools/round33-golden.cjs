// Writes (WRITE=1) or checks mockup/round33-golden.json: the answers of the browser simulation for the five features the Windows app takes
// over in round 33 (card details with Copy, text size and spacing, recent and kept ANDAs, One Bar, Today). The Windows app's tests
// (Round33Tests) must reproduce every answer.
// Run from this folder with Playwright available:  NODE_PATH=$(npm root -g) node round33-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round33-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const ids = Object.keys(DATA), all = DETAIL_FIELDS.map(f => f[0]), usual = state.cardFields.slice();
    const sets = [usual, all, [], ['applicant'], ['fei', 'applicant', 'nothing', 'applicant'], ['doc356h', 'doc32p1'], ['myNotes', 'applicantEmail'], null, 'applicant'];
    // recent and kept: each step is one press, and the lists after it
    const steps = [['look', '216482'], ['look', '214990'], ['look', '216482'], ['look', '12345'], ['look', 'abc'], ['look', '999999'], ['keep', '214990'], ['keep', '214990'], ['keep', '214990'], ['keep', '12a456'],
      ...['100001', '100002', '100003', '100004', '100005', '100006', '100007', '100008', '100009'].map(x => ['look', x]), ...['100001', '100002', '100003', '100004'].map(x => ['keep', x]), ['keep', '100005'], ['keep', '216482'],
      ['keep', '100002'], ['keep', '216482'], ['off', ''], ['look', '216482'], ['on', ''], ['look', '216482'], ['clear', '']];
    let recents = [], pins = [], on = true; const known = x => !!DATA[x] || /^1000/.test(x);
    const lists = steps.map(([what, id]) => {
      let result = null;
      if (what === 'look') recents = recentAfter(recents, id, on);
      else if (what === 'keep') { const r = pinAfter(pins, id); pins = r.pins; result = r.result; }
      else if (what === 'off') { on = false; recents = []; } else if (what === 'on') on = true; else recents = [];
      return { what, id, result, recents: recents.slice(), pins: pins.slice(), listed: listedOf(pins, recents, known), names: listedOf(pins, recents, known).map(x => recentName(x, pins.includes(x))) };
    });
    const typed = ['', '  ', '216482', 'ANDA 214990', '999999', 'N210793', 'NDA 021234', 'NDA No. 021234', '216 482', '216-482', '12345', 'Please confirm the goal date of ANDA 216482.', 'hello there', 'A216482'];
    const sels = ['', 'ANDA 216482 is on track.', 'Name\tGoal\nA\t1\nB\t2', '214990'];
    const display = {};
    for (const [ts, density] of [['standard', 'comfortable'], ['large', 'comfortable'], ['xl', 'compact'], ['standard', 'compact']]) {
      state.ts = ts; state.density = density; applyDisplay();
      const c = getComputedStyle(document.documentElement);
      display[ts + '/' + density] = { text: +c.getPropertyValue('--ts') || 1, spacing: +c.getPropertyValue('--sp') || 1 };
    }
    state.ts = 'standard'; state.density = 'comfortable'; applyDisplay();
    return {
      about: 'Shared cases for round 33, written by the browser simulation. The Windows app must give the same answers.',
      detailFields: DETAIL_FIELDS, detailDefault: usual, notInSheet: NOT_IN_SHEET, noneChosen: DETAILS_NONE_CHOSEN, copied: ['applicant', 'all the details shown'].map(w => ({ what: w, text: detailCopied(w) })),
      detailValues: ids.flatMap(id => [...all, 'short', 'nothing'].map(key => ({ id, key, value: detailValue(id, key) }))),
      details: ids.flatMap(id => sets.map(fields => ({ id, fields, result: detailRows(id, fields) }))),
      recentMax: RECENT_MAX, pinMax: PIN_MAX, pinFull: pinFull(), pinLabels: [pinLabel(false), pinLabel(true)], recentsSaid: RECENTS_SAID, lists,
      hero: ids.map(id => ({ id, text: heroSentence(id) })),
      bar: typed.flatMap(t => sels.map(sel => ({ typed: t, sel, rows: barRows(t, sel) }))),
      lookupErrors: [...typed, 'n-210793', 'BLA 125000', 'ndc 0000-1111', 'DMF 012345', null].map(t => ({ typed: t, text: lookupError(t) })),
      todaySaid: TODAY_SAID,
      today: [[[], []], [['214990'], ['216482', '214990']], [['999999', '216482'], ['214990', '888888']], [[], ['214990']]].map(([pins, recents]) => ({ pins, recents, groups: todayGroups(pins, recents) })),
      todayMain: ids.map(id => ({ id, result: todayMain(id) })),
      todayShown: ids.map(id => ({ id, text: todayShown(id) })), todayMarks: TODAY_MARK,
      detailNames: DETAIL_FIELDS.map(f => ({ label: f[1], name: detailName(f[1]) })),
      nearNumbers: ['N210793', 'n-210793', 'NDA 021234', 'NDA No. 021234', 'nda#021234', 'BLA 125000', 'DMF 012345', 'NDC 0000-1111', '216 482', '216-482', '216482', 'NDA 021234 is late, please check', 'N2107931', 'hello'].map(t => ({ typed: t, near: nearNumber(t) })),
      pinsCleared: RECENTS_SAID.pinsCleared,
      display,
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.details.length} views of the details, ${doc.detailValues.length} values, ${doc.lists.length} steps of the lists, ${doc.bar.length} sets of One Bar rows, ${doc.lookupErrors.length} lookup messages, ${doc.today.length} views of Today, ${Object.keys(doc.display).length} display choices`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round33-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
