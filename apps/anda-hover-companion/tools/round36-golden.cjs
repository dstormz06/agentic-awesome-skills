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
    ].map(c => ({ ...c, result: changesSince(c.before, c.now, id => words[id] || null) }));
    // next week: the hours around Thursday noon, every day of one week, two weeks with a federal holiday, and the week before each demo date
    const weeks = [];
    const add = (t, hour, why) => weeks.push({ today: isoDay(t), hour, why, result: nextWeek(t, hour, DATA) });
    add(ymd(2026, 7, 16), 11, 'Thursday before noon'); add(ymd(2026, 7, 16), 12, 'Thursday at noon');
    for (let d = 17; d <= 22; d++) add(ymd(2026, 7, d), 9, 'a day of the week');
    add(ymd(2026, 11, 20), 15, 'Thanksgiving next week'); add(ymd(2026, 12, 18), 10, 'Christmas next week, on a Friday'); add(ymd(2026, 12, 24), 13, 'next week runs into the new year and ends on New Year\'s Day');
    Object.keys(DATA).sort().forEach(id => [DATA[id].goalD, DATA[id].fcD].forEach(parts => { if (!parts) return; const t = ymd(...parts), dw = dowOf(t); if (dw === 0 || dw === 6) return; add(t - (dw + 2) * DAY, 16, 'the Friday before ANDA ' + id + "'s date"); }));
    return {
      about: 'Shared cases for round 36, written by the browser simulation. The Windows app must give the same answers.',
      fingerprints: texts.map(t => ({ text: t, print: fingerprint(t) })), lookMax: LOOK_MAX, said: { first: LOOK_SAID.first, none: LOOK_SAID.none },
      demoLook: demo, cases, weeks, changes: CHANGES.find(c => c.round === 36),
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.fingerprints.length} fingerprints, ${doc.cases.length} pairs of looks, ${doc.weeks.length} days for next week`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round36-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
