// Writes (WRITE=1) or checks mockup/round36-golden.json: the answers of the browser simulation for round 36 (Today's "Since you last
// looked": the fingerprint of an open item, the look kept of the demo records, what moved between two looks; and "Next week": when it is
// shown and what it lists). The Windows app's tests (Round36Tests) must reproduce every answer.
// Run from this folder:  NODE_PATH=$(npm root -g) node round36-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round36-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const texts = ['', 'IR response due in 5 days — Biopharmaceutics', 'Deficiency letter: DMF', 'ñ é 😀 字', 'a'.repeat(300)];
    const at = '2026-07-16T14:42:00.000Z', demo = lookNow(DATA, Date.parse(at));
    const L = (goal, forecast, item) => ({ goal, forecast, item });
    const look = items => ({ at, items });
    const three = {}, threeNow = {}; for (let i = 0; i < 20; i++) { const id = String(310000 + i); three[id] = L('2027-01-04', '2027-01-04', null); threeNow[id] = L('2027-01-05', '2027-01-06', 'abcd1234'); }
    const many = {}, manyNow = {}; for (let i = 0; i < 45; i++) { const id = String(300000 + i); many[id] = L('2027-01-04', '2027-01-04', null); manyNow[id] = L('2027-01-04', '2027-01-05', null); }
    const words = { '216482': 'IR response due in 5 days — Biopharmaceutics', '300001': 'Labeling comments due' };
    const cases = [
      { name: 'the demo against its sample earlier look', before: sampleLastLook(), now: demo },
      { name: 'nothing kept yet', before: null, now: demo },
      { name: 'the same look twice', before: demo, now: demo },
      { name: 'goal set, goal removed, forecast set, forecast removed', before: look({ '300001': L(null, '2027-02-01', null), '300002': L('2027-02-01', null, null), '300003': L('2027-02-01', '2027-02-02', null) }),
        now: look({ '300001': L('2027-02-01', '2027-02-01', null), '300002': L(null, '2027-02-03', null), '300003': L('2027-02-01', null, null) }) },
      { name: 'one day earlier, a new item, an item gone, a number that is not six digits', before: look({ '300001': L('2027-03-01', '2027-03-10', null), '300002': L('2027-03-01', '2027-03-01', 'abcd1234'), '12345': L(null, null, null) }),
        now: look({ '300001': L('2027-03-01', '2027-03-09', 'ffff0000'), '300002': L('2027-03-01', '2027-03-01', null), '1234567': L(null, null, null) }) },
      { name: 'forty-five changes: forty shown', before: look(many), now: look(manyNow) },
      // added after the review of round 36
      { name: 'dates that cannot be read count as none', before: look({ '300001': L('x', '2027-02-30', null), '300002': L('2027-02-01', 'nope', 'abcd1234'), '300003': L(null, '2027-13-01', null) }),
        now: look({ '300001': L('2027-02-01', '2027-02-10', null), '300002': L('2027-02-01', '2027-02-10', 'abcd1234'), '300003': L(null, null, null) }) },
      { name: 'the fortieth change is in the middle of an ANDA: that ANDA is shown whole', before: look(three), now: look(threeNow) },
    ].map(c => ({ ...c, result: changesSince(c.before, c.now, id => words[id] || null) }));
    // next week: the hours around Thursday noon, every day of one week, two weeks with a federal holiday, and the week before each demo date
    const weeks = [];
    const add = (t, hour, why) => weeks.push({ today: isoDay(t), hour, why, result: nextWeek(t, hour, DATA) });
    add(ymd(2026, 7, 16), 11, 'Thursday before noon'); add(ymd(2026, 7, 16), 12, 'Thursday at noon');
    for (let d = 17; d <= 22; d++) add(ymd(2026, 7, d), 9, 'a day of the week');
    add(ymd(2026, 11, 20), 15, 'Thanksgiving next week'); add(ymd(2026, 12, 18), 10, 'Christmas next week, on a Friday'); add(ymd(2026, 12, 24), 13, 'next week runs into the new year and ends on New Year\'s Day');
    Object.keys(DATA).sort().forEach(id => [DATA[id].goalD, DATA[id].fcD].forEach(parts => { if (!parts) return; const t = ymd(...parts), dw = dowOf(t); if (dw === 0 || dw === 6) return; add(t - (dw + 2) * DAY, 16, 'the Friday before ANDA ' + id + "'s date"); }));
    // next week with records made for it: two ANDAs and two dates in one week, one without a forecast, one without a goal date, and the
    // weeks with a holiday observed on a Friday (Independence Day 2026; New Year's Day 2028, observed on Fri Dec 31, 2027)
    const records = { '320001': { goal: '2026-07-01', forecast: '2026-06-30' }, '320002': { goal: '2026-06-29', forecast: null }, '320003': { goal: null, forecast: '2026-07-02' },
      '320006': { goal: '2026-07-01', forecast: null },   // the same day as 320001's goal: the lower number first
      '320004': { goal: '2027-12-29', forecast: '2027-12-27' }, '320005': { goal: '2027-12-30', forecast: '2027-12-30' } };
    const data = {}; Object.keys(records).forEach(id => { const p = s => s ? s.split('-').map(Number) : null; data[id] = { goalD: p(records[id].goal), fcD: p(records[id].forecast) }; });
    const madeWeeks = [[ymd(2026, 6, 26), 9, 'the week of Independence Day, observed on Friday Jul 3'], [ymd(2027, 12, 23), 12, 'the last week of 2027: New Year\'s Day observed on Friday Dec 31'],
      [ymd(2026, 6, 25), 11, 'Thursday before noon, with records due next week']].map(([t, hour, why]) => ({ today: isoDay(t), hour, why, result: nextWeek(t, hour, data) }));
    return {
      about: 'Shared cases for round 36, written by the browser simulation. The Windows app must give the same answers.',
      fingerprints: texts.map(t => ({ text: t, print: fingerprint(t) })), lookMax: LOOK_MAX, said: { first: LOOK_SAID.first, none: LOOK_SAID.none },
      demoLook: demo, cases, weeks, made: { records, weeks: madeWeeks }, changes: CHANGES.find(c => c.round === 36),
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.fingerprints.length} fingerprints, ${doc.cases.length} pairs of looks, ${doc.weeks.length} days for next week, ${doc.made.weeks.length} with records made for them`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round36-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
