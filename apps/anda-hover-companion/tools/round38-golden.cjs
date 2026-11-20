// Writes (WRITE=1) or checks mockup/round38-golden.json: the answers of the browser simulation for round 38 (Today's "Next week" also lists
// the person's to-do items due Monday to Friday of next week, from both lists). The Windows app's tests (Round38Tests) must give the same.
// Run from this folder:  NODE_PATH=$(npm root -g) node round38-golden.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const DIR = require('path').join(__dirname, '..', 'mockup') + '/';
const OUT = DIR + 'round38-golden.json';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ timezoneId: 'America/New_York' }); const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + DIR + 'windows-simulation.html?clock=fixed'); await p.waitForTimeout(300);
  const doc = await p.evaluate(() => {
    const A = 'ANDA to-do list', M = 'My to-do list';
    const records = { '330001': { goal: '2026-12-02', forecast: '2026-12-04' } };
    const data = {}; Object.keys(records).forEach(id => { const q = s => s ? s.split('-').map(Number) : null; data[id] = { goalD: q(records[id].goal), fcD: q(records[id].forecast) }; });
    const many = []; for (let i = 0; i < 25; i++) many.push({ list: i % 2 ? A : M, title: `Fictional task ${String(i).padStart(2, '0')}`, due: '2026-12-0' + (1 + (i % 4)) });   // Tue Dec 1 to Fri Dec 4
    const sets = [
      { name: 'both lists, the same day: by list, then title; one before and one after the week left out', tasks: [
        { list: M, title: 'send the weekly status update', due: '2026-12-01' }, { list: A, title: 'Call the applicant about 330001', due: '2026-12-01' },
        { list: M, title: 'Book the team room', due: '2026-12-01' }, { list: M, title: 'Too early', due: '2026-11-27' }, { list: A, title: 'Too late', due: '2026-12-07' },
        { list: A, title: 'Friday item', due: '2026-12-04' }, { list: M, title: 'Monday item', due: '2026-11-30' }] },
      { name: 'a date that cannot be read, a blank title, spaces around a title', tasks: [
        { list: M, title: 'Never shown', due: '2026-12-32' }, { list: A, title: '   ', due: '2026-12-01' }, { list: M, title: '  Trimmed title  ', due: '2026-12-02' }, { list: A, title: 'No date', due: null }] },
      { name: 'twenty-five items: twenty shown, then how many more', tasks: many },
      { name: 'to-do items only, no ANDA dates: the week is not empty', tasks: [{ list: M, title: 'Only item', due: '2026-12-03' }], noRecords: true },
      { name: 'nothing at all', tasks: [], noRecords: true },
    ];
    const today = ymd(2026, 11, 27);     // a Friday: next week is Mon Nov 30 to Fri Dec 4
    return {
      about: 'Shared cases for round 38, written by the browser simulation. The Windows app must give the same answers.',
      today: isoDay(today), hour: 10, records, taskMax: WEEK_TASK_MAX, more: [1, 2, 7].map(n => ({ n, text: weekTasksMore(n) })),
      sets: sets.map(c => ({ name: c.name, tasks: c.tasks, noRecords: !!c.noRecords, result: nextWeek(today, 10, c.noRecords ? {} : data, c.tasks) })),
      changes: CHANGES.find(c => c.round === 38),
    };
  });
  const text = JSON.stringify(doc) + '\n';
  const counts = `${doc.sets.length} sets of to-do items for next week, ${doc.more.length} sentences of how many more`;
  if (process.env.WRITE === '1') { fs.writeFileSync(OUT, text); console.log(`wrote ${counts}, ${text.length} bytes; page errors: ${JSON.stringify(errs)}`); }
  else { const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''; console.log(have === text ? `${counts}, 0 mismatches; page errors: ${JSON.stringify(errs)}` : 'MISMATCH with round38-golden.json'); if (have !== text || errs.length) process.exitCode = 1; }
  await b.close();
})();
